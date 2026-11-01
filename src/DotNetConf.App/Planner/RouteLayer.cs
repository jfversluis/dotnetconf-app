namespace DotNetConf.App.Planner;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Stop = (int Day, double From, double To, bool Branch, bool Conflict);

/// <summary>
/// Your route through each day, in the gutter beside the cards: a track along every saved session, dashes over the
/// free time between them, and a branch where two of them overlap.
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
    private IReadOnlyList<Stop> stops = [];
    private double[] dayStarts = [];
    private double minuteHeight = 1;
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
    /// Clears the route while the layout is rebuilt.
    /// </summary>
    public void Reset()
    {
        stops = [];
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
    /// The saved sessions in chronological order, at their current timeline positions.
    /// </summary>
    public void SetStops(IReadOnlyList<Stop> value)
    {
        stops = value;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (stops.Count == 0)
            return;
        var mainX = Bounds.Width - 9;
        // Dashes over the free time between one saved session and the next on the same day.
        var (day, reach) = (-1, 0.0);
        foreach (var stop in stops)
        {
            if (stop.Day >= dayStarts.Length)
                continue;
            var top = Y(stop.Day, stop.From) + Inset;
            var bottom = Y(stop.Day, stop.To) - Inset;
            if (stop.Day == day && top - reach >= 14)
                context.DrawLine(dashes, new Point(mainX, reach + 6), new Point(mainX, top - 7));
            if (stop.Day != day || bottom > reach)
                reach = bottom;
            day = stop.Day;
            var x = stop.Branch ? mainX - BranchSpacing : mainX;
            if (stop.Branch)
            {
                // The route branches off for an overlapping session and rejoins after it.
                context.DrawGeometry(null, branch, Curve(new Point(mainX, top - 14), new Point(x, top)));
                context.DrawGeometry(null, branch, Curve(new Point(x, bottom), new Point(mainX, bottom + 14)));
            }
            context.DrawRectangle(stop.Conflict ? conflictTrack : track, null,
                new RoundedRect(new Rect(x - TrackWidth / 2, top, TrackWidth, Math.Max(0, bottom - top)), TrackWidth / 2));
            context.DrawEllipse(nodeFill, stop.Conflict ? conflictRing : nodeRing, new Point(x, top), 4.5, 4.5);
        }
    }

    private double Y(int day, double minute) => dayStarts[day] + minute * minuteHeight;

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
