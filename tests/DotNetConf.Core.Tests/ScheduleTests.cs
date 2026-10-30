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

    private static readonly Dictionary<string, string> Aliases = new() { ["1338204"] = "20261100", ["1353047"] = "20261101" };

    [Fact]
    public async Task AliasedSavesAreLogicallyEquivalentAndInitializeNeverRewrites()
    {
        var storage = new HashSet<string>(["1338204", "legacy-gone"], StringComparer.Ordinal);
        var service = new ScheduleService(SharedStore(storage));
        service.SetAliases(Aliases);
        await service.InitializeAsync();
        Assert.True(service.IsSaved("20261100"));
        Assert.True(service.IsSaved("1338204"));
        Assert.False(service.IsSaved("20261101"));
        Assert.Equal(2, service.SavedCount);
        Assert.Equal(["1338204", "legacy-gone"], storage.Order());
    }

    [Fact]
    public async Task AliasesMayArriveAfterLoadAndAreReplacedAtomically()
    {
        var storage = new HashSet<string>(["1338204"], StringComparer.Ordinal);
        var service = new ScheduleService(SharedStore(storage));
        await service.InitializeAsync();
        Assert.False(service.IsSaved("20261100"));
        service.SetAliases(Aliases);
        Assert.True(service.IsSaved("20261100"));
        Assert.Throws<ArgumentException>(() => service.SetAliases(new Dictionary<string, string> { ["a"] = "b", ["b"] = "c" }));
        Assert.True(service.IsSaved("20261100"));
        service.SetAliases(new Dictionary<string, string>());
        Assert.False(service.IsSaved("20261100"));
        Assert.Equal(1, service.SavedCount);
    }

    [Fact]
    public async Task BothIdsOfOneSessionCountOnceAndToggleRemovesTheWholeGroup()
    {
        var storage = new HashSet<string>(["20261100", "1338204", "other"], StringComparer.Ordinal);
        var service = new ScheduleService(SharedStore(storage));
        service.SetAliases(Aliases);
        await service.InitializeAsync();
        Assert.Equal(2, service.SavedCount);
        Assert.False(await service.ToggleAsync("1338204"));
        Assert.Equal(["other"], storage);
        Assert.False(service.IsSaved("20261100"));
        Assert.Equal(1, service.SavedCount);
    }

    [Theory]
    [InlineData("20261100")]
    [InlineData("1338204")]
    public async Task TogglingEitherIdSavesTheCanonicalIdAndKeepsUnknownIds(string toggled)
    {
        var storage = new HashSet<string>(["unknown"], StringComparer.Ordinal);
        var service = new ScheduleService(SharedStore(storage));
        service.SetAliases(Aliases);
        Assert.True(await service.ToggleAsync(toggled));
        Assert.Equal(["20261100", "unknown"], storage.Order());
        Assert.True(service.IsSaved("1338204"));
        Assert.Equal(2, service.SavedCount);
    }

    [Fact]
    public async Task AliasedSaveSurvivesRestartWithJsonStore()
    {
        var first = new ScheduleService(new JsonScheduleStore(FilePath));
        first.SetAliases(Aliases);
        Assert.True(await first.ToggleAsync("1353047"));
        var restarted = new ScheduleService(new JsonScheduleStore(FilePath));
        restarted.SetAliases(Aliases);
        await restarted.InitializeAsync();
        Assert.True(restarted.IsSaved("1353047"));
        Assert.True(restarted.IsSaved("20261101"));
        Assert.Equal(["20261101"], await new JsonScheduleStore(FilePath).ReadAsync());
    }

    [Fact]
    public async Task ToggleUsesLatestStoreAcrossBrowserTabsWithAliases()
    {
        var storage = new HashSet<string>(StringComparer.Ordinal);
        var first = new ScheduleService(SharedStore(storage));
        var second = new ScheduleService(SharedStore(storage));
        first.SetAliases(Aliases);
        second.SetAliases(Aliases);
        await first.InitializeAsync();
        await second.InitializeAsync();
        Assert.True(await first.ToggleAsync("20261100"));
        // The second tab loaded before the save and must remove the whole group it finds stored now.
        storage.Add("1338204");
        Assert.False(await second.ToggleAsync("1338204"));
        Assert.Empty(storage);
        Assert.False(second.IsSaved("20261100"));
    }

    [Fact]
    public async Task SetSavedIsIdempotentAcrossStaleTabs()
    {
        var storage = new HashSet<string>(StringComparer.Ordinal);
        var first = new ScheduleService(SharedStore(storage));
        var second = new ScheduleService(SharedStore(storage));
        first.SetAliases(Aliases);
        second.SetAliases(Aliases);
        await first.InitializeAsync();
        await second.InitializeAsync();
        Assert.True(await first.SetSavedAsync("20261100", true));
        Assert.True(await second.SetSavedAsync("20261100", true));
        Assert.Equal(["20261100"], storage);
        Assert.True(second.IsSaved("1338204"));
        storage.Add("1338204");
        Assert.False(await second.SetSavedAsync("1338204", false));
        Assert.Empty(storage);
        Assert.False(second.IsSaved("20261100"));
    }

    [Fact]
    public async Task SetSavedFailureLeavesMemoryUnchanged()
    {
        var store = new ToggleFailingStore(["unknown"]);
        var service = new ScheduleService(store);
        service.SetAliases(Aliases);
        await service.InitializeAsync();
        await Assert.ThrowsAsync<IOException>(() => service.SetSavedAsync("20261100", true));
        Assert.False(service.IsSaved("20261100"));
        Assert.Equal(["unknown"], store.Ids);
    }

    [Fact]
    public async Task FailedAliasedWriteChangesNothing()
    {
        var store = new ToggleFailingStore(["1338204", "unknown"]);
        var service = new ScheduleService(store);
        service.SetAliases(Aliases);
        await service.InitializeAsync();
        await Assert.ThrowsAsync<IOException>(() => service.ToggleAsync("20261100"));
        Assert.True(service.IsSaved("20261100"));
        Assert.Equal(2, service.SavedCount);
        Assert.Equal(["1338204", "unknown"], store.Ids.Order());
        store.Fail = false;
        Assert.False(await service.ToggleAsync("20261100"));
        Assert.Equal(["unknown"], store.Ids);
    }

    [Theory]
    [InlineData("", "a")]
    [InlineData("a", " ")]
    [InlineData("a", "a")]
    public void InvalidAliasesAreRejected(string alias, string canonical) =>
        Assert.Throws<ArgumentException>(() => new ScheduleService(new FailingStore())
            .SetAliases(new Dictionary<string, string> { [alias] = canonical }));

    [Fact]
    public void AliasChainsAndCyclesAreRejected()
    {
        var service = new ScheduleService(new FailingStore());
        Assert.Throws<ArgumentException>(() => service.SetAliases(new Dictionary<string, string> { ["a"] = "b", ["b"] = "c" }));
        Assert.Throws<ArgumentException>(() => service.SetAliases(new Dictionary<string, string> { ["a"] = "b", ["b"] = "a" }));
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
