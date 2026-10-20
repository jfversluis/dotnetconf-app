using DotNetConf.Core;

namespace DotNetConf.Core.Tests;

public sealed class TimelineLayoutTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    // Three sessions overlap from 16:00, so they need three lanes; a fourth runs alone later.
    private static readonly Session[] Sessions =
    [
        At("a", "2026-11-10T16:00Z", "2026-11-10T17:00Z"),
        At("b", "2026-11-10T16:15Z", "2026-11-10T16:45Z"),
        At("c", "2026-11-10T16:30Z", "2026-11-10T17:30Z"),
        At("d", "2026-11-10T18:00Z", "2026-11-10T18:30Z"),
    ];

    private static Session At(string id, string start, string end) => new()
    {
        Id = id,
        Title = id,
        StartsAt = DateTimeOffset.Parse(start),
        EndsAt = DateTimeOffset.Parse(end),
    };

    private static string[] Ids(TimelineLayout layout, IEnumerable<int> items) =>
        items.Select(item => Sessions[layout.Sessions[item]].Id).ToArray();

    private static int Item(TimelineLayout layout, string id) =>
        Enumerable.Range(0, layout.Count).Single(item => Sessions[layout.Sessions[item]].Id == id);

    [Fact]
    public void SessionsPastTheColumnsThatFitAreCountedInsteadOfShown()
    {
        var layout = TimelineLayout.Build(Sessions, Utc);
        var length = layout.Days[0].Length;

        // With two columns the three-lane group keeps its first lane and counts the rest in the last column.
        Assert.Equal(["a", "d"], Ids(layout, layout.ShownBetween(0, 0, length, columns: 2)));
        Assert.Equal(2, layout.HiddenBetween(0, 0, length, columns: 2));

        Assert.Equal(["a", "b", "c", "d"], Ids(layout, layout.ShownBetween(0, 0, length, columns: 3)));
        Assert.Equal(0, layout.HiddenBetween(0, 0, length, columns: 3));
    }

    [Fact]
    public void ASingleColumnStillShowsTheFirstSessionOfAnOverlap()
    {
        var layout = TimelineLayout.Build(Sessions, Utc);
        var length = layout.Days[0].Length;

        Assert.Equal(["a", "d"], Ids(layout, layout.ShownBetween(0, 0, length, columns: 1)));
        Assert.Equal(2, layout.HiddenBetween(0, 0, length, columns: 1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TheCountedSessionsCanBeListed(int columns)
    {
        var layout = TimelineLayout.Build([.. Sessions, At("e", "2026-11-11T16:00Z", "2026-11-11T16:45Z")], Utc);
        var day = layout.Days[0];
        double Offset(string time) => day.OffsetOf(DateTimeOffset.Parse($"2026-11-10T{time}Z"))!.Value;

        Assert.Equal(["b", "c"], Ids(layout, layout.HiddenItemsBetween(0, 0, double.MaxValue, columns)));
        Assert.Equal(["b"], Ids(layout, layout.HiddenItemsBetween(0, Offset("16:00"), Offset("16:20"), columns)));
        Assert.Equal(layout.HiddenBetween(0, Offset("16:20"), Offset("16:40"), columns),
            layout.HiddenItemsBetween(0, Offset("16:20"), Offset("16:40"), columns).Count());
        Assert.Empty(layout.HiddenItemsBetween(1, 0, double.MaxValue, columns));
    }

    [Fact]
    public void NeighboursSkipFilteredSessionsAndCanStartFromACountedOne()
    {
        var layout = TimelineLayout.Build(Sessions, Utc);
        var (a, b, c, d) = (Item(layout, "a"), Item(layout, "b"), Item(layout, "c"), Item(layout, "d"));

        Assert.Equal((-1, c), layout.ShownNeighbours(a, columns: 3, item => item != b));
        Assert.Equal((-1, -1), layout.ShownNeighbours(a, columns: 2, item => item != d));
        Assert.Equal((a, d), layout.ShownNeighbours(b, columns: 2));
    }

    [Fact]
    public void OnlySessionsOverlappingTheRangeAreShown()
    {
        var layout = TimelineLayout.Build(Sessions, Utc);
        var day = layout.Days[0];
        var from = day.OffsetOf(DateTimeOffset.Parse("2026-11-10T18:10Z"))!.Value;
        var to = day.OffsetOf(DateTimeOffset.Parse("2026-11-10T18:20Z"))!.Value;

        Assert.Equal(["d"], Ids(layout, layout.ShownBetween(0, from, to, columns: 3)));
    }

    [Fact]
    public void NeighboursSkipSessionsThatAreOnlyCounted()
    {
        var layout = TimelineLayout.Build(Sessions, Utc);
        var a = Item(layout, "a");

        Assert.Equal((-1, Item(layout, "d")), layout.ShownNeighbours(a, columns: 2));
        Assert.Equal((-1, Item(layout, "b")), layout.ShownNeighbours(a, columns: 3));
    }

    [Fact]
    public void BusyLevelsAreSharesOfTheBusiestMoment()
    {
        var layout = TimelineLayout.Build(Sessions, Utc);

        Assert.Equal(1, layout.Busy.Max(run => run.Level));
        Assert.Contains(layout.Busy, run => Math.Abs(run.Level - 1.0 / 3) < 1e-9);
        Assert.All(layout.Busy, run => Assert.True(run.From < run.To));
    }

    [Fact]
    public void LiveAndNextFollowTheClock()
    {
        var layout = TimelineLayout.Build(Sessions, Utc);

        Assert.False(layout.AnyStarted(DateTimeOffset.Parse("2026-11-10T15:00Z")));
        Assert.Equal(DateTimeOffset.Parse("2026-11-10T16:00Z"), layout.NextStart(DateTimeOffset.Parse("2026-11-10T15:00Z")));
        Assert.True(layout.AnyLive(DateTimeOffset.Parse("2026-11-10T16:20Z")));
        Assert.False(layout.AnyLive(DateTimeOffset.Parse("2026-11-10T17:45Z")));
        Assert.Equal(DateTimeOffset.Parse("2026-11-10T18:00Z"), layout.NextStart(DateTimeOffset.Parse("2026-11-10T17:45Z")));
        Assert.Null(layout.NextStart(DateTimeOffset.Parse("2026-11-10T19:00Z")));
    }

    [Fact]
    public void ItemsKnowTheirDay()
    {
        var layout = TimelineLayout.Build([.. Sessions, At("e", "2026-11-11T16:00Z", "2026-11-11T16:45Z")], Utc);

        Assert.Equal(2, layout.Days.Count);
        Assert.Equal(0, layout.DayOf(0));
        Assert.Equal(1, layout.DayOf(layout.Count - 1));
    }

    [Fact]
    public void CountsCoverOnlyTheSessionsStartingInTheRange()
    {
        var layout = TimelineLayout.Build(Sessions, Utc);
        var day = layout.Days[0];
        double Offset(string time) => day.OffsetOf(DateTimeOffset.Parse($"2026-11-10T{time}Z"))!.Value;

        Assert.Equal(1, layout.HiddenBetween(0, Offset("16:00"), Offset("16:20"), columns: 2));
        Assert.Equal(1, layout.HiddenBetween(0, Offset("16:20"), Offset("16:40"), columns: 2));
        Assert.Equal(0, layout.HiddenBetween(0, Offset("17:00"), Offset("18:30"), columns: 2));
    }

    [Fact]
    public void AnEmptyAgendaHasNothingToShow()
    {
        var layout = TimelineLayout.Build([], Utc);
        var instant = DateTimeOffset.Parse("2026-11-10T16:00Z");

        Assert.Empty(layout.Days);
        Assert.Equal(0, layout.Count);
        Assert.Empty(layout.Busy);
        Assert.False(layout.AnyLive(instant));
        Assert.False(layout.AnyStarted(instant));
        Assert.Null(layout.NextStart(instant));
        Assert.Null(layout.Locate(instant));
    }
}
