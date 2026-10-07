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
        Assert.True(await service.ToggleAsync("session-one"));
        var restarted = new ScheduleService(new JsonScheduleStore(FilePath));
        await restarted.InitializeAsync();
        Assert.True(restarted.IsSaved("session-one"));
        Assert.False(await restarted.ToggleAsync("session-one"));
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
        await service.ToggleAsync("new-session");
        Assert.Contains("removed-from-agenda", await store.ReadAsync());
    }

    [Fact]
    public async Task WriteFailureDoesNotChangeMemoryState()
    {
        var service = new ScheduleService(new FailingStore());
        await service.InitializeAsync();
        await Assert.ThrowsAsync<IOException>(() => service.ToggleAsync("one"));
        Assert.False(service.IsSaved("one"));
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

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private sealed class FailingStore : IScheduleStore
    {
        public Task<IReadOnlyCollection<string>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);
        public Task WriteAsync(IReadOnlyCollection<string> sessionIds, CancellationToken cancellationToken = default) =>
            throw new IOException("Simulated storage failure.");
    }
}
