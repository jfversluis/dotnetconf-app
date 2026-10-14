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
using Avalonia.Media.Transformation;
using DotNetConf.App.ViewModels;

/// <summary>
/// A session on the timeline: tap to open it, use the heart to save it. Cards are recycled as the timeline
/// scrolls, so one card shows whichever session it is bound to, with as much detail as its size allows.
/// </summary>
internal sealed class SessionCard : Button
{
    // White, softened by role, for text and fills on the saved gradient.
    private static readonly IBrush SavedTime = new ImmutableSolidColorBrush(Colors.White, 0.9);
    private static readonly IBrush SavedSpeakers = new ImmutableSolidColorBrush(Colors.White, 0.85);
    private static readonly IBrush SavedProgress = new ImmutableSolidColorBrush(Colors.White, 0.16);
    private static readonly IBrush SavedBadge = new ImmutableSolidColorBrush(Colors.White, 0.22);

    private readonly PlannerTheme theme;
    private readonly Border glow;
    private readonly Border focusRing;
    private readonly Border surface;
    private readonly Border savedFill;
    private readonly Border progress;
    private readonly Ellipse topicDot;
    private readonly StackPanel textPanel;
    private readonly Grid header;
    private readonly TextBlock time;
    private readonly TextBlock title;
    private readonly TextBlock speakers;
    private readonly Border badge;
    private readonly Ellipse badgeDot;
    private readonly TextBlock badgeText;
    private readonly Animatable[] animated;
    private Color topicColor;
    private string startLabel = "";
    private bool dimmed;
    private bool keyboardFocus;
    private bool ended;
    private bool live;

    public SessionCard(PlannerTheme theme)
    {
        this.theme = theme;
        topicColor = theme.Accent;
        Template = PlannerTheme.ButtonTemplate;
        // Draws its own rounded focus ring instead of the Fluent theme's rectangle.
        FocusAdorner = null;
        RenderTransformOrigin = RelativePoint.Center;
        Transitions =
        [
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(260) },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new CubicEaseOut() },
        ];

        Heart = new HeartButton(theme) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        glow = new Border
        {
            CornerRadius = new CornerRadius(17),
            Margin = new Thickness(-3),
            BorderThickness = new Thickness(3),
            BorderBrush = new ImmutableSolidColorBrush(PlannerTheme.WithAlpha(theme.Conflict, 0.45)),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        glow.Classes.Add("glow");
        savedFill = new Border { Background = theme.SavedFill, Opacity = 0, Transitions = [Fade(320)] };
        progress = new Border
        {
            RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            RenderTransform = Scale(0, 1),
            Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(700), Easing = new CubicEaseOut() }],
        };
        // The topic shows as the same dot as its filter chip, plus a faint tint of the card.
        topicDot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Transitions = [Paint(Shape.FillProperty)],
        };
        time = Text(12, FontWeight.SemiBold);
        title = Text(15, FontWeight.SemiBold);
        title.TextWrapping = TextWrapping.Wrap;
        title.Margin = new Thickness(0, 2, 0, 0);
        speakers = Text(12, FontWeight.Normal);
        speakers.Margin = new Thickness(0, 3, 0, 0);
        badgeDot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        badgeDot.Classes.Add("live-dot");
        badgeText = new TextBlock { FontFamily = PlannerTheme.BodyFont, FontSize = 11, FontWeight = FontWeight.Bold, LetterSpacing = 0.6 };
        badge = new Border
        {
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(7, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { badgeDot, badgeText } },
        };
        // A grid, not a stack, so the time trims instead of running under the heart in narrow lanes.
        header = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(GridLength.Auto)],
            Children = { topicDot, time, badge },
        };
        Grid.SetColumn(time, 1);
        Grid.SetColumn(badge, 2);
        textPanel = new StackPanel { Children = { header, title, speakers } };
        surface = new Border
        {
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Transitions = [Paint(Border.BorderBrushProperty), new BoxShadowsTransition { Property = Border.BoxShadowProperty, Duration = TimeSpan.FromMilliseconds(200) }],
            Child = new Panel { Children = { savedFill, progress, textPanel } },
        };
        // Drawn 2 px clear of the card, so it stands out against the page on saved and unsaved cards alike.
        focusRing = new Border
        {
            Margin = new Thickness(-4),
            BorderThickness = new Thickness(2),
            BorderBrush = new ImmutableSolidColorBrush(theme.Accent),
            IsHitTestVisible = false,
            IsVisible = false,
        };
        Content = new Panel { Children = { glow, surface, Heart, focusRing } };
        // The Fluent theme clips buttons to their bounds, which would cut off the focus ring and the overlap glow.
        ClipToBounds = false;
        AutomationProperties.SetHelpText(this, "Opens session details");
        animated = [this, surface, savedFill, progress, topicDot, time, title, speakers, .. Heart.Animated];
    }

    public SessionViewModel? Session { get; private set; }
    public HeartButton Heart { get; }

    /// <summary>
    /// Shows another session on this card as it stands now: filtered out or not, and live, ended or still to come.
    /// A recycled card takes all of it on at once instead of animating from the session it showed before.
    /// </summary>
    public void Bind(SessionViewModel session, Color topic, bool isDimmed, (bool Live, bool Ended, double Elapsed) state)
    {
        Session = session;
        topicColor = topic;
        startLabel = TimeZoneInfo.ConvertTime(session.Model.StartsAt, TimeZoneInfo.Local).ToString("t");
        time.Text = session.TimeLabel;
        title.Text = session.Title;
        speakers.Text = session.SpeakerNames;
        Command = session.OpenCommand;
        Heart.Bind(session);
        keyboardFocus = false;
        WithoutTransitions(animated, () =>
        {
            ApplyDimmed(isDimmed);
            ApplyTime(state);
            Update();
        });
    }

    /// <summary>
    /// Fades sessions outside the selected topics, keeping the timeline in place.
    /// </summary>
    public void SetDimmed(bool value)
    {
        ApplyDimmed(value);
        Update();
    }

    public void SetTime((bool Live, bool Ended, double Elapsed) state)
    {
        ApplyTime(state);
        Update();
    }

    private void ApplyDimmed(bool value)
    {
        dimmed = value;
        IsHitTestVisible = !value;
        // Sessions outside the filter leave the tab order and the accessibility tree, like the Sessions list.
        Focusable = !value;
        Heart.Focusable = !value;
        var view = value ? AccessibilityView.Raw : AccessibilityView.Default;
        AutomationProperties.SetAccessibilityView(this, view);
        AutomationProperties.SetAccessibilityView(Heart, view);
    }

    private void ApplyTime((bool Live, bool Ended, double Elapsed) state)
    {
        (live, ended) = (state.Live, state.Ended);
        progress.RenderTransform = Scale(state.Live ? Math.Clamp(state.Elapsed, 0, 1) : 0, 1);
    }

    /// <summary>
    /// Shows as much as fits: zoomed out or squeezed between parallel sessions, cards drop the speakers, then the
    /// time, then shrink the title to one small line, down to a colored bar.
    /// </summary>
    public void SetSize(double width, double height)
    {
        var roomy = width >= 120;
        // Narrow lanes give the title the speakers' line.
        var narrow = width < 180;
        // Zoomed far out, a smaller title still fits on one line, centered in the card.
        var compact = height < 28;
        // In narrow lanes the start is enough: the card's height already shows how long it runs.
        time.Text = narrow ? startLabel : Session?.TimeLabel;
        Heart.IsVisible = height >= 44 && width >= 96;
        speakers.IsVisible = height >= 92 && !narrow;
        header.IsVisible = height >= 56 && roomy;
        title.IsVisible = height >= 15 && width >= 48;
        title.FontSize = !compact ? 15 : height >= 19 ? 12 : 11;
        title.Margin = new Thickness(0, compact ? 0 : 2, 0, 0);
        title.MaxLines = height >= 96 && narrow ? 3 : height >= 72 ? 2 : 1;
        textPanel.VerticalAlignment = compact ? VerticalAlignment.Center : VerticalAlignment.Top;
        textPanel.Margin = new Thickness(width >= 64 ? 14 : 8, height >= 56 ? 10 : compact ? 0 : 4, Heart.IsVisible ? 44 : 8, compact ? 0 : 4);
        var radius = Math.Min(14, Math.Min(width, height) / 2);
        surface.CornerRadius = new CornerRadius(radius);
        glow.CornerRadius = new CornerRadius(radius + 3);
        focusRing.CornerRadius = new CornerRadius(radius + 4);
    }

    public void Update()
    {
        if (Session is not { } session)
            return;
        var saved = session.IsSaved;
        savedFill.Opacity = saved ? 1 : 0;
        time.Foreground = saved ? SavedTime : Brush(topicColor);
        title.Foreground = saved ? Brushes.White : Brush(theme.Text);
        speakers.Foreground = saved ? SavedSpeakers : Brush(theme.Secondary);
        topicDot.Fill = saved ? Brushes.White : Brush(topicColor);
        progress.Background = saved ? SavedProgress : Brush(PlannerTheme.WithAlpha(topicColor, 0.12));
        surface.Background = Brush(PlannerTheme.Blend(theme.Surface, topicColor, theme.IsDark ? 0.10 : 0.06));
        surface.BorderBrush = Brush(session.HasConflict ? theme.Conflict : saved ? Colors.Transparent : theme.Outline);
        focusRing.IsVisible = keyboardFocus;
        // The focused card comes forward, so its ring isn't hidden under the next card.
        ZIndex = keyboardFocus ? 1 : 0;
        var shadow = PlannerTheme.WithAlpha(saved ? theme.Magenta : Colors.Black, saved ? 0.32 : theme.IsDark ? 0.4 : 0.08);
        surface.BoxShadow = new BoxShadows(new BoxShadow
        {
            OffsetY = IsPointerOver && !dimmed ? 8 : saved ? 6 : 2,
            Blur = IsPointerOver && !dimmed ? 24 : saved ? 18 : 6,
            Color = shadow,
        });
        glow.Classes.Set("conflict", session.HasConflict);

        var (label, color, showDot) = live ? ("LIVE", theme.Magenta, true)
            : session.HasConflict ? ("OVERLAPS", theme.Conflict, false)
            : ended ? ("ENDED", theme.Secondary, false)
            : ("", theme.Secondary, false);
        badge.IsVisible = label.Length > 0;
        badgeText.Text = label;
        badge.Background = saved ? SavedBadge : Brush(PlannerTheme.WithAlpha(color, 0.14));
        badgeText.Foreground = saved ? Brushes.White : Brush(color);
        badgeDot.IsVisible = showDot;
        badgeDot.Fill = badgeText.Foreground;

        Opacity = (dimmed ? 0.22 : 1) * (ended && !live ? 0.62 : 1);
        RenderTransform = Lift();
        Heart.Update();
        AutomationProperties.SetName(this, session.NavigationDescription);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        // Focus events bubble up from the heart, which draws its own ring.
        keyboardFocus = IsFocused && (e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional);
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
        if (change.Property == IsPointerOverProperty || change.Property == IsPressedProperty)
            Update();
    }

    private TransformOperations Lift()
    {
        var builder = TransformOperations.CreateBuilder(2);
        builder.AppendTranslate(0, IsPointerOver && !dimmed ? -3 : 0);
        var scale = IsPressed ? 0.97 : dimmed ? 0.96 : 1;
        builder.AppendScale(scale, scale);
        return builder.Build();
    }

    private static TextBlock Text(double size, FontWeight weight) => new()
    {
        FontFamily = PlannerTheme.BodyFont,
        FontSize = size,
        FontWeight = weight,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Transitions = [Paint(TextBlock.ForegroundProperty)],
    };

    // Applies a change at once, as when a recycled card shows another session, instead of animating to it.
    private static void WithoutTransitions(IReadOnlyList<Animatable> targets, Action change)
    {
        var saved = targets.Select(target => target.Transitions).ToArray();
        foreach (var target in targets)
            target.Transitions = null;
        try
        {
            change();
        }
        finally
        {
            for (var i = 0; i < targets.Count; i++)
                targets[i].Transitions = saved[i];
        }
    }

    private static ImmutableSolidColorBrush Brush(Color color) => new(color);
    private static DoubleTransition Fade(int milliseconds) => new() { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(milliseconds) };
    private static BrushTransition Paint(AvaloniaProperty property) => new() { Property = property, Duration = TimeSpan.FromMilliseconds(280) };

    private static TransformOperations Scale(double x, double y)
    {
        var builder = TransformOperations.CreateBuilder(1);
        builder.AppendScale(x, y);
        return builder.Build();
    }
}
