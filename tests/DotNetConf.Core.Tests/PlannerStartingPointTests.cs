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
        Assert.Equal(["Explore Performance", "Explore MAUI", "Explore C#"], result.Select(s => s.Label));
        Assert.Equal("I'm interested in Performance.", result[0].Request);
        var choices = PlannerStartingPoints.Schema(Catalog)
            .GetProperty("properties").GetProperty("topics").GetProperty("items").GetProperty("enum");
        Assert.Equal(5, choices.GetArrayLength());
        var subset = PlannerStartingPoints.Schema(Catalog, ["AI", "MAUI", "C#"])
            .GetProperty("properties").GetProperty("topics").GetProperty("items").GetProperty("enum");
        Assert.Equal(["AI", "MAUI", "C#"], subset.EnumerateArray().Select(t => t.GetString()));
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
