namespace DotNetConf.App.Planner;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using DotNetConf.App.ViewModels;

/// <summary>
/// Saves or removes the session, with a pop when it changes.
/// </summary>
internal sealed class HeartButton : Button
{
    private readonly PlannerTheme theme;
    private readonly Path heart;
    private readonly Border focusRing;
    private SessionViewModel? session;

    public HeartButton(PlannerTheme theme)
    {
        this.theme = theme;
        Template = PlannerTheme.ButtonTemplate;
        // Draws its own rounded focus ring instead of the Fluent theme's rectangle.
        FocusAdorner = null;
        heart = new Path
        {
            Data = PlannerTheme.HeartIcon,
            Width = 22,
            Height = 22,
            Stretch = Stretch.Uniform,
            StrokeThickness = 2,
            StrokeJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = new ScaleTransform(),
            Transitions = [new BrushTransition { Property = Shape.FillProperty, Duration = TimeSpan.FromMilliseconds(220) }],
        };
        focusRing = new Border
        {
            CornerRadius = new CornerRadius(22),
            BorderThickness = new Thickness(2),
            BorderBrush = new ImmutableSolidColorBrush(theme.Accent),
            IsVisible = false,
        };
        // A transparent background makes the whole 44 × 44 square tappable.
        Content = new Panel { Width = 44, Height = 44, Background = Brushes.Transparent, Children = { focusRing, heart } };
        Animated = [heart];
    }

    /// <summary>
    /// What animates on the heart, so a recycled card can show its new state at once.
    /// </summary>
    public IReadOnlyList<Animatable> Animated { get; }

    /// <summary>
    /// The heart's center, for the particle burst.
    /// </summary>
    public Point Center => new(Bounds.Width / 2, Bounds.Height / 2);

    public void Bind(SessionViewModel value) => session = value;

    public void Update()
    {
        var saved = session?.IsSaved == true;
        var color = saved ? Colors.White : theme.Accent;
        focusRing.BorderBrush = new ImmutableSolidColorBrush(color);
        heart.Stroke = new ImmutableSolidColorBrush(color);
        heart.Fill = new ImmutableSolidColorBrush(saved ? color : Colors.Transparent);
        AutomationProperties.SetName(this, session?.SaveHint);
    }

    /// <summary>
    /// A quick bounce as the session is saved, or a shrink as it is removed.
    /// </summary>
    public void Pop(bool grow)
    {
        KeyFrame Frame(double cue, double scale) => new()
        {
            Cue = new Cue(cue),
            Setters = { new Setter(ScaleTransform.ScaleXProperty, scale), new Setter(ScaleTransform.ScaleYProperty, scale) },
        };

        _ = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(grow ? 480 : 300),
            Easing = new CubicEaseOut(),
            Children = { Frame(0, 1), Frame(0.35, grow ? 1.4 : 0.7), Frame(0.7, grow ? 0.9 : 1.08), Frame(1, 1) },
        }.RunAsync(heart);
    }

    // Runs the save command without binding it: a bound command disables the button while the save is in progress,
    // which would drop keyboard focus. Taps during a save are still ignored, so a quick double tap doesn't undo it.
    protected override void OnClick()
    {
        base.OnClick();
        if (session?.ToggleSaveCommand is { } command && command.CanExecute(null))
            command.Execute(null);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        focusRing.IsVisible = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        focusRing.IsVisible = false;
    }
}
