namespace DotNetConf.App.Services;

internal sealed class KeyboardFrameRefreshGuard
{
    private long revision;

    public long? WillShow(bool isEmpty)
    {
        var current = ++revision;
        return isEmpty ? current : null;
    }

    public void Cancel() => revision++;

    public bool IsCurrent(long pendingRevision) => pendingRevision == revision;
}
