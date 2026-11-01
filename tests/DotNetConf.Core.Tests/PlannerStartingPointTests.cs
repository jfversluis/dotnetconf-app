using DotNetConf.App.Services;
using DotNetConf.Core;

namespace DotNetConf.Core.Tests;

public sealed class PlannerStartingPointTests
{
    private static ConferenceDocument Catalog => new()
    {
        Sessions = [new() { Id = "one", Topics = ["AI", "MAUI", "C#", "Blazor", "Performance"] }]
    };

    [Fact]
    public void SuggestionsKeepModelOrderAndOnlyUseRealAgendaTopics()
    {
        var result = PlannerStartingPoints.Ground(Catalog, ["Performance", "MAUI", "C#"]);
        Assert.Equal(3, result.Count);
        Assert.Contains("Performance", result[0].Label);
        Assert.Contains("MAUI", result[1].Label);
        Assert.Contains("C#", result[2].Label);
        Assert.Equal("I'm interested in Performance.", result[0].Request);
        var choices = PlannerStartingPoints.Schema(Catalog)
            .GetProperty("properties").GetProperty("topics").GetProperty("items").GetProperty("enum");
        Assert.Equal(5, choices.GetArrayLength());
        var subset = PlannerStartingPoints.Schema(Catalog, ["AI", "MAUI", "C#"])
            .GetProperty("properties").GetProperty("topics").GetProperty("items").GetProperty("enum");
        Assert.Equal(["AI", "MAUI", "C#"], subset.EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public void SpeakerSuggestionUsesOnlyAnUnambiguousSpeakerWithScheduledSessions()
    {
        var conference = Catalog;
        conference.Speakers.AddRange([
            new() { Id = "real", Name = "Maddy Montaquila" },
            new() { Id = "absent", Name = "Not Scheduled" },
            new() { Id = "duplicate-one", Name = "Same Name" },
            new() { Id = "duplicate-two", Name = "Same Name" }]);
        conference.Sessions[0] = conference.Sessions[0] with { SpeakerIds = ["real", "duplicate-one", "duplicate-two"] };
        var result = PlannerStartingPoints.Ground(conference, ["AI", "MAUI", "C#"]);
        Assert.Equal(4, result.Count);
        Assert.Equal("Team Maddy Montaquila", result[1].Label);
        Assert.Equal("I want to see all sessions by Maddy Montaquila.", result[1].Request);
    }

    [Fact]
    public void InvalidOrInventedSuggestionsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => PlannerStartingPoints.Ground(Catalog, null));
        Assert.Throws<InvalidDataException>(() => PlannerStartingPoints.Ground(Catalog, ["AI"]));
        Assert.Throws<InvalidDataException>(() => PlannerStartingPoints.Ground(Catalog, ["AI", "AI", "MAUI"]));
        Assert.Throws<InvalidDataException>(() => PlannerStartingPoints.Ground(Catalog, ["AI", "Rust", "MAUI"]));
        Assert.Throws<InvalidDataException>(() => PlannerStartingPoints.Ground(Catalog,
            ["AI", "MAUI", "C#", "Blazor", "Performance", "Testing"]));
    }
}
