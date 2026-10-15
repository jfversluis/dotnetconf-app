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
        var topic = JsonSerializer.Deserialize("""{"text":"C#","quantity":"all"}""",
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
        Assert.False(new PlannerTopicRequest("Mobile", "some").RequestsAll(request));
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
        Assert.Equal(expected, new PlannerTopicRequest(evidence, "all").HasInterest(conference, evidence));
        Assert.False(new PlannerTopicRequest("none", "all").HasInterest(conference, evidence));
        Assert.Throws<InvalidDataException>(() =>
            new PlannerTopicRequest("invented interest", "all").HasInterest(conference, evidence));
    }

    [Theory]
    [InlineData("I'm into web stuff")]
    [InlineData("Help me make my code faster")]
    [InlineData("I must see Ortinau")]
    public void ModelCannotInventAnAllSessionsConstraintWithoutAnExplicitQuantifier(string request)
    {
        var interpretation = new PlannerInterpretation("none", "none", "Web", "all");
        Assert.False(interpretation.RequestsAllSpeakers(request));
        Assert.False(new PlannerTopicRequest("Web", "all").RequestsAll(request));
    }

    [Fact]
    public async Task SchemasGroundTopicsAndSelectionsWithoutEmbeddingTheCatalog()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        var topics = PlannerSchemas.Interpretation(conference).GetProperty("properties")
            .GetProperty("searchQuery").GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("Web", topics);
        Assert.Contains("Mobile", topics);
        Assert.Contains("none", topics);
        Assert.DoesNotContain("building-with-maui", topics);
        var ids = PlannerSchemas.Selection(["blazor-web", "aspnet-apis"]).GetProperty("properties")
            .GetProperty("sessionIds").GetProperty("items").GetProperty("enum")
            .EnumerateArray().Select(e => Assert.IsType<string>(e.GetString())).ToArray();
        Assert.Equal(["blazor-web", "aspnet-apis"], ids);
    }
}
