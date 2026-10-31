namespace DotNetConf.Core;

/// <summary>
/// One local day of the agenda, laid out for the timeline.
/// </summary>
public sealed class TimelineDay
{
    private readonly TimeZoneInfo timeZone;

    internal TimelineDay(DateOnly day, IReadOnlyList<TimelineItem> items, IReadOnlyList<TimelineSpan> spans,
        IReadOnlyList<TimelineBreak> breaks, TimeZoneInfo timeZone)
    {
        this.timeZone = timeZone;
        Day = day;
        Items = items;
        Spans = spans;
        Breaks = breaks;
        Length = spans[^1].Offset + spans[^1].Length;
    }

    public DateOnly Day { get; }
    public IReadOnlyList<TimelineItem> Items { get; }
    public IReadOnlyList<TimelineSpan> Spans { get; }
    public IReadOnlyList<TimelineBreak> Breaks { get; }

    /// <summary>
    /// The day's height in timeline minutes, with breaks at their fixed length.
    /// </summary>
    public double Length { get; }

    /// <summary>
    /// Minutes from the top of the day, or null outside it. An instant inside a break sits in its middle.
    /// </summary>
    public double? OffsetOf(DateTimeOffset instant)
    {
        foreach (var span in Spans)
        {
            if (instant >= span.Start && instant <= span.End)
                return span.Offset + (instant - span.Start).TotalMinutes;
        }
        foreach (var gap in Breaks)
        {
            if (instant > gap.Start && instant < gap.End)
                return gap.Offset + Timeline.BreakLength / 2;
        }
        return null;
    }

    /// <summary>
    /// Whole hours inside the spans, for the time ruler. The spans are walked in quarter hours read in the day's time
    /// zone, so the labels stay on the hour across any daylight saving change, even one of half an hour.
    /// </summary>
    public IEnumerable<DateTimeOffset> Hours() => Spans.SelectMany(span =>
        Enumerable.Range(0, (int)((span.End - span.Start).TotalMinutes / 15) + 1)
            .Select(step => TimeZoneInfo.ConvertTime(span.Start.AddMinutes(15 * step), timeZone))
            .Where(time => time.Minute == 0));
}
