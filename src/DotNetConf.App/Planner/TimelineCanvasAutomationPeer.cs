namespace DotNetConf.App.Planner;

using Avalonia.Automation.Peers;
using Avalonia.Controls;

/// <summary>
/// Orders the timeline's children as they read on screen, top to bottom and then left to right. A pinned day header
/// counts from where its day starts, so it is still read before that day's sessions.
/// </summary>
internal sealed class TimelineCanvasAutomationPeer(TimelineCanvas owner) : ControlAutomationPeer(owner)
{
    protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => base.GetChildrenCore()?
        .OrderBy(peer => peer is ControlAutomationPeer { Owner: var control } ? Top(control) : 0)
        .ThenBy(peer => peer is ControlAutomationPeer { Owner: var control } ? Canvas.GetLeft(control) : 0)
        .ToList();

    private static double Top(Control control) => control is DayHeader header ? header.NaturalTop : Canvas.GetTop(control);
}
