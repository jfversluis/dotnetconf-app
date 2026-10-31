namespace DotNetConf.App.Planner;

using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using DotNetConf.Core;

/// <summary>
/// What the timeline draws behind its cards: the hours on the ruler with their grid lines, the breaks that stand in
/// for long gaps, and hatching where saved sessions overlap. The view fills in what is on screen before each redraw.
/// </summary>
internal sealed class TimelineBackdrop : Control
{
    private const double BreakPadding = 12;
    private const int HourLabel = 0;
    private const int OverlapLabel = 1;
    private const int BreakLabel = 2;

    // Formatted labels are kept between redraws; the same few hours and durations come back as the timeline scrolls.
    private readonly Dictionary<(string Text, int Style), FormattedText> labels = [];
    private PlannerTheme? theme;
    private IBrush gridLine = Brushes.Transparent;
    private IPen breakPen = new ImmutablePen(Brushes.Transparent);
    private IPen bandPen = new ImmutablePen(Brushes.Transparent);
    private IBrush hatch = Brushes.Transparent;

    public TimelineBackdrop()
    {
        IsHitTestVisible = false;
        ZIndex = -2;
        // Grayscale like the text around it; subpixel rendering on this transparent layer leaves color fringes.
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        // Decoration only: each card already tells its time, and saved overlaps are marked on the cards.
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }

    public List<(double Y, DateTimeOffset Hour)> Hours { get; } = [];
    public List<(double Top, double Height, TimeSpan Gap)> Breaks { get; } = [];
    public List<(double Top, double Height, TimeSpan Overlap)> Bands { get; } = [];

    /// <summary>
    /// The content column the ruler and the cards share.
    /// </summary>
    public (double Left, double Width, double RulerWidth) Column { get; set; }

    /// <summary>
    /// Forgets what was on screen, as when the timeline is laid out again.
    /// </summary>
    public void Clear()
    {
        Hours.Clear();
        Breaks.Clear();
        Bands.Clear();
        InvalidateVisual();
    }

    public void SetTheme(PlannerTheme value)
    {
        theme = value;
        labels.Clear();
        gridLine = new ImmutableSolidColorBrush(PlannerTheme.WithAlpha(value.Outline, 0.7));
        breakPen = new ImmutablePen(new ImmutableSolidColorBrush(value.Outline));
        bandPen = new ImmutablePen(new ImmutableSolidColorBrush(PlannerTheme.WithAlpha(value.Conflict, 0.5)));
        hatch = new DrawingBrush
        {
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, 10, 10, RelativeUnit.Absolute),
            Drawing = new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing
                    {
                        Brush = new ImmutableSolidColorBrush(PlannerTheme.WithAlpha(value.Conflict, 0.08)),
                        Geometry = new RectangleGeometry(new Rect(0, 0, 10, 10)),
                    },
                    new GeometryDrawing
                    {
                        Pen = new Pen(new ImmutableSolidColorBrush(PlannerTheme.WithAlpha(value.Conflict, 0.35)), 1.5),
                        Geometry = new LineGeometry(new Point(0, 10), new Point(10, 0)),
                    },
                },
            },
        };
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (theme is null)
            return;
        var (left, width, ruler) = Column;
        var right = left + width;

        foreach (var (y, hour) in Hours)
        {
            context.FillRectangle(gridLine, new Rect(left + ruler, Math.Round(y), width - ruler, 1));
            var label = Label(hour.ToString("t"), HourLabel, ruler - 22);
            context.DrawText(label, new Point(left, y - label.Height / 2));
        }

        foreach (var (top, height, overlap) in Bands)
        {
            context.DrawRectangle(hatch, bandPen, new RoundedRect(new Rect(left + ruler - 6, top, width - ruler + 12, height), 12));
            var label = Label($"{Agenda.Duration(overlap)}\noverlap", OverlapLabel, ruler - 26);
            context.DrawText(label, new Point(left, top + (height - label.Height) / 2));
        }

        foreach (var (top, height, gap) in Breaks)
        {
            var label = Label($"{Agenda.Duration(gap)} break", BreakLabel, double.PositiveInfinity);
            var pill = new Rect(0, 0, label.Width + 2 * BreakPadding, label.Height + 8);
            pill = pill.WithX(left + ruler + (width - ruler - pill.Width) / 2).WithY(top + (height - pill.Height) / 2);
            var y = Math.Round(top + height / 2) + 0.5;
            context.DrawLine(breakPen, new Point(left + ruler, y), new Point(pill.Left - 10, y));
            context.DrawLine(breakPen, new Point(pill.Right + 10, y), new Point(right, y));
            context.DrawRectangle(null, breakPen, new RoundedRect(pill, pill.Height / 2));
            context.DrawText(label, new Point(pill.X + BreakPadding, pill.Y + 4));
        }
    }

    // Hours and overlaps sit right-aligned in the ruler; breaks are centered over the timeline.
    private FormattedText Label(string text, int style, double maxWidth)
    {
        if (labels.TryGetValue((text, style), out var label))
            return label;
        label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(PlannerTheme.BodyFont, style == BreakLabel ? FontStyle.Italic : FontStyle.Normal,
                style == OverlapLabel ? FontWeight.SemiBold : FontWeight.Normal),
            style == BreakLabel ? 12 : 11, new ImmutableSolidColorBrush(style == OverlapLabel ? theme!.Conflict : theme!.Secondary));
        if (style != BreakLabel)
        {
            label.MaxTextWidth = Math.Max(1, maxWidth);
            label.TextAlignment = TextAlignment.Right;
        }
        labels[(text, style)] = label;
        return label;
    }
}
