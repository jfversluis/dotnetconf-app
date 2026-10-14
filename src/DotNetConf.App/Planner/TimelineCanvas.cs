namespace DotNetConf.App.Planner;

using Avalonia.Automation.Peers;
using Avalonia.Controls;

/// <summary>
/// The timeline's canvas. Screen readers go through it in time order, not in the order its pooled controls were made.
/// </summary>
internal sealed class TimelineCanvas : Canvas
{
    protected override AutomationPeer OnCreateAutomationPeer() => new TimelineCanvasAutomationPeer(this);
}
