namespace DotNetConf.App.Planner;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using DotNetConf.App.ViewModels;

/// <summary>
/// Counts the parallel sessions that start around a time but don't fit beside the others, and lists them to open when
/// pressed. A wider window fits more lanes, and the Sessions tab lists every session.
/// </summary>
internal sealed class OverflowMarker : Button
{
    public const double MarkerHeight = 26;
    private const double FullTextWidth = 64;

    private readonly PlannerTheme theme;
    private readonly Border pill;
    private readonly TextBlock label;
    private readonly int count;
    private readonly IReadOnlyList<SessionViewModel> sessions;
    private readonly Func<SessionViewModel, bool> isEnabled;
    private bool keyboardFocus;

    public OverflowMarker(int firstItem, IReadOnlyList<SessionViewModel> sessions, Func<SessionViewModel, bool> isEnabled, PlannerTheme theme)
    {
        this.theme = theme;
        count = sessions.Count;
        FirstItem = firstItem;
        Template = PlannerTheme.ButtonTemplate;
        // Draws its own focus ring instead of the Fluent theme's rectangle.
        FocusAdorner = null;
        label = new TextBlock
        {
            FontFamily = PlannerTheme.BodyFont,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = new ImmutableSolidColorBrush(theme.Accent),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        pill = new Border { CornerRadius = new CornerRadius(MarkerHeight / 2), Child = label };
        Content = pill;
        KeyboardNavigation.SetTabIndex(this, firstItem);
        AutomationProperties.SetName(this, $"{count:N0} more sessions start around this time");
        AutomationProperties.SetHelpText(this, "Lists them");
        this.sessions = sessions;
        this.isEnabled = isEnabled;
        Update();
    }

    public int FirstItem { get; }

    /// <summary>
    /// Shortens the count to fit a narrow strip.
    /// </summary>
    public void Fit(double width) =>
        label.Text = width < FullTextWidth ? $"+{count:N0}" : count == 1 ? "+1 more" : $"+{count:N0} more";

    protected override void OnClick()
    {
        base.OnClick();
        // Built on each press, so sessions outside the topic filter are disabled like their cards.
        var menu = new MenuFlyout();
        foreach (var session in sessions)
        {
            var start = TimeZoneInfo.ConvertTime(session.Model.StartsAt, TimeZoneInfo.Local).ToString("t");
            menu.Items.Add(new MenuItem { Header = $"{start}  {session.Title}", Command = session.OpenCommand, IsEnabled = isEnabled(session) });
        }
        menu.ShowAt(this);
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
        if (change.Property == IsPointerOverProperty)
            Update();
    }

    private void Update()
    {
        var fill = IsPointerOver ? (theme.IsDark ? 0.32 : 0.2) : (theme.IsDark ? 0.22 : 0.12);
        pill.Background = new ImmutableSolidColorBrush(PlannerTheme.WithAlpha(theme.Accent, fill));
        pill.BorderBrush = new ImmutableSolidColorBrush(keyboardFocus ? theme.Accent : PlannerTheme.WithAlpha(theme.Accent, 0.4));
        pill.BorderThickness = new Thickness(keyboardFocus ? 2 : 1);
    }
}
