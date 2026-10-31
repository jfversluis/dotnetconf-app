using System.Text.Json;
using DotNetConf.Core;

namespace DotNetConf.Core.Tests;

public sealed class ScheduleTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"dotnetconf-tests-{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(directory, "schedule.json");

    [Fact]
    public async Task SavesAndRemovesAcrossRestarts()
    {
        var service = new ScheduleService(new JsonScheduleStore(FilePath));
        await service.InitializeAsync();
        Assert.False(service.IsSaved("session-one"));
        Assert.Equal(0, service.SavedCount);
        Assert.True(await service.ToggleAsync("session-one"));
        Assert.Equal(1, service.SavedCount);
        var restarted = new ScheduleService(new JsonScheduleStore(FilePath));
        await restarted.InitializeAsync();
        Assert.True(restarted.IsSaved("session-one"));
        Assert.Equal(1, restarted.SavedCount);
        Assert.False(await restarted.ToggleAsync("session-one"));
        Assert.Equal(0, restarted.SavedCount);
        Assert.Empty(await new JsonScheduleStore(FilePath).ReadAsync());
    }

    [Fact]
    public async Task ConcurrentUpdatesKeepBothSessions()
    {
        var service = new ScheduleService(new JsonScheduleStore(FilePath));
        await Task.WhenAll(service.ToggleAsync("one"), service.ToggleAsync("two"));
        Assert.Equal(["one", "two"], (await new JsonScheduleStore(FilePath).ReadAsync()).Order().ToArray());
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task UnknownAgendaIdsArePreserved()
    {
        var store = new JsonScheduleStore(FilePath);
        await store.WriteAsync(["removed-from-agenda"]);
        var service = new ScheduleService(store);
        await service.InitializeAsync();
        Assert.Equal(1, service.SavedCount);
        await service.ToggleAsync("new-session");
        Assert.Equal(2, service.SavedCount);
        Assert.Contains("removed-from-agenda", await store.ReadAsync());
    }

    [Fact]
    public async Task WriteFailureDoesNotChangeMemoryState()
    {
        var service = new ScheduleService(new FailingStore());
        await service.InitializeAsync();
        await Assert.ThrowsAsync<IOException>(() => service.ToggleAsync("one"));
        Assert.False(service.IsSaved("one"));
        Assert.Equal(0, service.SavedCount);
    }

    [Fact]
    public async Task CorruptScheduleIsNotOverwritten()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(FilePath, "corrupt");
        var service = new ScheduleService(new JsonScheduleStore(FilePath));
        await Assert.ThrowsAsync<JsonException>(() => service.ToggleAsync("one"));
        Assert.Equal("corrupt", await File.ReadAllTextAsync(FilePath));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[\"\"]")]
    [InlineData("[null]")]
    public async Task InvalidIdsAreRejected(string json)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(FilePath, json);
        await Assert.ThrowsAsync<InvalidDataException>(() => new JsonScheduleStore(FilePath).ReadAsync());
    }

    [Fact]
    public async Task KeyedStoreSavesAndRemovesAcrossRestarts()
    {
        var storage = new HashSet<string>(StringComparer.Ordinal);
        var service = new ScheduleService(SharedStore(storage));
        Assert.True(await service.ToggleAsync("two"));
        Assert.True(await service.ToggleAsync("one"));
        Assert.Equal(["one", "two"], storage.Order());
        var restarted = new ScheduleService(SharedStore(storage));
        await restarted.InitializeAsync();
        Assert.True(restarted.IsSaved("one"));
        Assert.False(await restarted.ToggleAsync("one"));
        Assert.Equal(["two"], storage);
    }

    [Fact]
    public async Task ToggleKeepsSessionsSavedInAnotherTab()
    {
        var storage = new HashSet<string>(StringComparer.Ordinal);
        var first = new ScheduleService(SharedStore(storage));
        var second = new ScheduleService(SharedStore(storage));
        await first.InitializeAsync();
        await second.InitializeAsync();
        Assert.True(await first.ToggleAsync("one"));
        Assert.True(await second.ToggleAsync("two"));
        Assert.True(second.IsSaved("one"));
        Assert.Equal(["one", "two"], storage.Order());
    }

    [Fact]
    public async Task ToggleRemovesASessionSavedInAnotherTab()
    {
        var storage = new HashSet<string>(StringComparer.Ordinal);
        var first = new ScheduleService(SharedStore(storage));
        var second = new ScheduleService(SharedStore(storage));
        await first.InitializeAsync();
        await second.InitializeAsync();
        Assert.True(await first.ToggleAsync("one"));
        // The second tab loaded before the first saved, but toggles what's stored now.
        Assert.False(await second.ToggleAsync("one"));
        Assert.False(second.IsSaved("one"));
        Assert.Empty(storage);
    }

    [Fact]
    public async Task TabsSavingDifferentSessionsAtOnceKeepBoth()
    {
        // Each tab reads its own copy of storage, which has not heard about the other tab's save yet.
        var storage = new HashSet<string>(StringComparer.Ordinal);
        var first = new ScheduleService(TabStore(storage, new HashSet<string>(StringComparer.Ordinal)));
        var second = new ScheduleService(TabStore(storage, new HashSet<string>(StringComparer.Ordinal)));
        await first.InitializeAsync();
        await second.InitializeAsync();
        Assert.True(await first.ToggleAsync("one"));
        Assert.True(await second.ToggleAsync("two"));
        Assert.Equal(["one", "two"], storage.Order());
    }

    [Fact]
    public async Task ARemovalIsNotUndoneByAnotherTabSavingAtOnce()
    {
        var storage = new HashSet<string>(["one"], StringComparer.Ordinal);
        var first = new ScheduleService(TabStore(storage, new HashSet<string>(storage, StringComparer.Ordinal)));
        var second = new ScheduleService(TabStore(storage, new HashSet<string>(storage, StringComparer.Ordinal)));
        await first.InitializeAsync();
        await second.InitializeAsync();
        Assert.False(await first.ToggleAsync("one"));
        Assert.True(await second.ToggleAsync("two"));
        Assert.Equal(["two"], storage);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task InvalidStoredIdsAreRejectedAndKept(string id)
    {
        var storage = new HashSet<string>([id, "one"], StringComparer.Ordinal);
        var service = new ScheduleService(SharedStore(storage));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ToggleAsync("two"));
        Assert.Equal([id, "one"], storage.Order());
    }

    [Fact]
    public async Task SetSavedIsIdempotentAcrossStaleTabs()
    {
        var storage = new HashSet<string>(StringComparer.Ordinal);
        var first = new ScheduleService(SharedStore(storage));
        var second = new ScheduleService(SharedStore(storage));
        await first.InitializeAsync();
        await second.InitializeAsync();
        Assert.True(await first.SetSavedAsync("20261100", true));
        Assert.True(await second.SetSavedAsync("20261100", true));
        Assert.Equal(["20261100"], storage);
        Assert.True(second.IsSaved("20261100"));
        Assert.False(await second.SetSavedAsync("20261100", false));
        Assert.False(await first.SetSavedAsync("20261100", false));
        Assert.Empty(storage);
        Assert.False(second.IsSaved("20261100"));
    }

    [Fact]
    public async Task SetSavedFailureLeavesMemoryUnchanged()
    {
        var store = new ToggleFailingStore(["unknown"]);
        var service = new ScheduleService(store);
        await service.InitializeAsync();
        await Assert.ThrowsAsync<IOException>(() => service.SetSavedAsync("20261100", true));
        Assert.False(service.IsSaved("20261100"));
        Assert.Equal(["unknown"], store.Ids);
    }

    [Fact]
    public async Task FailedRemovalChangesNothingAndCanBeRetried()
    {
        var store = new ToggleFailingStore(["20261100", "unknown"]);
        var service = new ScheduleService(store);
        await service.InitializeAsync();
        await Assert.ThrowsAsync<IOException>(() => service.ToggleAsync("20261100"));
        Assert.True(service.IsSaved("20261100"));
        Assert.Equal(2, service.SavedCount);
        Assert.Equal(["20261100", "unknown"], store.Ids.Order());
        store.Fail = false;
        Assert.False(await service.ToggleAsync("20261100"));
        Assert.Equal(["unknown"], store.Ids);
    }

    private sealed class ToggleFailingStore(IEnumerable<string> ids) : IScheduleStore
    {
        public HashSet<string> Ids { get; } = new(ids, StringComparer.Ordinal);
        public bool Fail { get; set; } = true;
        public Task<IReadOnlyCollection<string>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<string>>(Ids.ToArray());
        public Task WriteAsync(IReadOnlyCollection<string> sessionIds, CancellationToken cancellationToken = default)
        {
            if (Fail)
                throw new IOException("Simulated storage failure.");
            Ids.Clear();
            Ids.UnionWith(sessionIds);
            return Task.CompletedTask;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    /// <summary>
    /// Storage every tab can see at once.
    /// </summary>
    private static KeyedScheduleStore SharedStore(HashSet<string> storage) =>
        new(() => storage, id => storage.Add(id), id => storage.Remove(id));

    /// <summary>
    /// A browser tab that reads its own copy of storage, which learns only about this tab's changes.
    /// </summary>
    private static KeyedScheduleStore TabStore(HashSet<string> storage, HashSet<string> copy) => new(
        () => copy,
        id => { copy.Add(id); storage.Add(id); },
        id => { copy.Remove(id); storage.Remove(id); });

    private sealed class FailingStore : IScheduleStore
    {
        public Task<IReadOnlyCollection<string>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);
        public Task WriteAsync(IReadOnlyCollection<string> sessionIds, CancellationToken cancellationToken = default) =>
            throw new IOException("Simulated storage failure.");
    }
}
