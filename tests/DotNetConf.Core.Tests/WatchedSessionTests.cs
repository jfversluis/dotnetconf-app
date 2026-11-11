using System.Text.Json;
using DotNetConf.Core;

namespace DotNetConf.Core.Tests;

public sealed class WatchedSessionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"dotnetconf-watched-{Guid.NewGuid():N}");
    private string WatchedPath => Path.Combine(directory, "watched.json");
    private string SchedulePath => Path.Combine(directory, "schedule.json");

    [Fact]
    public async Task WatchedStatusIsSeparateFromScheduleAndSurvivesRestart()
    {
        var schedule = new ScheduleService(new JsonScheduleStore(SchedulePath));
        var watched = new WatchedSessionService(new JsonScheduleStore(WatchedPath));
        await schedule.SetSavedAsync("saved", true);
        await watched.SetWatchedAsync("video", true);
        await watched.SetWatchedAsync("saved", true);
        await watched.SetWatchedAsync("video", false);

        var restarted = new WatchedSessionService(new JsonScheduleStore(WatchedPath));
        await restarted.ReloadAsync();
        Assert.True(restarted.IsWatched("saved"));
        Assert.False(restarted.IsWatched("video"));
        Assert.Equal(["saved"], await new JsonScheduleStore(SchedulePath).ReadAsync());
        Assert.Equal(["saved"], await new JsonScheduleStore(WatchedPath).ReadAsync());
    }

    [Fact]
    public async Task CorruptWatchedFileIsPreservedAndDoesNotAffectTheSchedule()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(WatchedPath, "corrupt");
        var schedule = new ScheduleService(new JsonScheduleStore(SchedulePath));
        await schedule.SetSavedAsync("saved", true);
        var watched = new WatchedSessionService(new JsonScheduleStore(WatchedPath));

        await Assert.ThrowsAsync<JsonException>(() => watched.ReloadAsync());
        await Assert.ThrowsAsync<JsonException>(() => watched.SetWatchedAsync("video", true));
        Assert.Equal("corrupt", await File.ReadAllTextAsync(WatchedPath));
        Assert.True(schedule.IsSaved("saved"));
    }

    [Fact]
    public async Task ConcurrentUpdatesRetainBothWatchedSessions()
    {
        var watched = new WatchedSessionService(new JsonScheduleStore(WatchedPath));
        await Task.WhenAll(watched.SetWatchedAsync("one", true), watched.SetWatchedAsync("two", true));
        var restarted = new WatchedSessionService(new JsonScheduleStore(WatchedPath));
        await restarted.ReloadAsync();
        Assert.True(restarted.IsWatched("one"));
        Assert.True(restarted.IsWatched("two"));
    }

    [Fact]
    public async Task KeyedStoresKeepBrowserWatchedAndScheduleEntriesSeparate()
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        IScheduleStore Store(string prefix) => new KeyedScheduleStore(
            () => values.Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(key => key[prefix.Length..]).ToArray(),
            id => values.Add(prefix + id),
            id => values.Remove(prefix + id));
        var schedule = new ScheduleService(Store("dotnetconf.saved."));
        var watched = new WatchedSessionService(Store("dotnetconf.watched."));
        await schedule.SetSavedAsync("saved", true);
        await watched.SetWatchedAsync("video", true);
        await watched.SetWatchedAsync("saved", true);
        await watched.SetWatchedAsync("saved", false);

        var restarted = new WatchedSessionService(Store("dotnetconf.watched."));
        await restarted.ReloadAsync();
        Assert.True(restarted.IsWatched("video"));
        Assert.False(restarted.IsWatched("saved"));
        Assert.True(schedule.IsSaved("saved"));
        Assert.Contains("dotnetconf.saved.saved", values);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
