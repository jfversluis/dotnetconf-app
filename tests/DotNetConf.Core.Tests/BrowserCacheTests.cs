using DotNetConf.App;
using System.Text;

namespace DotNetConf.Core.Tests;

public sealed class BrowserCacheTests
{
    private readonly Dictionary<string, string> values = [];
    private BrowserCache Cache() => new(values.GetValueOrDefault,
        (key, value) => values[key] = value);

    [Fact]
    public async Task AgendaPersistsAcrossInstancesWithoutOverwritingBookmarksOrPreferences()
    {
        values["dotnetconf.saved.session-1"] = "1";
        values["preferences"] = "existing";
        var data = Encoding.UTF8.GetBytes("""{"sources":[]}""");
        Assert.Null(await Cache().ReadAsync());
        await Cache().WriteAsync(data);
        Assert.Equal(data, await Cache().ReadAsync());
        Assert.Equal("1", values["dotnetconf.saved.session-1"]);
        Assert.Equal("existing", values["preferences"]);
    }

    [Fact]
    public async Task OversizedOrDeniedWritesFailWithoutReplacingLastGoodAgenda()
    {
        await Cache().WriteAsync([1, 2, 3]);
        await Assert.ThrowsAsync<IOException>(() => Cache().WriteAsync(new byte[BrowserCache.MaximumAgendaBytes + 1]));
        var denied = new BrowserCache(values.GetValueOrDefault, (_, _) => throw new IOException("storage denied"));
        await Assert.ThrowsAsync<IOException>(() => denied.WriteAsync([4]));
        Assert.Equal(new byte[] { 1, 2, 3 }, await Cache().ReadAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptOrOversizedSavedAgendaIsReported(bool oversized)
    {
        values[BrowserCache.AgendaKey] = oversized ? new string('A', BrowserCache.MaximumAgendaBytes * 2) : "invalid!";
        await Assert.ThrowsAsync<IOException>(() => Cache().ReadAsync());
    }

    [Fact]
    public async Task CancelledAgendaOperationsDoNotReadOrWrite()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Cache().WriteAsync([1], cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Cache().ReadAsync(cancelled.Token));
        Assert.Empty(values);
    }

}
