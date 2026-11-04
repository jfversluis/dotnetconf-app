using DotNetConf.App.Services;
using DotNetConf.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetConf.Core.Tests;

[Trait("Category", "AIAttributes")]
public sealed class PlannerScaleTests
{
    private static ConferenceDocument Catalog(int count) => new()
    {
        Speakers = [new() { Id = "early", Name = "Early Speaker" }, new() { Id = "late", Name = "Late Speaker" }],
        Sessions = Enumerable.Range(0, count).Select(i => new Session
        {
            Id = $"session-{i}", Title = $"Synthetic talk {i}",
            Topics = [i % 2 == 0 ? "Performance" : "AI"],
            SpeakerIds = [i == count - 1 ? "late" : "early"],
            StartsAt = DateTimeOffset.Parse("2026-11-10T00:00Z").AddMinutes(i * 15),
            EndsAt = DateTimeOffset.Parse("2026-11-10T00:00Z").AddMinutes(i * 15 + 15)
        }).ToList()
    };

    [Fact]
    public void CompactCatalogKeepsAllTopicsAtOneHundredSessions()
    {
        var conference = Catalog(100);
        var text = PlannerCatalog.DescribeTopics(conference);
        Assert.Contains("Performance:", text);
        Assert.Contains("AI:", text);
        Assert.True(text.Length < 1000);
        Assert.Equal(PlannerCatalog.DescribeTopics(Catalog(2)), text);
        Assert.Equal(text, PlannerCatalog.DescribeTopics(Catalog(250)));
        conference.Sessions[0] = conference.Sessions[0] with { Title = new string('x', PlannerCatalog.MaximumCharacters) };
        Assert.Throws<InvalidDataException>(() => PlannerCatalog.DescribeTopics(conference));
    }

    [Fact]
    public async Task OversizedCatalogFunctionFailsInsteadOfReturningAnErrorAsModelContext()
    {
        var tools = new PlannerTools();
        var conference = Catalog(250);
        conference.Sessions[0] = conference.Sessions[0] with { Title = new string('x', PlannerCatalog.MaximumCharacters) };
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        using var services = new ServiceCollection().AddSingleton(tools).BuildServiceProvider();
        var catalog = tools.CreateFunctions(services).OfType<AIFunction>().Single(f => f.Name == "catalog_topics");
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await catalog.InvokeAsync(new AIFunctionArguments()));
    }

    [Fact]
    public void AllFiftyMatchesAreVisibleAndCanBeSavedWithoutAShortlistLimit()
    {
        var conference = Catalog(100);
        var tools = new PlannerTools();
        tools.Initialize(conference, TimeZoneInfo.Utc, []);
        tools.CapturePreferences([], [], false, new(["Performance"], false, false, true));
        var ids = SchedulePlanner.TopicMatches(conference, tools.Preferences!).Select(s => s.Id).ToArray();
        tools.IncludeCandidates(ids);
        Assert.Equal(50, tools.CandidateIds.Count);
        Assert.Equal(50, tools.EligibleCandidateIds.Length);
        Assert.StartsWith("Preview ready.", tools.SubmitProposal(ids));
        Assert.Equal(50, tools.Proposal!.MatchedSessionIds.Length);
        Assert.True(SchedulePlanner.Review(conference, tools.Proposal, TimeZoneInfo.Utc, []).CanApply);
        Assert.Throws<InvalidDataException>(() => tools.IncludeCandidates(["invented"]));
    }

    [Fact]
    public void RecommendationPreservesLateRequiredSpeakerAfterTwelveEarlierAlternatives()
    {
        var conference = Catalog(100);
        var preferences = new ScheduleProposal([], ["Late Speaker"], []);
        var ids = SchedulePlanner.Recommend(conference, preferences,
            conference.Sessions.Select(s => s.Id), TimeZoneInfo.Utc, []);
        Assert.Equal(12, ids.Length);
        Assert.Contains("session-99", ids);
        Assert.True(SchedulePlanner.Review(conference, preferences with { SessionIds = ids }, TimeZoneInfo.Utc, []).CanApply);
    }

    [Fact]
    public void RecommendationHonorsOverlapsLikesAndAvailabilityWithoutInventingSpeakerCoverage()
    {
        var conference = Catalog(100);
        conference.Sessions[1] = conference.Sessions[1] with
        {
            StartsAt = conference.Sessions[0].StartsAt, EndsAt = conference.Sessions[0].EndsAt
        };
        var preferences = new ScheduleProposal([], [], [new("2026-11-10T00:00", "2026-11-10T01:00", "UTC")]);
        var ids = SchedulePlanner.Recommend(conference, preferences,
            conference.Sessions.Select(s => s.Id), TimeZoneInfo.Utc, ["session-0"]);
        Assert.DoesNotContain("session-1", ids);
        Assert.Equal(3, ids.Length);
        Assert.True(SchedulePlanner.Review(conference, preferences with { SessionIds = ids }, TimeZoneInfo.Utc, ["session-0"]).CanApply);
        Assert.Empty(SchedulePlanner.Recommend(conference, preferences with { RequiredSpeakers = ["Late Speaker"] },
            conference.Sessions.Select(s => s.Id), TimeZoneInfo.Utc, []));
    }

    [Theory]
    [InlineData("I like mobile, AI and I want to see everything on perf", 0)]
    [InlineData("My users need screen readers and bigger text", 1)]
    [InlineData("I need to build HTTP endpoints for my backend", 1)]
    [InlineData("Just everything from Gerald, no matter the time", 0)]
    [InlineData("All talks on MAUUI", 0)]
    [InlineData("Everything about Blazorrr", 0)]
    [InlineData("All C# sessions, but they must finish by 22:00 UTC each day", 0)]
    public async Task SourceInterestsDoNotLoseGoalsOrReclassifyKnownTopics(string request, int semanticCount)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        Assert.Equal(semanticCount, PlannerCatalog.SemanticInterests(conference, request).Length);
    }

    [Theory]
    [InlineData("Everything about Rust")]
    [InlineData("All sessions on COBOL")]
    public async Task ExplicitMissingSubjectsCannotSilentlyBecomeAdjacentCatalogCategories(string request)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var conference = await ConferenceJson.ReadAsync(stream);
        Assert.Throws<InvalidDataException>(() => PlannerCatalog.SemanticInterests(conference, request));
    }
}
