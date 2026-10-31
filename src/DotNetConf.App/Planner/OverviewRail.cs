namespace DotNetConf.App.Planner;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

/// <summary>
/// The personal schedule in one strip beside the timeline, in place of its scrollbar: how busy each part is, the saved
/// sessions, the current time and the part on screen. Drag it, or tap anywhere on it, to get there.
/// </summary>
internal sealed class OverviewRail : Control
{
    public const double RailWidth = 16;
    private const double TrackWidth = 6;
    private const double MinThumbHeight = 24;
    private const int Shades = 6;
    // How far one notch of the wheel scrolls the timeline over the rail, as over a scrollbar.
    private const double WheelStep = 50;

    private readonly ScrollViewer scroller;
    private (int Day, double From, double To, double Level)[] busy = [];
    private (int Day, double Offset, bool Conflict)[] saved = [];
    private (int Day, double Offset)? now;
    private double[] dayStarts = [];
    private double minuteHeight = 1;
    private PlannerTheme? theme;
    private IBrush[] shades = [];
    private IBrush track = Brushes.Transparent;
    private IBrush savedMark = Brushes.Transparent;
    private IBrush conflictMark = Brushes.Transparent;
    private IBrush nowFill = Brushes.Transparent;
    private IPen nowRing = new ImmutablePen(Brushes.Transparent);
    private (IBrush Fill, IPen Ring) thumb = (Brushes.Transparent, new ImmutablePen(Brushes.Transparent));
    private (IBrush Fill, IPen Ring) activeThumb = (Brushes.Transparent, new ImmutablePen(Brushes.Transparent));
    // The busiest level in each pixel row, resampled when the rail or the timeline changes size.
    private float[] rows = [];
    private (double Height, double Extent) sampled;
    private double? grab;

    static OverviewRail() => AffectsRender<OverviewRail>(IsPointerOverProperty);

    public OverviewRail(ScrollViewer scroller)
    {
        this.scroller = scroller;
        Width = RailWidth;
        Cursor = new Cursor(StandardCursorType.Hand);
        // The timeline itself is the accessible way through the agenda; this only repeats it as a picture.
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
    }

    public void SetTheme(PlannerTheme value)
    {
        theme = value;
        ImmutableSolidColorBrush Brush(Color color, double alpha = 1) => new(PlannerTheme.WithAlpha(color, alpha));
        shades = Enumerable.Range(1, Shades).Select(shade => (IBrush)Brush(value.Accent, 0.3 + 0.7 * shade / Shades)).ToArray();
        track = Brush(value.Outline, value.IsDark ? 0.5 : 0.7);
        savedMark = Brush(value.Magenta);
        conflictMark = Brush(value.Conflict);
        nowFill = Brush(value.Magenta);
        nowRing = new ImmutablePen(Brush(value.Background), 1.5);
        thumb = (Brush(value.Accent, 0.1), new ImmutablePen(Brush(value.Accent, 0.7), 1.5));
        activeThumb = (Brush(value.Accent, 0.2), new ImmutablePen(Brush(value.Accent), 1.5));
        InvalidateVisual();
    }

    /// <summary>
    /// How busy each stretch of the agenda is, as the layout reports it.
    /// </summary>
    public void SetBusy((int Day, double From, double To, double Level)[] value)
    {
        busy = value;
        Resample();
        InvalidateVisual();
    }

    public void SetMarks((int Day, double Offset, bool Conflict)[] savedSessions, (int Day, double Offset)? current)
    {
        saved = savedSessions;
        now = current;
        InvalidateVisual();
    }

    /// <summary>
    /// Where each day's timeline starts on the canvas and how tall a minute is, after a zoom or resize.
    /// </summary>
    public void SetScale(double[] starts, double minutes)
    {
        dayStarts = starts;
        minuteHeight = minutes;
        Resample();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var (height, extent, viewport) = (Bounds.Height, scroller.Extent.Height, scroller.Viewport.Height);
        if (theme is null || height <= 0 || extent <= viewport + 1 || dayStarts.Length == 0)
            return;
        if (sampled != (height, extent))
            Resample();
        var scale = height / extent;
        var left = (Bounds.Width - TrackWidth) / 2;
        // The whole strip takes taps, not only what is drawn on it.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        context.DrawRectangle(track, null, new RoundedRect(new Rect(left, 0, TrackWidth, height), TrackWidth / 2));

        for (var row = 0; row < rows.Length;)
        {
            if (rows[row] <= 0)
            {
                row++;
                continue;
            }
            var end = row + 1;
            while (end < rows.Length && rows[end] > 0)
                end++;
            // Each busy stretch is one rounded segment, shaded inside by how many sessions run at once.
            using (context.PushClip(new RoundedRect(new Rect(left, row, TrackWidth, end - row), Math.Min(TrackWidth, end - row) / 2)))
            {
                for (var run = row; run < end;)
                {
                    var shade = Shade(rows[run]);
                    var next = run + 1;
                    while (next < end && Shade(rows[next]) == shade)
                        next++;
                    context.FillRectangle(shades[shade - 1], new Rect(left, run, TrackWidth, next - run));
                    run = next;
                }
            }
            row = end;
        }

        var (thumbTop, thumbHeight) = ThumbBounds();
        var bounds = new RoundedRect(new Rect(1, thumbTop, Bounds.Width - 2, thumbHeight), (Bounds.Width - 2) / 2);
        var (fill, ring) = IsPointerOver || grab is not null ? activeThumb : thumb;
        context.DrawRectangle(fill, null, bounds);
        foreach (var (day, offset, conflict) in saved)
        {
            var y = (dayStarts[day] + offset * minuteHeight) * scale;
            context.DrawRectangle(conflict ? conflictMark : savedMark, null, new RoundedRect(new Rect(left - 2, y - 1.5, TrackWidth + 4, 3), 1.5));
        }
        if (now is { } current)
            context.DrawEllipse(nowFill, nowRing, new Point(Bounds.Width / 2, (dayStarts[current.Day] + current.Offset * minuteHeight) * scale), 4, 4);
        context.DrawRectangle(null, ring, bounds);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var y = e.GetPosition(this).Y;
        var (thumbTop, thumbHeight) = ThumbBounds();
        // Grabbing the thumb keeps the spot under the finger; anywhere else centers the screen there.
        grab = y >= thumbTop && y <= thumbTop + thumbHeight ? y - thumbTop : thumbHeight / 2;
        e.Pointer.Capture(this);
        ScrollTo(y);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (grab is not null)
            ScrollTo(e.GetPosition(this).Y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        grab = null;
        InvalidateVisual();
    }

    // Over the rail, the wheel scrolls the timeline as it would over a scrollbar.
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        scroller.Offset = scroller.Offset.WithY(scroller.Offset.Y - e.Delta.Y * WheelStep);
        e.Handled = true;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Resample();
        InvalidateVisual();
    }

    // The thumb is the screen's share of the rail, at least MinThumbHeight tall, and travels the whole rail as the
    // timeline scrolls from top to bottom. Drawing and dragging share this, so the thumb stays under the finger.
    private (double Top, double Height) ThumbBounds()
    {
        var (extent, viewport) = (Math.Max(1, scroller.Extent.Height), scroller.Viewport.Height);
        var height = Math.Min(Bounds.Height, Math.Max(MinThumbHeight, viewport * Bounds.Height / extent));
        var progress = scroller.Offset.Y / Math.Max(1, extent - viewport);
        return (Math.Clamp(progress, 0, 1) * (Bounds.Height - height), height);
    }

    private void ScrollTo(double y)
    {
        var (_, height) = ThumbBounds();
        var progress = (y - (grab ?? 0)) / Math.Max(1, Bounds.Height - height);
        scroller.Offset = scroller.Offset.WithY(Math.Clamp(progress, 0, 1) * Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height));
    }

    private void Resample()
    {
        var (height, extent) = (Bounds.Height, scroller.Extent.Height);
        sampled = (height, extent);
        if (height <= 0 || extent <= 0 || dayStarts.Length == 0)
        {
            rows = [];
            return;
        }
        if (rows.Length != (int)Math.Ceiling(height))
            rows = new float[(int)Math.Ceiling(height)];
        else
            Array.Clear(rows);
        var scale = height / extent;
        foreach (var (day, from, to, level) in busy)
        {
            if (day >= dayStarts.Length)
                continue;
            var top = (dayStarts[day] + from * minuteHeight) * scale;
            var bottom = Math.Min(rows.Length, Math.Ceiling((dayStarts[day] + to * minuteHeight) * scale));
            for (var row = Math.Max(0, (int)top); row < bottom; row++)
                rows[row] = Math.Max(rows[row], (float)level);
        }
    }

    private static int Shade(float level) => level <= 0 ? 0 : 1 + (int)Math.Round(level * (Shades - 1));
}
