using Microsoft.Maui.Controls.Handlers;

namespace DotNetConf.App;

/// <summary>
/// Switches bottom tabs in place. Shell on Android keeps its tabs in a pager that otherwise scrolls through every
/// tab in between, drawing each of those pages on the way.
/// </summary>
public sealed class InstantTabsHandler : ShellItemHandler
{
    protected override void OnSectionChanged(ShellSection shellSection, bool animate) =>
        base.OnSectionChanged(shellSection, false);
}
