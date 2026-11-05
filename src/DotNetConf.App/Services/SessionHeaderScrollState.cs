namespace DotNetConf.App.Services;

public sealed class SessionHeaderScrollState
{
    private double previousOffset;
    private double previousViewportHeight;
    private double travel;

    public bool IsCollapsed { get; private set; }

    public void Update(double verticalOffset, double viewportHeight, bool keepExpanded = false, bool rebase = false)
    {
        if (!double.IsFinite(verticalOffset))
            throw new ArgumentOutOfRangeException(nameof(verticalOffset));
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));

        var offset = Math.Max(0, verticalOffset);
        var delta = offset - previousOffset;
        var viewportChanged = previousViewportHeight > 0 && Math.Abs(viewportHeight - previousViewportHeight) > 1;
        previousOffset = offset;
        previousViewportHeight = viewportHeight;
        if (keepExpanded)
        {
            Expand();
            return;
        }
        // Growing the list can clamp its offset, especially for a short filtered agenda.
        // That is a layout adjustment, not an upward gesture to reveal the header again.
        if (viewportChanged || rebase)
        {
            travel = 0;
            return;
        }
        if (verticalOffset < 0 || (offset == 0 && delta < 0))
        {
            Expand();
            return;
        }

        // Accumulate travel in one direction so tiny reversals do not toggle the layout.
        travel = IsCollapsed ? Math.Min(0, travel + delta) : Math.Max(0, travel + delta);
        if ((!IsCollapsed && travel >= 32) || (IsCollapsed && travel <= -24))
        {
            IsCollapsed = !IsCollapsed;
            travel = 0;
        }
    }

    public void Expand()
    {
        IsCollapsed = false;
        travel = 0;
    }
}
