namespace DotNetConf.App.Planner;

using System.Diagnostics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
// A saved session on the route, in its day's timeline minutes. It animates its shown share from Origin to Target
// since Started: in as the session is saved, out as it is removed.
using Stop = (int Day, double From, double To, bool Branch, bool Conflict, double Origin, double Target, long Started);

/// <summary>
/// Your route through each day, in the gutter beside the cards: a track along every saved session, dashes over the
/// free time between them, and a branch where two of them overlap. A session you save draws itself onto the route.
/// </summary>
internal sealed class RouteLayer : Control
{
    /// <summary>
    /// The width the route takes beside the cards.
    /// </summary>
    public const double GutterWidth = 22;
    private const double TrackWidth = 4;
    private const double BranchSpacing = 8;
    // Matches the gap the cards keep from the times they start and end.
    private const double Inset = 2;
    private const double GrowMilliseconds = 520;
    private const double ShrinkMilliseconds = 260;
    private static readonly CubicEaseOut EaseOut = new();

    private readonly Dictionary<string, Stop> stops = [];
    private readonly HashSet<string> current = [];
    private readonly List<string> finished = [];
    private double[] dayStarts = [];
    private double minuteHeight = 1;
    private bool populated;
    private bool frameRequested;
    private IBrush track = Brushes.Transparent;
    private IBrush conflictTrack = Brushes.Transparent;
    private IBrush nodeFill = Brushes.Transparent;
    private IPen nodeRing = new ImmutablePen(Brushes.Transparent);
    private IPen conflictRing = new ImmutablePen(Brushes.Transparent);
    private IPen dashes = new ImmutablePen(Brushes.Transparent);
    private IPen branch = new ImmutablePen(Brushes.Transparent);

    public RouteLayer()
    {
        IsHitTestVisible = false;
        // Decoration only: each card already says whether it is saved and whether it overlaps.
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }

    public void SetTheme(PlannerTheme value)
    {
        track = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(value.Accent, 0), new GradientStop(value.Magenta, 1) },
        };
        conflictTrack = new ImmutableSolidColorBrush(value.Conflict);
        nodeFill = new ImmutableSolidColorBrush(value.Background);
        nodeRing = new ImmutablePen(new ImmutableSolidColorBrush(value.Accent), 2.5);
        conflictRing = new ImmutablePen(new ImmutableSolidColorBrush(value.Conflict), 2.5);
        dashes = new ImmutablePen(new ImmutableSolidColorBrush(PlannerTheme.WithAlpha(value.Accent, 0.45)), 2,
            new ImmutableDashStyle([2, 2.5], 0), PenLineCap.Round);
        branch = new ImmutablePen(new ImmutableSolidColorBrush(value.Conflict), 2, lineCap: PenLineCap.Round);
        InvalidateVisual();
    }

    /// <summary>
    /// Starts over, so the next route shows at once instead of drawing in, as after a rebuild.
    /// </summary>
    public void Reset()
    {
        stops.Clear();
        populated = false;
        InvalidateVisual();
    }

    /// <summary>
    /// Where each day's timeline starts on the canvas and how tall a minute is, after a zoom or resize.
    /// </summary>
    public void SetScale(double[] starts, double height)
    {
        dayStarts = starts;
        minuteHeight = height;
        InvalidateVisual();
    }

    /// <summary>
    /// The saved sessions now on the route. Sessions new to it draw in and removed ones retract, except on the first
    /// placement or when <paramref name="animate"/> is off.
    /// </summary>
    public void SetStops(IReadOnlyList<(string Id, int Day, double From, double To, bool Branch, bool Conflict)> saved, bool animate)
    {
        animate &= populated;
        var now = Stopwatch.GetTimestamp();
        current.Clear();
        foreach (var (id, day, from, to, branches, conflict) in saved)
        {
            current.Add(id);
            if (!stops.TryGetValue(id, out var stop))
                stops[id] = (day, from, to, branches, conflict, animate ? 0 : 1, 1, now);
            else if (stop.Target == 1)
                stops[id] = stop with { Day = day, From = from, To = to, Branch = branches, Conflict = conflict };
            else
                stops[id] = (day, from, to, branches, conflict, Shown(stop, now), 1, now);
        }
        foreach (var (id, stop) in stops.ToList())
        {
            if (current.Contains(id) || stop.Target == 0)
                continue;
            if (animate)
                stops[id] = stop with { Origin = Shown(stop, now), Target = 0, Started = now };
            else
                stops.Remove(id);
        }
        populated = true;
        InvalidateVisual();
        RequestFrame();
    }

    public override void Render(DrawingContext context)
    {
        if (stops.Count == 0)
            return;
        var now = Stopwatch.GetTimestamp();
        var mainX = Bounds.Width - 9;
        var ordered = stops.Values
            .Where(stop => stop.Day < dayStarts.Length)
            .Select(stop => (Stop: stop, Shown: Shown(stop, now), Top: Y(stop.Day, stop.From) + Inset, Bottom: Y(stop.Day, stop.To) - Inset))
            .Where(entry => entry.Shown > 0)
            .OrderBy(entry => entry.Top)
            .ToList();

        // Dashes over the free time between one saved session and the next on the same day.
        var (day, reach, reachShown) = (-1, 0.0, 0.0);
        foreach (var (stop, shown, top, bottom) in ordered)
        {
            if (stop.Day == day && top - reach >= 14)
            {
                using (context.PushOpacity(Math.Min(shown, reachShown)))
                    context.DrawLine(dashes, new Point(mainX, reach + 6), new Point(mainX, top - 7));
            }
            if (stop.Day != day || bottom > reach)
                (reach, reachShown) = (bottom, shown);
            day = stop.Day;
        }

        foreach (var (stop, shown, top, bottom) in ordered)
        {
            var x = stop.Branch ? mainX - BranchSpacing : mainX;
            var grown = EaseOut.Ease(shown);
            if (stop.Branch)
            {
                // The route branches off for an overlapping session and rejoins after it.
                using (context.PushOpacity(grown))
                {
                    context.DrawGeometry(null, branch, Curve(new Point(mainX, top - 14), new Point(x, top)));
                    context.DrawGeometry(null, branch, Curve(new Point(x, bottom), new Point(mainX, bottom + 14)));
                }
            }
            context.DrawRectangle(stop.Conflict ? conflictTrack : track, null,
                new RoundedRect(new Rect(x - TrackWidth / 2, top, TrackWidth, Math.Max(0, (bottom - top) * grown)), TrackWidth / 2));
            var radius = 4.5 * grown;
            context.DrawEllipse(nodeFill, stop.Conflict ? conflictRing : nodeRing, new Point(x, top), radius, radius);
        }
    }

    private double Y(int day, double minute) => dayStarts[day] + minute * minuteHeight;

    private void RequestFrame()
    {
        if (frameRequested || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;
        frameRequested = true;
        topLevel.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        frameRequested = false;
        var now = Stopwatch.GetTimestamp();
        finished.Clear();
        var moving = false;
        foreach (var (id, stop) in stops)
        {
            var done = Stopwatch.GetElapsedTime(stop.Started, now).TotalMilliseconds >= Duration(stop);
            if (done && stop.Target == 0)
                finished.Add(id);
            moving |= !done;
        }
        foreach (var id in finished)
            stops.Remove(id);
        InvalidateVisual();
        if (moving)
            RequestFrame();
    }

    private static double Shown(Stop stop, long now)
    {
        var progress = Math.Min(1, Stopwatch.GetElapsedTime(stop.Started, now).TotalMilliseconds / Duration(stop));
        return stop.Origin + (stop.Target - stop.Origin) * progress;
    }

    private static double Duration(Stop stop) => stop.Target > stop.Origin ? GrowMilliseconds : ShrinkMilliseconds;

    private static StreamGeometry Curve(Point from, Point to)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(from, false);
        context.CubicBezierTo(new Point(from.X, (from.Y + to.Y) / 2), new Point(to.X, (from.Y + to.Y) / 2), to);
        context.EndFigure(false);
        return geometry;
    }
}
