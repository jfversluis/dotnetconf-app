namespace DotNetConf.App.Planner;

using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using DotNetConf.Core;

/// <summary>
/// A day's name, date and session count above its timeline. The day at the top of the screen keeps its header
/// pinned there, over a backdrop that fades into the sessions scrolling underneath, until its last sessions push it up.
/// </summary>
internal sealed class DayHeader : Grid
{
    /// <summary>
    /// The fade below the header, part of its height so the canvas places both together.
    /// </summary>
    public const double FadeHeight = 16;

    private readonly Border backdrop;
    private readonly Border fade;

    public DayHeader(TimelineDay day, PlannerTheme theme)
    {
        RowDefinitions = [new RowDefinition(1, GridUnitType.Star), new RowDefinition(FadeHeight, GridUnitType.Pixel)];
        // Above the cards, even a focused one, so a pinned header hides the sessions passing beneath it.
        ZIndex = 2;
        backdrop = new Border { Background = new ImmutableSolidColorBrush(theme.Background), IsVisible = false };
        fade = new Border
        {
            IsHitTestVisible = false,
            Opacity = 0,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(theme.Background, 0), new GradientStop(PlannerTheme.WithAlpha(theme.Background, 0), 1) },
            },
        };
        SetRow(fade, 1);

        var date = day.Day.ToDateTime(TimeOnly.MinValue);
        var count = day.Items.Count;
        var dayName = date.ToString("dddd");
        var heading = new TextBlock
        {
            // As a heading the day starts with a capital, even in languages that write it in lower case.
            Text = CultureInfo.CurrentCulture.TextInfo.ToUpper(dayName[0]) + dayName[1..],
            FontFamily = PlannerTheme.HeadingFont,
            FontWeight = FontWeight.Bold,
            FontSize = 24,
            Foreground = new ImmutableSolidColorBrush(theme.IsDark ? Colors.White : theme.Midnight),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        AutomationProperties.SetHeadingLevel(heading, 2);
        var details = new TextBlock
        {
            Text = $"{Agenda.MonthDay(day.Day)}  ·  {count:N0} {(count == 1 ? "session" : "sessions")}",
            FontFamily = PlannerTheme.BodyFont,
            FontSize = 13,
            Margin = new Thickness(10, 0, 0, 4),
            Foreground = new ImmutableSolidColorBrush(theme.Secondary),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        var text = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(4, 0, 0, 12),
            Children = { heading, details },
        };
        Children.AddRange([backdrop, fade, text]);
    }

    /// <summary>
    /// Where the header sits when it isn't pinned, at the top of its day.
    /// </summary>
    public double NaturalTop { get; set; }

    /// <summary>
    /// Shows the backdrop while pinned, which also keeps taps from reaching sessions hidden under it.
    /// The fade grows in as the header leaves its place and shrinks as the next day arrives.
    /// </summary>
    public void SetPinned(bool pinned, double fadeOpacity)
    {
        backdrop.IsVisible = pinned;
        fade.Opacity = pinned ? Math.Clamp(fadeOpacity, 0, 1) : 0;
    }
}
