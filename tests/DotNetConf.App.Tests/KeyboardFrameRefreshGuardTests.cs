using DotNetConf.App.Services;

namespace DotNetConf.App.Tests;

public sealed class KeyboardFrameRefreshGuardTests
{
    [Fact]
    public void EmptyFrameSchedulesARefresh()
    {
        var guard = new KeyboardFrameRefreshGuard();
        var pending = Assert.IsType<long>(guard.WillShow(true));

        Assert.True(guard.IsCurrent(pending));
    }

    [Fact]
    public void VisibleKeyboardCancelsAnEmptyFrameRefresh()
    {
        var guard = new KeyboardFrameRefreshGuard();
        var pending = Assert.IsType<long>(guard.WillShow(true));

        Assert.Null(guard.WillShow(false));
        Assert.False(guard.IsCurrent(pending));
    }

    [Fact]
    public void HideOrUnloadCancelsAnEmptyFrameRefresh()
    {
        var guard = new KeyboardFrameRefreshGuard();
        var pending = Assert.IsType<long>(guard.WillShow(true));

        guard.Cancel();
        Assert.False(guard.IsCurrent(pending));
    }

    [Fact]
    public void OnlyTheLatestEmptyFrameRefreshRemainsCurrent()
    {
        var guard = new KeyboardFrameRefreshGuard();
        var first = Assert.IsType<long>(guard.WillShow(true));
        var second = Assert.IsType<long>(guard.WillShow(true));

        Assert.False(guard.IsCurrent(first));
        Assert.True(guard.IsCurrent(second));
    }
}
