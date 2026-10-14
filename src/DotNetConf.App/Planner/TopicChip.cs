namespace DotNetConf.App.Planner;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Transformation;

/// <summary>
/// A topic filter pill. The dot matches the accent of that topic's sessions on the timeline.
/// </summary>
internal sealed class TopicChip : ToggleButton
{
    private readonly Color color;
    private readonly PlannerTheme theme;
    private readonly Border pill;
    private readonly Border focusRing;
    private readonly Ellipse dot;
    private readonly TextBlock label;
    private bool keyboardFocus;

    public TopicChip(string text, Color color, PlannerTheme theme)
    {
        this.color = color;
        this.theme = theme;
        Template = PlannerTheme.ButtonTemplate;
        // Draws its own rounded focus ring instead of the Fluent theme's rectangle.
        FocusAdorner = null;
        RenderTransformOrigin = RelativePoint.Center;
        Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = new CubicEaseOut() }];
        dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        label = new TextBlock
        {
            Text = text,
            FontFamily = PlannerTheme.BodyFont,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Transitions = [new BrushTransition { Property = TextBlock.ForegroundProperty, Duration = TimeSpan.FromMilliseconds(220) }],
        };
        pill = new Border
        {
            CornerRadius = new CornerRadius(999),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 0),
            MinHeight = 36,
            Margin = new Thickness(0, 0, 8, 8),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, label } },
            Transitions =
            [
                new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(220) },
                new BrushTransition { Property = Border.BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(220) },
            ],
        };
        // Drawn 2 px clear of the pill, so it stands out against the page whatever the chip's fill.
        focusRing = new Border
        {
            CornerRadius = new CornerRadius(999),
            BorderThickness = new Thickness(2),
            BorderBrush = new ImmutableSolidColorBrush(theme.Accent),
            Margin = new Thickness(-4, -4, 4, 4),
            IsHitTestVisible = false,
            IsVisible = false,
        };
        Content = new Panel { Children = { pill, focusRing } };
        // The Fluent theme clips buttons to their bounds, which would cut off the focus ring.
        ClipToBounds = false;
        AutomationProperties.SetName(this, $"Filter by {text}");
        Update();
    }

    // The checked state always mirrors the app's topic filters, which the command changes.
    protected override void Toggle()
    {
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
        if (change.Property == IsCheckedProperty || change.Property == IsPressedProperty)
            Update();
    }

    private void Update()
    {
        var selected = IsChecked == true;
        var onColor = theme.IsDark ? theme.Midnight : Colors.White;
        pill.Background = new ImmutableSolidColorBrush(selected ? color : theme.Surface);
        pill.BorderBrush = new ImmutableSolidColorBrush(selected ? color : theme.Outline);
        focusRing.IsVisible = keyboardFocus;
        label.Foreground = new ImmutableSolidColorBrush(selected ? onColor : theme.Text);
        dot.Fill = new ImmutableSolidColorBrush(selected ? onColor : color);
        var builder = TransformOperations.CreateBuilder(1);
        var scale = IsPressed ? 0.93 : 1;
        builder.AppendScale(scale, scale);
        RenderTransform = builder.Build();
    }
}
