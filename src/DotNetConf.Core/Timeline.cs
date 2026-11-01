namespace DotNetConf.Core;

/// <summary>
/// Builds the timeline layout of the agenda.
/// </summary>
public static class Timeline
{
    /// <summary>
    /// Gaps longer than this become breaks.
    /// </summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(90);

    /// <summary>
    /// How many timeline minutes a break takes up.
    /// </summary>
    public const double BreakLength = 40;

    private static readonly TimeSpan Padding = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Lays out sessions per local day: lanes for overlaps, spans to scale and long gaps as breaks.
    /// </summary>
    public static IReadOnlyList<TimelineDay> Build(IEnumerable<Session> sessions, TimeZoneInfo timeZone) => sessions
        .Select((session, index) => (Session: session, Index: index,
            Start: TimeZoneInfo.ConvertTime(session.StartsAt, timeZone),
            End: TimeZoneInfo.ConvertTime(session.EndsAt, timeZone)))
        .GroupBy(entry => DateOnly.FromDateTime(entry.Start.DateTime))
        .OrderBy(group => group.Key)
        .Select(group => BuildDay(group.Key, group.OrderBy(entry => entry.Start).ThenBy(entry => entry.End).ToArray(), timeZone))
        .ToArray();

    /// <summary>
    /// The day showing an instant, with its offset in timeline minutes. Days can overlap around midnight, where a day's
    /// last sessions run on past it and the next day's first span starts just before it, so the instant's own date
    /// decides first, then whichever neighbour reaches it.
    /// </summary>
    public static (int Day, double Offset)? Locate(IReadOnlyList<TimelineDay> days, DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);
        var (low, high) = (0, days.Count);
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (days[middle].Day < date)
                low = middle + 1;
            else
                high = middle;
        }
        ReadOnlySpan<int> candidates = low < days.Count && days[low].Day == date ? [low, low - 1, low + 1] : [low - 1, low];
        foreach (var day in candidates)
        {
            if (day >= 0 && day < days.Count && days[day].OffsetOf(instant) is { } offset)
                return (day, offset);
        }
        return null;
    }

    private static TimelineDay BuildDay(DateOnly day,
        IReadOnlyList<(Session Session, int Index, DateTimeOffset Start, DateTimeOffset End)> entries, TimeZoneInfo timeZone)
    {
        var items = new List<TimelineItem>();
        var spans = new List<TimelineSpan>();
        var breaks = new List<TimelineBreak>();
        var offset = 0.0;
        var index = 0;
        var lastEnd = DateTimeOffset.MinValue;
        while (index < entries.Count)
        {
            // A span runs until the next session starts more than MaxGap after everything before it has ended.
            var spanStart = Floor(entries[index].Start - Padding);
            var spanEnd = entries[index].End;
            var first = index;
            while (index < entries.Count && entries[index].Start - spanEnd <= MaxGap)
            {
                if (entries[index].End > spanEnd)
                    spanEnd = entries[index].End;
                index++;
            }
            if (spans.Count > 0 && spanStart < spans[^1].End)
                spanStart = spans[^1].End;
            if (spans.Count > 0)
            {
                // Labelled with the real wait, from the last session ending to the next one starting, not the padding.
                breaks.Add(new(lastEnd, entries[first].Start, offset));
                offset += BreakLength;
            }
            var span = new TimelineSpan(spanStart, Ceiling(spanEnd + Padding), offset);
            spans.Add(span);
            offset += span.Length;
            lastEnd = spanEnd;
            AddLanes(items, entries.Skip(first).Take(index - first));
        }
        return new(day, items, spans, breaks, timeZone);
    }

    // Sessions that overlap, directly or through each other, share lanes; each takes the lowest lane free at its
    // start. Two heaps keep this O(n log n) for very large agendas.
    private static void AddLanes(List<TimelineItem> items,
        IEnumerable<(Session Session, int Index, DateTimeOffset Start, DateTimeOffset End)> entries)
    {
        var group = new List<(Session Session, int Index, DateTimeOffset Start, DateTimeOffset End, int Lane)>();
        var busy = new PriorityQueue<int, DateTimeOffset>();
        var free = new PriorityQueue<int, int>();
        var laneCount = 0;
        foreach (var entry in entries)
        {
            while (busy.TryPeek(out var lane, out var end) && end <= entry.Start)
            {
                busy.Dequeue();
                free.Enqueue(lane, lane);
            }
            if (busy.Count == 0 && group.Count > 0)
            {
                Flush();
                free.Clear();
                laneCount = 0;
            }
            if (!free.TryDequeue(out var assigned, out _))
                assigned = laneCount++;
            busy.Enqueue(assigned, entry.End);
            group.Add((entry.Session, entry.Index, entry.Start, entry.End, assigned));
        }
        Flush();

        void Flush()
        {
            items.AddRange(group.Select(entry =>
                new TimelineItem(entry.Session, entry.Index, entry.Start, entry.End, entry.Lane, laneCount)));
            group.Clear();
        }
    }

    private static DateTimeOffset Floor(DateTimeOffset instant) =>
        instant.AddMinutes(-(instant.Minute % 15)).AddSeconds(-instant.Second).AddTicks(-(instant.Ticks % TimeSpan.TicksPerSecond));

    private static DateTimeOffset Ceiling(DateTimeOffset instant)
    {
        var floor = Floor(instant);
        return floor == instant ? instant : floor.AddMinutes(15);
    }
}
