using DotNetConf.App.Services;
using DotNetConf.App.ViewModels;
using DotNetConf.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Networking;

namespace DotNetConf.App.Tests;

public sealed class ConferenceViewModelTests
{
    [Fact]
    public async Task UnknownSavedIdsStayLocalWithoutInventingAgendaDetails()
    {
        var snapshot = Snapshot();
        var store = new TestScheduleStore();
        store.SavedIds.Add("unavailable-id");
        var viewModel = Create(new TestRepository(snapshot), store, new TestConnectivity());
        await viewModel.InitializeAsync();
        await (viewModel.RefreshCommand.ExecutionTask ?? Task.CompletedTask);

        var session = Assert.Single(viewModel.SavedSessions);
        Assert.Equal("id", session.Id);
        Assert.Same(session, viewModel.FindSession("id"));
        Assert.Null(viewModel.FindSession("unavailable-id"));
        Assert.Equal(2, viewModel.SavedCount);
        Assert.Equal(2, store.SavedIds.Count);
        Assert.False(session.HasConflict);
        Assert.Same(session, Assert.Single(viewModel.SavedTimelineSessions));
    }

    [Fact]
    public async Task DayFiltersFollowDownloadedDatesBeyondFourDays()
    {
        var original = Snapshot();
        var session = original.Document.Sessions[0];
        var snapshot = original with { Document = original.Document with
        {
            Sessions = Enumerable.Range(0, 7).Select(day => session with
            {
                Id = $"day-{day}", StartsAt = session.StartsAt.AddDays(day), EndsAt = session.EndsAt.AddDays(day)
            }).ToList()
        } };
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        store.SavedIds.UnionWith(["day-0", "day-6"]);
        var viewModel = Create(new TestRepository(snapshot), store, new TestConnectivity());
        await viewModel.InitializeAsync();
        await (viewModel.RefreshCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Equal(8, viewModel.Days.Count);
        Assert.Equal(7, viewModel.Days.Where(day => day.Day is not null).Select(day => day.Day).Distinct().Count());
        Assert.Equal(7, viewModel.Sessions.Count);
        Assert.Equal(["day-0", "day-6"], viewModel.SavedTimelineSessions.Select(saved => saved.Id));
        Assert.Equal([DateOnly.FromDateTime(session.StartsAt.DateTime), DateOnly.FromDateTime(session.StartsAt.AddDays(6).DateTime)],
            Timeline.Build(viewModel.SavedTimelineSessions.Select(saved => saved.Model), TimeZoneInfo.Utc).Select(day => day.Day));
    }

    [Fact]
    public async Task SavedTimelineFollowsPublishedDateChangesWithoutChangingFavoriteIds()
    {
        var original = Snapshot();
        var repository = new TestRepository(original);
        var store = new TestScheduleStore();
        var viewModel = Create(repository, store, new TestConnectivity());
        await viewModel.InitializeAsync();
        await (viewModel.RefreshCommand.ExecutionTask ?? Task.CompletedTask);
        var previous = viewModel.SavedTimelineSessions;
        var favorite = Assert.Single(previous);
        var updated = original.Document.Sessions[0] with
        {
            Title = "Synthetic updated title",
            StartsAt = original.Document.Sessions[0].StartsAt.AddDays(90),
            EndsAt = original.Document.Sessions[0].EndsAt.AddDays(90)
        };
        repository.Snapshot = original with { Document = original.Document with { Sessions = [updated] } };

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.NotSame(previous, viewModel.SavedTimelineSessions);
        Assert.Same(favorite, Assert.Single(viewModel.SavedTimelineSessions));
        Assert.Same(favorite, Assert.Single(viewModel.SavedSessions));
        Assert.Equal(updated, favorite.Model);
        Assert.Equal("id", Assert.Single(store.SavedIds));
        var day = Assert.Single(Timeline.Build(viewModel.SavedTimelineSessions.Select(saved => saved.Model), TimeZoneInfo.Utc));
        Assert.Equal(DateOnly.FromDateTime(updated.StartsAt.DateTime), day.Day);
        Assert.Equal(updated.StartsAt, Assert.Single(day.Items).Start);
    }

    [Theory]
    [InlineData("validation")]
    [InlineData("json")]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("mixed")]
    [InlineData("cache")]
    [InlineData("metadata")]
    public async Task RefreshErrorsDistinguishInvalidSourceDataFromConnectionAndStorageFailures(string failure)
    {
        var issue = failure switch
        {
            "json" => new ConferenceDataIssue(ConferenceRepository.MicrosoftSource, new System.Text.Json.JsonException("Synthetic malformed JSON")),
            "network" => new ConferenceDataIssue(ConferenceRepository.MicrosoftSource, new HttpRequestException("Synthetic network failure")),
            "timeout" => new ConferenceDataIssue(ConferenceRepository.MicrosoftSource, new TimeoutException("Synthetic timeout")),
            "cache" => new ConferenceDataIssue(ConferenceRepository.CacheSource, new InvalidDataException("Synthetic invalid cache")),
            "metadata" => new ConferenceDataIssue(ConferenceRepository.MetadataSource, new InvalidDataException("Synthetic invalid metadata")),
            _ => new ConferenceDataIssue(ConferenceRepository.MicrosoftSource, new InvalidDataException("Synthetic invalid agenda"))
        };
        var snapshot = Snapshot() with { Issues = failure == "mixed"
            ? [issue, new(ConferenceRepository.CacheSource, new IOException("Synthetic cache failure"))]
            : [issue] };
        var repository = new TestRepository(snapshot);
        var viewModel = Create(repository, new TestScheduleStore(), new TestConnectivity());
        await viewModel.InitializeAsync();
        await (viewModel.RefreshCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.True(viewModel.HasRefreshError);
        Assert.True(viewModel.HasAgenda);
        Assert.Contains(failure switch
        {
            "cache" or "mixed" => "Check storage",
            "metadata" => "conference date metadata; the cached agenda is unchanged",
            "network" or "timeout" => "Couldn't download Microsoft",
            _ => "Microsoft agenda update because its data was invalid"
        },
            viewModel.RefreshMessage);
        if (failure is "validation" or "json" or "cache" or "mixed" or "metadata")
            Assert.DoesNotContain("connection", viewModel.RefreshMessage);

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
        Assert.Same(session, Assert.Single(viewModel.SavedTimelineSessions));
        Assert.Contains("Saved.", session.SaveHint);
        Assert.Contains("id", store.SavedIds);
        Assert.Equal(access == NetworkAccess.None ? 0 : 1, repository.RefreshCalls);
    }

    [Fact]
    public async Task TimelineContainsOnlyFavoritesAndRebuildsItsDaysWhenFavoritesChange()
    {
        var original = Snapshot();
        var later = original.Document.Sessions[0] with
        {
            Id = "later",
            Title = "Synthetic later session",
            StartsAt = original.Document.Sessions[0].StartsAt.AddDays(1),
            EndsAt = original.Document.Sessions[0].EndsAt.AddDays(1)
        };
        var snapshot = original with { Document = original.Document with
        {
            Sessions = [.. original.Document.Sessions, later]
        } };
        var store = new TestScheduleStore();
        var schedule = new ScheduleService(store);
        var viewModel = Create(new TestRepository(snapshot), store, new TestConnectivity
        {
            NetworkAccess = NetworkAccess.None
        }, schedule);
        await viewModel.InitializeAsync();
        await (viewModel.RefreshCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Equal(2, viewModel.Sessions.Count);
        var firstSnapshot = viewModel.SavedTimelineSessions;
        Assert.Equal("id", Assert.Single(firstSnapshot).Id);
        Assert.Same(viewModel.SavedSessions[0], firstSnapshot[0]);
        var firstDay = Assert.Single(Timeline.Build(firstSnapshot.Select(session => session.Model), TimeZoneInfo.Utc));
        Assert.Equal(new DateOnly(2026, 11, 10), firstDay.Day);

        await schedule.ToggleAsync("later");
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, viewModel.SavedTimelineSessions.Count);
        Assert.Single(firstSnapshot);
        Assert.Equal(2, Timeline.Build(viewModel.SavedTimelineSessions.Select(session => session.Model), TimeZoneInfo.Utc).Count);

        await schedule.ToggleAsync("id");
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("later", Assert.Single(viewModel.SavedTimelineSessions).Id);
        var laterDay = Assert.Single(Timeline.Build(viewModel.SavedTimelineSessions.Select(session => session.Model), TimeZoneInfo.Utc));
        Assert.Equal(new DateOnly(2026, 11, 11), laterDay.Day);
        Assert.Equal(later.StartsAt, Assert.Single(laterDay.Items).Start);

        await schedule.ToggleAsync("later");
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Empty(viewModel.SavedTimelineSessions);
        Assert.Empty(viewModel.SavedSessions);
        Assert.Equal("Make it your conference", viewModel.ScheduleEmptyTitle);
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
        Assert.Same(session, Assert.Single(viewModel.SavedTimelineSessions));
        Assert.Contains("No longer in the published agenda", session.NavigationDescription);
        Assert.Contains("No longer in the published agenda", session.SaveHint);
        Assert.Equal("Archived speaker", Assert.Single(session.Speakers).Name);
        Assert.Equal("future", Assert.Single(viewModel.Speakers).Id);
    }

    private static ConferenceViewModel Create(TestRepository repository, TestScheduleStore store, TestConnectivity connectivity,
        ScheduleService? schedule = null) =>
        new(repository, schedule ?? new ScheduleService(store), new DetailNavigationService(), null!, connectivity,
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
