using DotNetConf.App.Services;
using DotNetConf.App.ViewModels;
using DotNetConf.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Networking;

namespace DotNetConf.App.Tests;

public sealed class ConferenceViewModelTests
{
    [Theory]
    [InlineData("validation")]
    [InlineData("json")]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("mixed")]
    [InlineData("cache")]
    public async Task RefreshErrorsDistinguishInvalidSourceDataFromConnectionAndStorageFailures(string failure)
    {
        var issue = failure switch
        {
            "json" => new ConferenceDataIssue(ConferenceRepository.MicrosoftSource, new System.Text.Json.JsonException("Synthetic malformed JSON")),
            "network" => new ConferenceDataIssue(ConferenceRepository.MicrosoftSource, new HttpRequestException("Synthetic network failure")),
            "timeout" => new ConferenceDataIssue(ConferenceRepository.MicrosoftSource, new TimeoutException("Synthetic timeout")),
            "cache" => new ConferenceDataIssue(ConferenceRepository.CacheSource, new InvalidDataException("Synthetic invalid cache")),
            _ => new ConferenceDataIssue(ConferenceRepository.MicrosoftSource, new InvalidDataException("Synthetic invalid agenda"))
        };
        var snapshot = Snapshot() with { Issues = failure == "mixed"
            ? [issue, new(ConferenceRepository.CommunitySource, new HttpRequestException("Synthetic network failure"))]
            : [issue] };
        var repository = new TestRepository(snapshot);
        var viewModel = Create(repository, new TestScheduleStore(), new TestConnectivity());
        await viewModel.InitializeAsync();
        await (viewModel.RefreshCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.True(viewModel.HasRefreshError);
        Assert.True(viewModel.HasAgenda);
        Assert.Contains(failure == "cache" ? "Check storage" :
            failure is "network" or "timeout" ? "Couldn't download Microsoft" : "Microsoft agenda update because its data was invalid",
            viewModel.RefreshMessage);
        if (failure is "validation" or "json" or "cache")
            Assert.DoesNotContain("connection", viewModel.RefreshMessage);
        if (failure == "mixed")
            Assert.Contains("Couldn't download community", viewModel.RefreshMessage);

        repository.Snapshot = snapshot with { Issues = [] };
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasRefreshError);
        Assert.Empty(viewModel.RefreshMessage);
    }

    [Theory]
    [InlineData(NetworkAccess.None, false)]
    [InlineData(NetworkAccess.Internet, false)]
    [InlineData(NetworkAccess.None, true)]
    [InlineData(NetworkAccess.Internet, true)]
    public async Task ScheduleRetryReconcilesExistingSessionAndSavedList(NetworkAccess access, bool withdrawn)
    {
        var store = new TestScheduleStore { FailReads = true };
        var connectivity = new TestConnectivity { NetworkAccess = NetworkAccess.None };
        var snapshot = Snapshot();
        if (withdrawn)
            snapshot = snapshot with { Document = snapshot.Document with
            {
                Sessions = [], Speakers = [],
                WithdrawnSessions = snapshot.Document.Sessions,
                WithdrawnSpeakers = snapshot.Document.Speakers
            } };
        var repository = new TestRepository(snapshot);
        var viewModel = Create(repository, store, connectivity);
        await viewModel.InitializeAsync();
        await (viewModel.RefreshCommand.ExecutionTask ?? Task.CompletedTask);
        Assert.False(viewModel.IsScheduleAvailable);
        var originalSession = withdrawn ? null : Assert.Single(viewModel.Sessions);
        Assert.False(originalSession?.IsSaved ?? false);

        store.FailReads = false;
        connectivity.NetworkAccess = access;
        await viewModel.RefreshCommand.ExecuteAsync(null);

        var session = Assert.Single(viewModel.SavedSessions);
        Assert.True(viewModel.IsScheduleAvailable);
        Assert.True(session.IsSaved);
        if (originalSession is not null)
            Assert.Same(originalSession, session);
        Assert.Equal(withdrawn, session.IsWithdrawn);
        Assert.Equal("old", Assert.Single(session.Speakers).Id);
        Assert.Equal(1, viewModel.SavedCount);
        Assert.Contains("Saved.", session.SaveHint);
        Assert.Contains("id", store.SavedIds);
        Assert.Equal(access == NetworkAccess.None ? 0 : 1, repository.RefreshCalls);
    }

    [Fact]
    public async Task ResumeDuringCancellationStartsAReplacementRefresh()
    {
        var repository = new TestRepository(Snapshot()) { HoldFirstRequest = true };
        var viewModel = Create(repository, new TestScheduleStore(), new TestConnectivity());
        await viewModel.InitializeAsync();
        await repository.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.PauseRefresh();
        await viewModel.ResumeAsync();
        repository.Release.SetResult();
        await viewModel.RefreshCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, repository.RefreshCalls);
        Assert.False(viewModel.IsRefreshing);
        Assert.True(viewModel.HasAgenda);
    }

    [Fact]
    public async Task StoppingAgainBeforeCancellationFinishesDoesNotRefreshInBackground()
    {
        var repository = new TestRepository(Snapshot()) { HoldFirstRequest = true };
        var viewModel = Create(repository, new TestScheduleStore(), new TestConnectivity());
        await viewModel.InitializeAsync();
        await repository.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.PauseRefresh();
        await viewModel.ResumeAsync();
        viewModel.PauseRefresh();
        repository.Release.SetResult();
        await viewModel.RefreshCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, repository.RefreshCalls);

        await viewModel.ResumeAsync();
        Assert.Equal(2, repository.RefreshCalls);
    }

    [Fact]
    public async Task WithdrawnSessionIsAnnouncedAndItsSpeakerIsNotInCurrentDirectory()
    {
        var document = Snapshot().Document with
        {
            Sessions = [],
            Speakers = [new() { Id = "future", Name = "Published speaker without a timed talk" }],
            WithdrawnSessions = Snapshot().Document.Sessions,
            WithdrawnSpeakers = [new() { Id = "old", Name = "Archived speaker" }]
        };
        var snapshot = new ConferenceSnapshot(document, null, null, true, []);
        var viewModel = Create(new TestRepository(snapshot), new TestScheduleStore(), new TestConnectivity
        {
            NetworkAccess = NetworkAccess.None
        });
        await viewModel.InitializeAsync();
        await (viewModel.RefreshCommand.ExecutionTask ?? Task.CompletedTask);

        var session = Assert.Single(viewModel.SavedSessions);
        Assert.True(session.IsWithdrawn);
        Assert.Contains("No longer in the published agenda", session.NavigationDescription);
        Assert.Contains("No longer in the published agenda", session.SaveHint);
        Assert.Equal("Archived speaker", Assert.Single(session.Speakers).Name);
        Assert.Equal("future", Assert.Single(viewModel.Speakers).Id);
    }

    private static ConferenceViewModel Create(TestRepository repository, TestScheduleStore store, TestConnectivity connectivity) =>
        new(repository, new ScheduleService(store), new DetailNavigationService(), null!, connectivity,
            new SpeakerImageCache(new HttpClient(), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
                NullLogger<SpeakerImageCache>.Instance),
            NullLogger<ConferenceViewModel>.Instance);

    private static ConferenceSnapshot Snapshot() => new(new ConferenceDocument
    {
        SchemaVersion = 1,
        Name = "Test conference",
        Description = "Synthetic test data",
        WebsiteUrl = "https://example.com",
        Sessions = [new()
        {
            Id = "id", Title = "Test session", SpeakerIds = ["old"],
            StartsAt = new(2026, 11, 10, 10, 0, 0, TimeSpan.Zero),
            EndsAt = new(2026, 11, 10, 11, 0, 0, TimeSpan.Zero)
        }],
        Speakers = [new() { Id = "old", Name = "Test speaker" }]
    }, null, null, true, []);

    private sealed class TestScheduleStore : IScheduleStore
    {
        public bool FailReads { get; set; }
        public HashSet<string> SavedIds { get; } = ["id"];
        public Task<IReadOnlyCollection<string>> ReadAsync(CancellationToken cancellationToken = default) =>
            FailReads
                ? Task.FromException<IReadOnlyCollection<string>>(new IOException("Transient read failure"))
                : Task.FromResult<IReadOnlyCollection<string>>(SavedIds.ToArray());
        public Task WriteAsync(IReadOnlyCollection<string> sessionIds, CancellationToken cancellationToken = default)
        {
            SavedIds.Clear();
            SavedIds.UnionWith(sessionIds);
            return Task.CompletedTask;
        }
    }

    private sealed class TestConnectivity : IConnectivity
    {
        public NetworkAccess NetworkAccess { get; set; } = NetworkAccess.Internet;
        public IEnumerable<ConnectionProfile> ConnectionProfiles => [];
        public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class TestRepository(ConferenceSnapshot snapshot) : IConferenceRepository
    {
        public ConferenceSnapshot Snapshot { get; set; } = snapshot;
        public bool HoldFirstRequest { get; init; }
        public int RefreshCalls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ConferenceSnapshot> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public async Task<ConferenceSnapshot> RefreshAsync(CancellationToken cancellationToken = default,
            IProgress<ConferenceSnapshot>? progress = null)
        {
            RefreshCalls++;
            Started.TrySetResult();
            if (HoldFirstRequest && RefreshCalls == 1)
                await Release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return Snapshot;
        }
    }
}
