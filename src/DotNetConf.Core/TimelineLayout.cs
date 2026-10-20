namespace DotNetConf.Core;

/// <summary>
/// The timeline flattened into arrays for a virtualized view. It can be built off the UI thread and answers "what is
/// on screen" with binary searches and prefix sums, so each frame costs what is visible, however many sessions run in
/// parallel.
/// </summary>
public sealed class TimelineLayout
{
    /// <summary>
    /// The most lanes the view ever shows side by side; sessions in further lanes are only counted.
    /// </summary>
    public const int MaxColumns = 16;

    /// <summary>
    /// The minutes in each step of <see cref="Busy"/>.
    /// </summary>
    public const int BusyStep = 5;

    // Hidden counts per day and minute for each column count, as prefix sums: [columns - 1][day][minute].
    private readonly int[][][] hiddenBefore;
    // Items in the first MaxColumns lanes, by day and start: the only ones the view can ever show.
    private readonly int[] shownFirst;
    // Each day's first item; the items are in day order.
    private readonly int[] dayFirst;
    private readonly double[] shownLongest;
    private int[] shownStorage = [];
    private readonly long[] startsUtc;
    private readonly long[] endsUtc;
    private readonly TimeZoneInfo timeZone;
    private long longestUtc;

    private TimelineLayout(IReadOnlyList<TimelineDay> days, int count, TimeZoneInfo timeZone)
    {
        this.timeZone = timeZone;
        Days = days;
        DayHours = days.Select(day => day.Hours().Select(hour => (day.OffsetOf(hour)!.Value, hour)).ToArray()).ToArray();
        Starts = new double[count];
        Ends = new double[count];
        Lanes = new int[count];
        LaneCounts = new int[count];
        Sessions = new int[count];
        shownFirst = new int[days.Count + 1];
        dayFirst = new int[days.Count];
        shownLongest = new double[days.Count];
        startsUtc = new long[count];
        endsUtc = new long[count];
        hiddenBefore = new int[MaxColumns][][];
    }

    public IReadOnlyList<TimelineDay> Days { get; }

    /// <summary>
    /// Each day's whole hours with their offsets, for the ruler.
    /// </summary>
    public (double Offset, DateTimeOffset Hour)[][] DayHours { get; }

    /// <summary>
    /// Each item's start and end in its day's timeline minutes, its lane, and the lanes its overlap group uses.
    /// Items are numbered by day and then by start time.
    /// </summary>
    public double[] Starts { get; }
    public double[] Ends { get; }
    public int[] Lanes { get; }
    public int[] LaneCounts { get; }

    /// <summary>
    /// Each item's position in the session list the layout was built from.
    /// </summary>
    public int[] Sessions { get; }

    public int Count => Starts.Length;

    /// <summary>
    /// How busy each stretch of each day is, in timeline minutes: the share of the busiest moment's parallel sessions.
    /// </summary>
    public (int Day, double From, double To, double Level)[] Busy { get; private set; } = [];

    public static TimelineLayout Build(IReadOnlyList<Session> sessions, TimeZoneInfo timeZone)
    {
        var days = Timeline.Build(sessions, timeZone);
        var layout = new TimelineLayout(days, sessions.Count, timeZone);
        var shown = new List<int>();
        var index = 0;
        for (var day = 0; day < days.Count; day++)
        {
            layout.shownFirst[day] = shown.Count;
            layout.dayFirst[day] = index;
            foreach (var item in days[day].Items)
            {
                layout.Starts[index] = days[day].OffsetOf(item.Start)!.Value;
                layout.Ends[index] = days[day].OffsetOf(item.End)!.Value;
                layout.Lanes[index] = item.Lane;
                layout.LaneCounts[index] = item.LaneCount;
                layout.Sessions[index] = item.Index;
                if (item.Lane < MaxColumns)
                {
                    shown.Add(index);
                    layout.shownLongest[day] = Math.Max(layout.shownLongest[day], layout.Ends[index] - layout.Starts[index]);
                }
                index++;
            }
        }
        layout.shownFirst[days.Count] = shown.Count;
        layout.shownStorage = shown.ToArray();
        layout.Busy = BusyRuns(days, layout.Starts, layout.Ends);

        for (var columns = 1; columns <= MaxColumns; columns++)
        {
            var perDay = new int[days.Count][];
            for (var day = 0; day < days.Count; day++)
                perDay[day] = new int[(int)Math.Ceiling(days[day].Length) + 2];
            index = 0;
            for (var day = 0; day < days.Count; day++)
            {
                foreach (var _ in days[day].Items)
                {
                    if (IsHidden(layout.Lanes[index], layout.LaneCounts[index], columns))
                        perDay[day][(int)layout.Starts[index] + 1]++;
                    index++;
                }
                for (var minute = 1; minute < perDay[day].Length; minute++)
                    perDay[day][minute] += perDay[day][minute - 1];
            }
            layout.hiddenBefore[columns - 1] = perDay;
        }

        var byStart = sessions.Select(session => (Start: session.StartsAt.UtcTicks, End: session.EndsAt.UtcTicks))
            .OrderBy(entry => entry.Start).ToArray();
        for (var i = 0; i < byStart.Length; i++)
        {
            layout.startsUtc[i] = byStart[i].Start;
            layout.endsUtc[i] = byStart[i].End;
            layout.longestUtc = Math.Max(layout.longestUtc, byStart[i].End - byStart[i].Start);
        }
        return layout;
    }

    // Counts the sessions running in each step with a difference array, merging equal steps into runs.
    private static (int Day, double From, double To, double Level)[] BusyRuns(IReadOnlyList<TimelineDay> days, double[] starts, double[] ends)
    {
        var runs = new List<(int Day, double From, double To, int Count)>();
        var busiest = 1;
        var index = 0;
        for (var day = 0; day < days.Count; day++)
        {
            var changes = new int[(int)Math.Ceiling(days[day].Length / BusyStep) + 1];
            for (var i = 0; i < days[day].Items.Count; i++, index++)
            {
                changes[(int)(starts[index] / BusyStep)]++;
                changes[(int)Math.Ceiling(ends[index] / BusyStep)]--;
            }
            var (running, from) = (0, 0);
            for (var step = 0; step < changes.Length; step++)
            {
                if (changes[step] == 0)
                    continue;
                if (running > 0)
                    runs.Add((day, from * BusyStep, step * BusyStep, running));
                running += changes[step];
                busiest = Math.Max(busiest, running);
                from = step;
            }
        }
        return runs.Select(run => (run.Day, run.From, run.To, (double)run.Count / busiest)).ToArray();
    }

    /// <summary>
    /// The day an item belongs to.
    /// </summary>
    public int DayOf(int item)
    {
        var index = Array.BinarySearch(dayFirst, item);
        return index >= 0 ? index : ~index - 1;
    }

    /// <summary>
    /// Whether an item gets counted instead of shown when <paramref name="columns"/> lanes fit side by side:
    /// a group that needs more lanes keeps its last visible column for the count. A single column still shows the
    /// first lane, with the count beside it.
    /// </summary>
    public static bool IsHidden(int lane, int laneCount, int columns) =>
        laneCount > columns && lane >= Math.Max(columns - 1, 1);

    /// <summary>
    /// Items of a day overlapping the given range of timeline minutes that are shown with this many columns.
    /// </summary>
    public IEnumerable<int> ShownBetween(int day, double from, double to, int columns)
    {
        var (start, end) = (shownFirst[day], shownFirst[day + 1]);
        // Shown items are in start order, and none is longer than the day's longest.
        var low = from - shownLongest[day];
        while (start < end)
        {
            var middle = (start + end) / 2;
            if (Starts[shownStorage[middle]] < low)
                start = middle + 1;
            else
                end = middle;
        }
        for (var i = start; i < shownFirst[day + 1]; i++)
        {
            var item = shownStorage[i];
            if (Starts[item] > to)
                yield break;
            if (Ends[item] >= from && !IsHidden(Lanes[item], LaneCounts[item], columns))
                yield return item;
        }
    }

    /// <summary>
    /// How many sessions starting in the given range of timeline minutes are counted instead of shown.
    /// </summary>
    public int HiddenBetween(int day, double from, double to, int columns)
    {
        var prefix = hiddenBefore[Math.Clamp(columns, 1, MaxColumns) - 1][day];
        int At(double minute) => prefix[Math.Clamp((int)Math.Ceiling(minute), 0, prefix.Length - 1)];
        return At(to) - At(from);
    }

    /// <summary>
    /// The items <see cref="HiddenBetween"/> counts, in start order.
    /// </summary>
    public IEnumerable<int> HiddenItemsBetween(int day, double from, double to, int columns)
    {
        var (lower, upper) = (Math.Ceiling(from), Math.Ceiling(to));
        var dayEnd = day + 1 < dayFirst.Length ? dayFirst[day + 1] : Count;
        var (start, end) = (dayFirst[day], dayEnd);
        while (start < end)
        {
            var middle = (start + end) / 2;
            if (Starts[middle] < lower)
                start = middle + 1;
            else
                end = middle;
        }
        for (var item = start; item < dayEnd && Math.Floor(Starts[item]) < upper; item++)
        {
            if (IsHidden(Lanes[item], LaneCounts[item], columns))
                yield return item;
        }
    }

    /// <summary>
    /// Whether any session is running at this instant.
    /// </summary>
    public bool AnyLive(DateTimeOffset now)
    {
        var ticks = now.UtcTicks;
        for (var i = UpperBound(startsUtc, ticks) - 1; i >= 0 && startsUtc[i] >= ticks - longestUtc; i--)
        {
            if (endsUtc[i] > ticks)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether the first session has started by this instant.
    /// </summary>
    public bool AnyStarted(DateTimeOffset now) => Count > 0 && startsUtc[0] <= now.UtcTicks;

    /// <summary>
    /// When the next session starts after this instant, or null once the last one has started.
    /// </summary>
    public DateTimeOffset? NextStart(DateTimeOffset now)
    {
        var index = UpperBound(startsUtc, now.UtcTicks);
        return index < Count ? new DateTimeOffset(startsUtc[index], TimeSpan.Zero) : null;
    }

    /// <summary>
    /// The day showing the instant, with its offset in timeline minutes.
    /// </summary>
    public (int Day, double Offset)? Locate(DateTimeOffset instant) => Timeline.Locate(Days, instant, timeZone);

    /// <summary>
    /// The shown items just before and after one, in time order, or -1 where there is none. The item itself may be
    /// counted instead of shown, and <paramref name="include"/> can skip more, such as sessions outside a filter.
    /// </summary>
    public (int Previous, int Next) ShownNeighbours(int item, int columns, Func<int, bool>? include = null)
    {
        var position = Array.BinarySearch(shownStorage, item);
        var (previous, next) = position >= 0 ? (position - 1, position + 1) : (~position - 1, ~position);
        bool Skip(int index) =>
            IsHidden(Lanes[shownStorage[index]], LaneCounts[shownStorage[index]], columns) || include?.Invoke(shownStorage[index]) == false;
        while (previous >= 0 && Skip(previous))
            previous--;
        while (next < shownStorage.Length && Skip(next))
            next++;
        return (previous >= 0 ? shownStorage[previous] : -1, next < shownStorage.Length ? shownStorage[next] : -1);
    }

    private static int UpperBound(long[] values, long value)
    {
        var (start, end) = (0, values.Length);
        while (start < end)
        {
            var middle = (start + end) / 2;
            if (values[middle] <= value)
                start = middle + 1;
            else
                end = middle;
        }
        return start;
    }
}
