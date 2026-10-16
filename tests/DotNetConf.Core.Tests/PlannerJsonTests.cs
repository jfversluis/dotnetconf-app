using System.Text.Json;
using DotNetConf.App.Services;
using DotNetConf.Core;

namespace DotNetConf.Core.Tests;

public sealed class PlannerJsonTests
{
    [Fact]
    public void ExtractedPreferencesDeserializeWithoutReflection()
    {
        var interpretation = JsonSerializer.Deserialize(
            """{"speakerNames":"Alex","availabilityText":"none","searchQuery":"none","quantity":"all"}""",
            PlannerJsonContext.Default.PlannerInterpretation)!;
        Assert.Equal("Alex", interpretation.SpeakerNames);
        Assert.Equal("none", interpretation.AvailabilityText);
        Assert.Equal("all", interpretation.Quantity);
        var availability = JsonSerializer.Deserialize(
            """{"windows":[{"start":"2026-11-11T19:00:00Z","end":"2026-11-11T20:00:00+00:00","zone":"UTC"}]}""",
            PlannerJsonContext.Default.PlannerAvailability)!;
        var window = Assert.Single(availability.Windows).ToWindow();
        Assert.Equal(new AvailabilityWindow("2026-11-11T19:00", "2026-11-11T20:00", "UTC"), window);
    }

    [Theory]
    [InlineData("2026-11-11T19:00:01", "UTC")]
    [InlineData("2026-11-11T19:00:00+01:00", "UTC")]
    [InlineData("2026-11-11T19:00Z", "local")]
    [InlineData("2026-02-30T19:00", "UTC")]
    [InlineData("tomorrow evening", "UTC")]
    public void NormalizationDoesNotDiscardPrecisionOffsetsOrInvalidDates(string start, string zone)
    {
        Assert.Throws<InvalidDataException>(() => new PlannerWindow(start, "2026-11-11T20:00", zone).ToWindow());
    }

    [Fact]
    public void LocalClockTimesAreNotConvertedByTheModelAdapter()
    {
        var window = new PlannerWindow("2026-11-10T17:00:00", "2026-11-10T18:00", "local").ToWindow();
        Assert.Equal("2026-11-10T17:00", window.Start);
        Assert.Equal("local", window.Zone);
        Assert.Throws<InvalidDataException>(() => new PlannerWindow(null!, "", "UTC").ToWindow());
    }

    [Fact]
    public void ExplicitAvailabilityCannotSilentlyBecomeUnrestrictedOrLoseAWindow()
    {
        Assert.Throws<InvalidDataException>(() => new PlannerAvailability([]).ToWindows(TimeZoneInfo.Utc));
        Assert.Throws<InvalidDataException>(() => new PlannerAvailability(null!).ToWindows(TimeZoneInfo.Utc));
        Assert.Throws<InvalidDataException>(() => new PlannerAvailability([null!]).ToWindows(TimeZoneInfo.Utc));
        var window = new PlannerWindow("2026-11-10T16:00", "2026-11-10T18:00", "UTC");
        Assert.Throws<InvalidDataException>(() =>
            new PlannerAvailability(Enumerable.Repeat(window, 13).ToArray()).ToWindows(TimeZoneInfo.Utc));
        Assert.Single(new PlannerAvailability([window]).ToWindows(TimeZoneInfo.Utc));
    }

    [Theory]
    [InlineData("none", false)]
    [InlineData("", false)]
    [InlineData("November 10 between 17:00 and 18:00", true)]
    public void ExplicitEmptyAvailabilityMarkerMeansUnrestricted(string value, bool hasAvailability)
    {
        var interpretation = JsonSerializer.Deserialize(
            $$"""{"speakerNames":"none","availabilityText":"{{value}}","searchQuery":"Mobile","quantity":"some"}""",
            PlannerJsonContext.Default.PlannerInterpretation)!;
        Assert.Equal(hasAvailability, interpretation.HasAvailability);
    }

    [Fact]
    public void RedundantOffsetsAreRemovedOnlyWhenConsistentWithTheNamedZone()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var window = new PlannerWindow("2026-11-10T17:00:00+01:00", "2026-11-10T18:00:00+01:00", "local").ToWindow(zone);
        Assert.Equal("2026-11-10T17:00", window.Start);
        Assert.Equal("local", window.Zone);
        Assert.Throws<InvalidDataException>(() =>
            new PlannerWindow("2026-11-10T17:00:00-05:00", "2026-11-10T18:00", "local").ToWindow(zone));
    }

    [Theory]
    [InlineData("Mobile", "Mobile", "I want to build phone apps", "Mobile")]
    [InlineData("Accessibility", "none", "I care about accessibility", "Accessibility")]
    [InlineData("none", ".NET MAUI", "Find talks about Blazor", "Blazor")]
    public async Task CatalogTopicsCannotBecomeRequiredSpeakers(string names, string inferred, string request, string query)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var grounded = new PlannerInterpretation(names, "none", inferred, "some").Ground(conference, request);
        Assert.Empty(grounded.Speakers);
        Assert.Equal(query, grounded.Query);
    }

    [Fact]
    public async Task GroundingDoesNotDropUnknownPeopleOrRenameRealPeople()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        foreach (var name in new[] { "Ada Lovelace", "Alex", "Scott" })
            Assert.Equal([name], new PlannerInterpretation(name, "none", "none", "some")
                .Ground(conference, $"I must see {name}").Speakers);
    }

    [Theory]
    [InlineData("I don't want to miss Ada Lovelace", "Ada Lovelace")]
    [InlineData("Don't miss Ada", "Ada")]
    [InlineData("I don’t want to miss Ada Lovelace", "Ada Lovelace")]
    [InlineData("Do not miss Ada Lovelace", "Ada Lovelace")]
    [InlineData("I want to hear Ada Lovelace", "Ada Lovelace")]
    [InlineData("Catch Ada Lovelace and all the AI talks", "Ada Lovelace")]
    [InlineData("Ada Lovelace's sessions are a must", "Ada Lovelace")]
    public async Task UnknownExtractedSpeakerRemainsABlockingConstraint(string request, string name)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var grounded = new PlannerInterpretation(name, "none", "AI", "all").Ground(conference, request);
        Assert.Equal([name], grounded.Speakers);
        var review = SchedulePlanner.Review(conference, new([], grounded.Speakers, []), TimeZoneInfo.Utc, []);
        Assert.False(review.CanApply);
        Assert.Contains(review.Errors, error => error.StartsWith("Speaker not found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ModelCannotTurnBackendWordsOrUnmentionedRealPeopleIntoRequiredSpeakers()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        Assert.Empty(new PlannerInterpretation("Backend", "none", "APIs", "some")
            .Ground(conference, "I need to build HTTP endpoints for my backend").Speakers);
        Assert.Empty(new PlannerInterpretation("Daniel Roth", "none", "APIs", "some")
            .Ground(conference, "I need to build HTTP endpoints for my backend").Speakers);
        Assert.Equal(["Ada Lovelace"], new PlannerInterpretation("Ada Lovelace", "none", "none", "some")
            .Ground(conference, "I must see Ada Lovelace").Speakers);
        Assert.Equal(["Scott"], new PlannerInterpretation("Scott Hunter", "none", "none", "all")
            .Ground(conference, "All talks by Scott").Speakers);
        Assert.Equal(["Gerald"], new PlannerInterpretation("none", "none", "none", "all")
            .Ground(conference, "Everything from Gerald").Speakers);
        Assert.Equal(["Scot"], new PlannerInterpretation("Scot", "none", "none", "all")
            .Ground(conference, "All talks by Scot").Speakers);
    }

    [Theory]
    [InlineData("All talks on MAUUI", ".NET MAUI")]
    [InlineData("Everything about Blazorrr", "Blazor")]
    [InlineData("gimme all the perf talks pls", "Performance")]
    [InlineData("Everything on performnce", "Performance")]
    [InlineData("I want to build phone apps", "Mobile")]
    public async Task CatalogAwareSpellingAndShorthandDoNotNeedPerTypoAliases(string request, string topic)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        Assert.Equal([topic], Agenda.MentionedTopics(conference, request));
    }

    [Fact]
    public async Task MixedAllRequestsKeepIndependentTopicAndSpeakerQuantities()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var interpretation = JsonSerializer.Deserialize(
            """{"speakerNames":"Ortinau","availabilityText":"none","searchQuery":"Mobile","quantity":"all"}""",
            PlannerJsonContext.Default.PlannerInterpretation)!;
        var grounded = interpretation.Ground(conference, "All sessions about C# and everything from Ortinau");
        Assert.Equal(["Ortinau"], grounded.Speakers);
        Assert.Equal("C#", grounded.Query);
        Assert.Equal("all", interpretation.Quantity);
        Assert.True(interpretation.RequestsAllSpeakers("All sessions about C# and everything from Ortinau"));
        var topic = JsonSerializer.Deserialize("""{"text":"C#","topics":["C#"],"quantity":"all","match":"any","speakerScope":"independent"}""",
            PlannerJsonContext.Default.PlannerTopicRequest)!;
        Assert.Equal("C#", topic.Text);
        Assert.True(topic.HasInterest(conference, "All sessions about C# and everything from Ortinau"));
        Assert.True(topic.RequestsAll("All sessions about C# and everything from Ortinau"));
    }

    [Fact]
    public void TopicQuantityIsIndependentOfAllSpeakerQuantity()
    {
        var request = "Everything from Ortinau and some phone apps";
        Assert.True(new PlannerInterpretation("Ortinau", "none", "Mobile", "all").RequestsAllSpeakers(request));
        Assert.False(new PlannerTopicRequest("Mobile", ["Mobile"], "some", "any", "independent").RequestsAll(request));
    }

    [Theory]
    [InlineData("Ortinau", false)]
    [InlineData("Show every talk by Ortinau", false)]
    [InlineData("all sessions by Mads", false)]
    [InlineData("C# and everything from Ortinau", true)]
    [InlineData("phone apps", true)]
    public async Task CopiedSpeakerRequestsAreNotEvidenceOfTopicalInterests(string evidence, bool expected)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        Assert.Equal(expected, new PlannerTopicRequest(evidence, [], "all", "any", "independent").HasInterest(conference, evidence));
        Assert.False(new PlannerTopicRequest("none", [], "all", "any", "independent").HasInterest(conference, evidence));
        Assert.Equal(expected,
            new PlannerTopicRequest("a semantic paraphrase", [], "all", "any", "independent").HasInterest(conference, evidence));
    }

    [Theory]
    [InlineData("I'm into web stuff")]
    [InlineData("Help me make my code faster")]
    [InlineData("I must see Ortinau")]
    public void ModelCannotInventAnAllSessionsConstraintWithoutAnExplicitQuantifier(string request)
    {
        var interpretation = new PlannerInterpretation("none", "none", "Web", "all");
        Assert.False(interpretation.RequestsAllSpeakers(request));
        Assert.False(new PlannerTopicRequest("Web", ["Web"], "all", "any", "independent").RequestsAll(request));
    }

    [Fact]
    public async Task TopicListsPreserveLiteralTopicsAndSpeakerScopeWithoutSubstringMatches()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var filter = new PlannerTopicRequest("AI, MAUI or Blazor", ["AI"], "all", "all", "independent")
            .ToFilter(conference, "Everything about AI, MAUI or Blazor")!;
        Assert.Equal([".NET MAUI", "AI", "Blazor"], filter.Topics.Order(StringComparer.Ordinal));
        Assert.False(filter.MatchAll);
        Assert.True(filter.IncludeAll);
        var independent = new PlannerTopicRequest("C#", ["C#", "Mobile"], "all", "any", "within-topics")
            .ToFilter(conference, "All C# sessions and everything from Ortinau")!;
        Assert.False(independent.RestrictToRequiredSpeakers);
        Assert.Equal(["C#", "Mobile"], independent.Topics);
        var scope = new PlannerTopicRequest("MAUI", [".NET MAUI"], "all", "any", "independent")
            .ToFilter(conference, "All of Maddy's MAUI sessions", hasSpeakers: true)!;
        Assert.True(scope.RestrictToRequiredSpeakers);
        Assert.Throws<InvalidDataException>(() =>
            new PlannerTopicRequest("Rust", [], "all", "any", "independent").ToFilter(conference, "Everything about Rust"));
        Assert.Throws<InvalidDataException>(() =>
            new PlannerTopicRequest("MAUI talks and Gerald's C# talks", [".NET MAUI", "C#"], "all", "any", "independent")
                .ToFilter(conference, "Maddy's MAUI talks and Gerald's C# talks"));
        Assert.True(new PlannerInterpretation("Maddy", "none", "none", "all")
            .RequestsAllSpeakers("Give me everything about MAUI and don't miss a session from Maddy"));
    }

    [Fact]
    public async Task InferredInterestsSurviveAlongsideLiteralTopicsWithoutAnAliasDictionary()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var filter = new PlannerTopicRequest("mobile, AI and perf", ["Mobile", "AI", "Performance"],
            "all", "any", "within-topics")
            .ToFilter(conference, "I like mobile, AI and I want to see everything on perf")!;
        Assert.Equal(["Mobile", "AI", "Performance"], filter.Topics);
        Assert.False(filter.RestrictToRequiredSpeakers);
        Assert.Contains(SchedulePlanner.TopicMatches(conference, new([], [], []) { TopicFilter = filter }),
            s => s.Id == "runtime-profiling");
    }

    [Theory]
    [InlineData("I like mobile development and AI and want to get the latest on performance", "Mobile")]
    [InlineData("I like MAUI and AI and performance", ".NET MAUI")]
    [InlineData("Everything about AI and MAUI and performance", ".NET MAUI")]
    [InlineData("I like phone apps, AI and performance", "Mobile")]
    public async Task InterestListsAreUnionsAndKeepAliasesAlongsideLiteralTopics(string request, string mobileTopic)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var filter = new PlannerTopicRequest("AI", ["AI"], "some", "all", "independent")
            .ToFilter(conference, request)!;
        Assert.False(filter.MatchAll);
        Assert.Equal(new[] { mobileTopic, "AI", "Performance" }.Order(), filter.Topics.Order());
        var matches = SchedulePlanner.TopicMatches(conference, new([], [], []) { TopicFilter = filter }).ToArray();
        Assert.Contains(matches, s => s.Id == "runtime-profiling");
        Assert.Contains(matches, s => s.Id == "building-with-maui");
        Assert.Contains(matches, s => s.Id == "blazor-ai");
    }

    [Theory]
    [InlineData("Everything about both AI and MAUI")]
    [InlineData("Talks covering AI and MAUI together")]
    [InlineData("AI and MAUI in the same session")]
    [InlineData("AI and MAUI, each talk must cover all these topics")]
    public async Task ExplicitCombinedTalkRequestsStillIntersectTopics(string request)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var filter = new PlannerTopicRequest("AI and MAUI", ["AI", ".NET MAUI"], "some", "any", "independent")
            .ToFilter(conference, request)!;
        Assert.True(filter.MatchAll);
        Assert.Equal(["maddy-maui-ai", "maui-local-ai"], SchedulePlanner.TopicMatches(conference,
            new([], [], []) { TopicFilter = filter }).Select(s => s.Id).Order());
    }

    [Theory]
    [InlineData("I want all the latest on C# but exclude sessions after 10pm for me", true)]
    [InlineData("All C# sessions must finish by 22:00", true)]
    [InlineData("Just everything from Gerald, no matter the time", false)]
    [InlineData("I want the latest on C#", false)]
    public void ExplicitClockLimitsCannotBeDroppedByAnEmptyModelMarker(string request, bool expected)
    {
        Assert.Equal(expected, new PlannerInterpretation("none", "none", "none", "some").RequiresTimeExtraction(request));
        Assert.False(PlannerInterpretation.HasExplicitDate(request));
    }

    [Fact]
    public async Task SpeakerAndTimeOnlyRequestsCannotAcquireTopicsFromParaphrasedModelEvidence()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        foreach (var request in new[]
        {
            "Don't miss any session from Gerald, but exclude sessions ending after 10pm local time",
            "A session with Alex on November 10, 2026, between 17:00 and 18:00 local time."
        })
            Assert.Null(new PlannerTopicRequest("a paraphrase not in the request", ["Mobile"], "some", "any", "independent")
                .ToFilter(conference, request, hasSpeakers: true));
    }

    [Theory]
    [InlineData("Exclude sessions if they are after 10pm for me", "none", "22:00")]
    [InlineData("All C# sessions must finish by 22:00 UTC", "none", "22:00")]
    [InlineData("No talks after 10pm local time", "none", "22:00")]
    [InlineData("I can watch after 9pm", "21:00", "none")]
    [InlineData("Finish before 12am", "none", "00:00")]
    public void ASingleExplicitClockCannotAcquireAnInventedStartOrChangedCutoff(string request, string start, string end)
    {
        var grounded = new PlannerDailyAvailability("09:00", "20:00", "local").Ground(request);
        Assert.Equal(start, grounded.DailyStart);
        Assert.Equal(end, grounded.DailyEnd);
    }

    [Theory]
    [InlineData("Finish before 25:00")]
    [InlineData("Finish before 13pm")]
    [InlineData("Finish before 0am")]
    [InlineData("Finish before 9:99")]
    public void InvalidExplicitClocksAreNotReplacedWithModelGuesses(string request) =>
        Assert.Throws<InvalidDataException>(() => new PlannerDailyAvailability("none", "22:00", "local").Ground(request));

    [Fact]
    public void MultipleClocksPreserveTheSeparateWindowExtraction()
    {
        var extracted = new PlannerDailyAvailability("09:00", "22:00", "local");
        Assert.Equal(extracted, extracted.Ground("Only between 09:00 and 22:00"));
    }

    [Fact]
    public void RequestedZoneWinsWithoutConvertingClockTimesOrDiscardingInconsistentOffsets()
    {
        var local = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var sourceZone = PlannerInterpretation.RequestedZone("November 10 from 08:00 to 09:00 UTC");
        Assert.Equal("UTC", sourceZone);
        var interpreted = new PlannerAvailability(
            [new("2026-11-10T08:00:00+01:00", "2026-11-10T09:00:00+01:00", "local")]);
        var window = Assert.Single(interpreted.InZone(sourceZone, local).ToWindows(local));
        Assert.Equal(new AvailabilityWindow("2026-11-10T08:00", "2026-11-10T09:00", "UTC"), window);
        Assert.Throws<InvalidDataException>(() =>
            (interpreted with { Windows = [new("2026-11-10T08:00+02:00", "2026-11-10T09:00+02:00", "local")] })
                .InZone(sourceZone, local));
        Assert.Equal("local", PlannerInterpretation.RequestedZone("Finish by 10pm for me"));
        Assert.Equal("America/New_York", PlannerInterpretation.RequestedZone("Finish by 22:00 America/New_York"));
        Assert.Throws<InvalidDataException>(() => PlannerInterpretation.RequestedZone("One window UTC and one Europe/Amsterdam"));
    }

    [Fact]
    public async Task DailyLimitsUseAgendaDatesInTheRequestedZoneRatherThanInventingDates()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var windows = new PlannerAvailability([], DailyEnd: "22:00").ToWindows(zone, conference);
        var daily = JsonSerializer.Deserialize(
            """{"dailyStart":"none","dailyEnd":"22:00","zone":"local"}""",
            PlannerJsonContext.Default.PlannerDailyAvailability)!;
        Assert.Equal(windows, daily.ToWindows(zone, conference));
        Assert.True(PlannerInterpretation.HasExplicitDate("November 10, 2026 between 17:00 and 18:00"));
        Assert.Equal("2026-11-10T00:00", windows[0].Start);
        Assert.Equal("2026-11-10T22:00", windows[0].End);
        Assert.Equal("2026-11-13T22:00", windows[^1].End);
        Assert.All(windows, w => Assert.Equal("local", w.Zone));
        Assert.Throws<InvalidDataException>(() => new PlannerAvailability([], DailyEnd: "25:00").ToWindows(zone, conference));
        Assert.Throws<InvalidDataException>(() => new PlannerAvailability([], DailyEnd: "22:00", Zone: "Unknown/Zone").ToWindows(zone, conference));
        Assert.Throws<InvalidDataException>(() =>
            new PlannerAvailability([new("2026-11-10T10:00", "2026-11-10T11:00", "local")], DailyEnd: "22:00")
                .ToWindows(zone, conference));
    }

    [Fact]
    public async Task SchemasConstrainSemanticDecisionsToRealTopicsWithoutEmbeddingTheFullCatalog()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var topics = PlannerSchemas.Interpretation(conference).GetProperty("properties")
            .GetProperty("searchQuery").GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("Web", topics);
        Assert.Contains("Mobile", topics);
        Assert.Contains("none", topics);
        Assert.DoesNotContain("building-with-maui", topics);
        var classifiedTopics = PlannerSchemas.ClassifyTopic(conference).GetProperty("properties")
            .GetProperty("topic").GetProperty("enum")
            .EnumerateArray().Select(e => Assert.IsType<string>(e.GetString())).ToArray();
        Assert.Equal(topics.Order(), classifiedTopics.Order());
        Assert.Equal(2, PlannerSchemas.TopicIntent.GetProperty("properties").EnumerateObject().Count());
    }
}
