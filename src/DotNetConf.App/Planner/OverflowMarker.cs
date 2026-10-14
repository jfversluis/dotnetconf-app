namespace DotNetConf.App.Planner;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;

/// <summary>
/// Counts the parallel sessions that start around a time but don't fit beside the others. A wider window fits more
/// lanes, and the Sessions tab lists every session.
/// </summary>
internal sealed class OverflowMarker : Border
{
    public const double MarkerHeight = 26;

    public OverflowMarker(int count, PlannerTheme theme)
    {
        CornerRadius = new CornerRadius(MarkerHeight / 2);
        Background = new ImmutableSolidColorBrush(PlannerTheme.WithAlpha(theme.Accent, theme.IsDark ? 0.22 : 0.12));
        BorderBrush = new ImmutableSolidColorBrush(PlannerTheme.WithAlpha(theme.Accent, 0.4));
        BorderThickness = new Thickness(1);
        IsHitTestVisible = false;
        var text = count == 1 ? "+1 more" : $"+{count:N0} more";
        AutomationProperties.SetName(this, $"{count:N0} more sessions start around this time");
        Child = new TextBlock
        {
            Text = text,
            FontFamily = PlannerTheme.BodyFont,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = new ImmutableSolidColorBrush(theme.Accent),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
    }
}
