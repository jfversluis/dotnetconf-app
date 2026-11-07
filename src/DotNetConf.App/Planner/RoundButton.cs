namespace DotNetConf.App.Planner;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Transformation;

/// <summary>
/// A small round text button, used for zooming the timeline without gestures. It dims while its command can't run.
/// </summary>
internal sealed class RoundButton : Button
{
    private readonly PlannerTheme theme;
    private readonly Border circle;
    private readonly Panel target;
    private readonly TextBlock glyphView;
    private bool keyboardFocus;
    private double textScale = 1;

    public RoundButton(string glyph, string name, PlannerTheme theme)
    {
        this.theme = theme;
        Template = PlannerTheme.ButtonTemplate;
        // Draws its own rounded focus ring instead of the Fluent theme's rectangle.
        FocusAdorner = null;
        RenderTransformOrigin = RelativePoint.Center;
        Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(140), Easing = new CubicEaseOut() }];
        glyphView = new TextBlock
        {
            Text = glyph,
            FontFamily = PlannerTheme.BodyFont,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = new ImmutableSolidColorBrush(theme.Accent),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        circle = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = glyphView,
        };
        target = new Panel { Width = 48, Height = 48, Background = Brushes.Transparent, Children = { circle } };
        Content = target;
        AutomationProperties.SetName(this, name);
        Update();
    }

    public void SetTextScale(double value)
    {
        textScale = Math.Max(value, 1);
        Width = Height = target.Width = target.Height = 48 / textScale;
        circle.Width = circle.Height = 36 / textScale;
        circle.CornerRadius = new CornerRadius(18 / textScale);
        glyphView.FontSize = 18 / textScale;
        Update();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        keyboardFocus = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
        Update();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        keyboardFocus = false;
        Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPressedProperty || change.Property == IsEffectivelyEnabledProperty)
            Update();
    }

    private void Update()
    {
        circle.Background = new ImmutableSolidColorBrush(theme.Surface);
        circle.BorderBrush = new ImmutableSolidColorBrush(keyboardFocus ? theme.Accent : theme.Outline);
        circle.BorderThickness = new Thickness((keyboardFocus ? 2 : 1) / textScale);
        Opacity = IsEffectivelyEnabled ? 1 : 0.4;
        var builder = TransformOperations.CreateBuilder(1);
        var scale = IsPressed ? 0.9 : 1;
        builder.AppendScale(scale, scale);
        RenderTransform = builder.Build();
    }
}
