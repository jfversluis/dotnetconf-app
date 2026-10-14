namespace DotNetConf.App.Planner;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

/// <summary>
/// The current time across the timeline: a labelled, pulsing marker that glides as the minutes pass.
/// </summary>
internal sealed class NowIndicator : Grid
{
    public const double MarkerHeight = 24;

    // A ring that keeps leaving the marker, growing as it fades.
    private static readonly Animation Ripple = new()
    {
        Duration = TimeSpan.FromMilliseconds(1600),
        IterationCount = IterationCount.Infinite,
        Easing = new CubicEaseOut(),
        Children =
        {
            new KeyFrame
            {
                Cue = new Cue(0),
                Setters = { new Setter(ScaleTransform.ScaleXProperty, 1d), new Setter(ScaleTransform.ScaleYProperty, 1d), new Setter(OpacityProperty, 0.8) },
            },
            new KeyFrame
            {
                Cue = new Cue(1),
                Setters = { new Setter(ScaleTransform.ScaleXProperty, 2.8), new Setter(ScaleTransform.ScaleYProperty, 2.8), new Setter(OpacityProperty, 0d) },
            },
        },
    };

    private readonly TextBlock label;
    private readonly Ellipse ring;
    private CancellationTokenSource? rippling;
    private readonly Transitions glides = [new DoubleTransition { Property = Canvas.TopProperty, Duration = TimeSpan.FromMilliseconds(700), Easing = new CubicEaseInOut() }];

    public NowIndicator(PlannerTheme theme, double rulerWidth)
    {
        ColumnDefinitions = [new ColumnDefinition(rulerWidth, GridUnitType.Pixel), new ColumnDefinition(1, GridUnitType.Star)];
        Height = MarkerHeight;
        IsHitTestVisible = false;
        // Behind the cards so it never crosses their text; live cards show their own progress.
        ZIndex = -1;

        var magenta = new ImmutableSolidColorBrush(theme.Magenta);
        label = new TextBlock { FontFamily = PlannerTheme.BodyFont, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brushes.White };
        var pill = new Border
        {
            Background = magenta,
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(7, 2),
            Margin = new Thickness(0, 0, 22, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Child = label,
        };
        var line = new Border
        {
            Height = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops = { new GradientStop(theme.Magenta, 0), new GradientStop(PlannerTheme.WithAlpha(theme.Magenta, 0.1), 1) },
            },
        };
        ring = Dot(theme.Magenta, filled: false);
        ring.RenderTransform = new ScaleTransform();
        ring.IsVisible = false;
        var dot = Dot(theme.Magenta, filled: true);
        SetColumn(line, 1);
        SetColumn(ring, 1);
        SetColumn(dot, 1);
        Children.AddRange([pill, line, ring, dot]);
    }

    /// <summary>
    /// Where the marker is headed; it may still be gliding there.
    /// </summary>
    public double Top { get; private set; } = double.NaN;

    public void SetTime(DateTimeOffset localTime)
    {
        label.Text = localTime.ToString("t");
        AutomationProperties.SetName(this, $"Now, {localTime:t}");
    }

    /// <summary>
    /// Places the marker. It glides when time moves it on, and jumps with everything else when the timeline zooms or
    /// resizes. The first placement can't glide: it would start from NaN.
    /// </summary>
    public void MoveTo(double top, bool glide)
    {
        Transitions = glide && !double.IsNaN(Top) ? glides : null;
        Canvas.SetTop(this, top);
        Top = top;
    }

    /// <summary>
    /// Starts or stops the ripple that keeps leaving the marker, which reduce motion turns off.
    /// </summary>
    public void SetRipple(bool on)
    {
        if (on == (rippling is not null))
            return;
        rippling?.Cancel();
        rippling = on ? new CancellationTokenSource() : null;
        ring.IsVisible = on;
        if (rippling is { } running)
            _ = Ripple.RunAsync(ring, running.Token);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SetRipple(false);
        base.OnDetachedFromVisualTree(e);
    }

    private static Ellipse Dot(Color color, bool filled) => new()
    {
        Width = 10,
        Height = 10,
        Margin = new Thickness(-5, 0, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        RenderTransformOrigin = RelativePoint.Center,
        Fill = filled ? new ImmutableSolidColorBrush(color) : null,
        Stroke = filled ? null : new ImmutableSolidColorBrush(color),
        StrokeThickness = 2,
    };
}
