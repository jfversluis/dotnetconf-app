using DotNetConf.Core;

namespace DotNetConf.Core.Tests;

public sealed class TimelineTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static Session At(string id, string start, string end) => new()
    {
        Id = id,
        Title = id,
        StartsAt = DateTimeOffset.Parse(start),
        EndsAt = DateTimeOffset.Parse(end),
    };

    [Fact]
    public void OverlappingSessionsShareLanesAndOthersKeepTheFullWidth()
    {
        var day = Assert.Single(Timeline.Build([
            At("first", "2026-11-10T16:00Z", "2026-11-10T16:45Z"),
            At("second", "2026-11-10T17:00Z", "2026-11-10T17:45Z"),
            At("third", "2026-11-10T17:15Z", "2026-11-10T18:00Z"),
        ], Utc));

        var items = day.Items.ToDictionary(item => item.Session.Id);
        Assert.Equal((0, 1), (items["first"].Lane, items["first"].LaneCount));
        Assert.Equal((0, 2), (items["second"].Lane, items["second"].LaneCount));
        Assert.Equal((1, 2), (items["third"].Lane, items["third"].LaneCount));
    }

    [Fact]
    public void FreedLanesAreReusedWithinAnOverlapGroup()
    {
        var day = Assert.Single(Timeline.Build([
            At("long", "2026-11-10T16:00Z", "2026-11-10T18:00Z"),
            At("early", "2026-11-10T16:00Z", "2026-11-10T16:30Z"),
            At("late", "2026-11-10T17:00Z", "2026-11-10T17:30Z"),
        ], Utc));

        var items = day.Items.ToDictionary(item => item.Session.Id);
        Assert.Equal(0, items["early"].Lane);
        Assert.Equal(1, items["long"].Lane);
        Assert.Equal(0, items["late"].Lane);
        Assert.All(day.Items, item => Assert.Equal(2, item.LaneCount));
    }

    [Fact]
    public void LongGapsBecomeBreaksAndOffsetsStayInOrder()
    {
        var day = Assert.Single(Timeline.Build([
            At("afternoon", "2026-11-11T16:00Z", "2026-11-11T16:45Z"),
            At("night", "2026-11-11T23:30Z", "2026-11-12T00:15Z"),
        ], Utc));

        Assert.Equal(2, day.Spans.Count);
        var gap = Assert.Single(day.Breaks);
        // The break covers the real wait between the sessions, not the padding around them.
        Assert.Equal(DateTimeOffset.Parse("2026-11-11T16:45Z"), gap.Start);
        Assert.Equal(DateTimeOffset.Parse("2026-11-11T23:30Z"), gap.End);
        Assert.Equal(day.Spans[0].Length + Timeline.BreakLength + day.Spans[1].Length, day.Length);

        var afternoon = day.OffsetOf(DateTimeOffset.Parse("2026-11-11T16:00Z"))!.Value;
        var inBreak = day.OffsetOf(DateTimeOffset.Parse("2026-11-11T20:00Z"))!.Value;
        var night = day.OffsetOf(DateTimeOffset.Parse("2026-11-11T23:30Z"))!.Value;
        Assert.True(afternoon < inBreak && inBreak < night);
        Assert.Equal(gap.Offset + Timeline.BreakLength / 2, inBreak);
    }

    [Fact]
    public void ASessionCrossingMidnightStaysOnTheDayItStarts()
    {
        var days = Timeline.Build([
            At("night", "2026-11-11T23:30Z", "2026-11-12T00:15Z"),
            At("next", "2026-11-12T16:00Z", "2026-11-12T16:45Z"),
        ], Utc);

        Assert.Equal([new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 12)], days.Select(day => day.Day));
        Assert.Equal("night", Assert.Single(days[0].Items).Session.Id);
        Assert.Contains(DateTimeOffset.Parse("2026-11-12T00:00Z"), days[0].Hours());
    }

    [Fact]
    public void DaysFollowTheLocalTimeZone()
    {
        var tokyo = TimeZoneInfo.CreateCustomTimeZone("Test/Tokyo", TimeSpan.FromHours(9), "Tokyo", "Tokyo");

        var days = Timeline.Build([
            At("afternoon", "2026-11-11T16:00Z", "2026-11-11T16:45Z"),
            At("night", "2026-11-11T23:30Z", "2026-11-12T00:15Z"),
        ], tokyo);

        Assert.Equal([new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 12)],
            days.SelectMany(day => day.Items).Select(item => DateOnly.FromDateTime(item.Start.DateTime)));
        Assert.Single(days);
    }

    [Fact]
    public void ItemsKeepTheirPositionInTheInput()
    {
        var days = Timeline.Build([
            At("late", "2026-11-12T16:00Z", "2026-11-12T16:45Z"),
            At("early", "2026-11-10T16:00Z", "2026-11-10T16:45Z"),
        ], Utc);

        Assert.Equal([1, 0], days.SelectMany(day => day.Items).Select(item => item.Index));
    }

    [Fact]
    public void LanesNeverOverlapInLargeAgendas()
    {
        var random = new Random(7);
        var origin = DateTimeOffset.Parse("2026-11-10T00:00Z");
        var sessions = Enumerable.Range(0, 20_000).Select(i =>
        {
            var start = origin.AddMinutes(random.Next(0, 3 * 24 * 60));
            return new Session { Id = i.ToString(), StartsAt = start, EndsAt = start.AddMinutes(15 + random.Next(0, 120)) };
        }).ToList();

        var items = Timeline.Build(sessions, Utc).SelectMany(day => day.Items).ToList();

        Assert.Equal(sessions.Count, items.Count);
        foreach (var lane in items.GroupBy(item => (DateOnly.FromDateTime(item.Start.DateTime), item.Lane)))
        {
            var ordered = lane.OrderBy(item => item.Start).ToList();
            for (var i = 1; i < ordered.Count; i++)
                Assert.True(ordered[i].Start >= ordered[i - 1].End);
        }
        Assert.All(items, item => Assert.InRange(item.Lane, 0, item.LaneCount - 1));
    }

    [Fact]
    public void InstantsOutsideTheDayHaveNoOffset()
    {
        var day = Assert.Single(Timeline.Build([At("only", "2026-11-10T16:00Z", "2026-11-10T16:45Z")], Utc));

        Assert.Null(day.OffsetOf(DateTimeOffset.Parse("2026-11-10T09:00Z")));
        Assert.Null(day.OffsetOf(DateTimeOffset.Parse("2026-11-10T20:00Z")));
        Assert.Equal(DateTimeOffset.Parse("2026-11-10T15:45Z"), day.Spans[0].Start);
        Assert.Equal(DateTimeOffset.Parse("2026-11-10T17:00Z"), day.Spans[0].End);
        Assert.Equal([DateTimeOffset.Parse("2026-11-10T16:00Z"), DateTimeOffset.Parse("2026-11-10T17:00Z")], day.Hours());
    }

    [Theory]
    // Madrid falls back from 03:00 to 02:00 on 25 October 2026 and springs forward from 02:00 to 03:00 on 29 March.
    [InlineData("Europe/Madrid", "2026-10-24T23:30Z", "2026-10-25T03:30Z", new[] { 2, 2, 3, 4 })]
    [InlineData("Europe/Madrid", "2026-03-29T00:30Z", "2026-03-29T03:00Z", new[] { 3, 4, 5 })]
    // Lord Howe Island moves its clocks by half an hour, from 02:00 to 02:30 on 4 October 2026.
    [InlineData("Australia/Lord_Howe", "2026-10-03T14:00Z", "2026-10-03T18:00Z", new[] { 1, 3, 4, 5 })]
    public void HoursStayWholeLocalHoursAcrossDaylightSavingChanges(string zoneId, string start, string end, int[] expected)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var day = Assert.Single(Timeline.Build([At("session", start, end)], zone));

        var hours = day.Hours().ToList();

        Assert.Equal(expected, hours.Select(hour => hour.Hour));
        Assert.All(hours, hour => Assert.Equal(0, hour.Minute));
        Assert.All(hours, hour => Assert.NotNull(day.OffsetOf(hour)));
    }

    [Fact]
    public void InstantsAroundMidnightBelongToTheDayTheyFallOn()
    {
        var days = Timeline.Build([
            At("late", "2026-11-11T23:30Z", "2026-11-12T00:30Z"),
            At("early", "2026-11-12T00:30Z", "2026-11-12T01:15Z"),
        ], Utc);

        Assert.Equal(2, days.Count);
        // The first day runs on past midnight, into the hours the second day shows.
        Assert.NotNull(days[0].OffsetOf(DateTimeOffset.Parse("2026-11-12T00:30Z")));
        Assert.Equal(1, Timeline.Locate(days, DateTimeOffset.Parse("2026-11-12T00:30Z"), Utc)?.Day);
        Assert.Equal(0, Timeline.Locate(days, DateTimeOffset.Parse("2026-11-11T23:30Z"), Utc)?.Day);
        Assert.Null(Timeline.Locate(days, DateTimeOffset.Parse("2026-11-12T09:00Z"), Utc));
    }
}
