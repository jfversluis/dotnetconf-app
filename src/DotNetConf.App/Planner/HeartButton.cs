namespace DotNetConf.App.Planner;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using DotNetConf.App.ViewModels;

/// <summary>
/// Removes a saved session from the timeline.
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
    }

    public void Bind(SessionViewModel value) => session = value;

    public void Update()
    {
        var saved = session?.IsSaved == true;
        var color = theme.Accent;
        focusRing.BorderBrush = new ImmutableSolidColorBrush(color);
        heart.Stroke = new ImmutableSolidColorBrush(color);
        heart.Fill = new ImmutableSolidColorBrush(saved ? color : Colors.Transparent);
        AutomationProperties.SetName(this, session?.SaveHint);
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
