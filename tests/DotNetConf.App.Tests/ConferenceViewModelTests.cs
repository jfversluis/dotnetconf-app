using DotNetConf.App.Services;
using DotNetConf.App.ViewModels;
using DotNetConf.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Networking;

namespace DotNetConf.App.Tests;

public sealed class ConferenceViewModelTests
{
    [Fact]
    public async Task AskBulkSavePublishesOneCompleteTimelineAndValidatesBeforeWriting()
    {
        var original = Snapshot();
        var second = original.Document.Sessions[0] with { Id = "second", Title = "Synthetic second session" };
        var snapshot = original with { Document = original.Document with { Sessions = [original.Document.Sessions[0], second] } };
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        store.SavedIds.Add("unknown");
        var viewModel = await CreateInitializedAsync(new TestRepository(snapshot), store, new TestConnectivity());
        var updates = new List<string[]>();
        viewModel.ScheduleChanged += (_, _) => updates.Add(viewModel.SavedTimelineSessions.Select(s => s.Id).ToArray());

        await Assert.ThrowsAsync<InvalidDataException>(() => viewModel.SaveAskSessionsAsync(["id", "fabricated"], default));
        Assert.Equal(0, store.Writes);
        Assert.Empty(updates);
        await viewModel.SaveAskSessionsAsync(["id", "second"], default);
        await viewModel.SaveAskSessionsAsync(["id", "second"], default);

        Assert.Equal(1, store.Writes);
        Assert.All(viewModel.Sessions, row => Assert.True(row.IsSaved));
        Assert.Equal(["id", "second"], viewModel.SavedTimelineSessions.Select(s => s.Id));
        Assert.All(updates, ids => Assert.Equal(["id", "second"], ids));
        Assert.Equal(3, viewModel.SavedCount);
        Assert.Contains("unknown", store.SavedIds);
    }

    [Fact]
    public async Task AskSaveUpdatesExistingRowsCountsAndTimelineWithoutToggling()
    {
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        var viewModel = await CreateInitializedAsync(new TestRepository(Snapshot()), store, new TestConnectivity());
        var row = Assert.Single(viewModel.Sessions);

        await viewModel.SaveAskSessionAsync("id", default);
        await viewModel.SaveAskSessionAsync("id", default);

        Assert.True(row.IsSaved);
        Assert.Same(row, Assert.Single(viewModel.SavedSessions));
        Assert.Same(row, Assert.Single(viewModel.SavedTimelineSessions));
        Assert.Equal(1, viewModel.SavedCount);
        Assert.Equal("id", Assert.Single(store.SavedIds));
        await Assert.ThrowsAsync<InvalidDataException>(() => viewModel.SaveAskSessionAsync("fabricated", default));
        Assert.Equal("id", Assert.Single(store.SavedIds));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    [InlineData(false, 2)]
    public async Task AskReportsWhenPersistenceSucceededButTheTimelineRefreshFailed(bool save, int failure)
    {
        var snapshot = Snapshot();
        var store = new TestScheduleStore();
        if (save)
            store.SavedIds.Clear();
        var viewModel = await CreateInitializedAsync(new TestRepository(snapshot), store, new TestConnectivity());
        void FailDisplay(object? _, EventArgs __) => throw failure switch
        {
            1 => new NullReferenceException("Synthetic display failure"),
            2 => new KeyNotFoundException("Synthetic display failure"),
            _ => new InvalidOperationException("Synthetic display failure")
        };
        viewModel.ScheduleChanged += FailDisplay;

        var error = save
            ? await Record.ExceptionAsync(() => viewModel.SaveAskSessionsAsync(["id"], default))
            : await Record.ExceptionAsync(() => viewModel.RemoveAskSessionsAsync(["id"], ["id"], viewModel.AskDocument!, default));

        Assert.IsType<SchedulePresentationException>(error);
        Assert.Contains(save ? "saved" : "removed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(save, store.SavedIds.Contains("id"));
        Assert.True(viewModel.HasRefreshError);
        Assert.Contains("schedule display couldn't refresh", viewModel.RefreshMessage);

        viewModel.ScheduleChanged -= FailDisplay;
        await viewModel.InitializeAsync();
        Assert.DoesNotContain("schedule display couldn't refresh", viewModel.RefreshMessage);
        Assert.Equal(save ? 1 : 0, viewModel.SavedTimelineSessions.Count);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task AskPreservesCommittedChangesWhenReminderRefreshThrows(bool save, bool timeout)
    {
        var store = new TestScheduleStore();
        if (save)
            store.SavedIds.Clear();
        var viewModel = await CreateInitializedAsync(new TestRepository(Snapshot()), store, new TestConnectivity(),
            reminders: new FailingReminders(timeout ? new TimeoutException("Synthetic reminder timeout") : null));

        var remindersUpdated = save
            ? await viewModel.SaveAskSessionsAsync(["id"], default)
            : await viewModel.RemoveAskSessionsAsync(["id"], ["id"], viewModel.AskDocument!, default);

        Assert.False(remindersUpdated);
        Assert.Equal(save, store.SavedIds.Contains("id"));
        Assert.Equal(save ? 1 : 0, viewModel.SavedTimelineSessions.Count);
    }

    [Fact]
    public async Task ReminderFailureCannotHideACommittedPresentationFailure()
    {
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        var viewModel = await CreateInitializedAsync(new TestRepository(Snapshot()), store, new TestConnectivity(),
            reminders: new FailingReminders());
        void FailDisplay(object? _, EventArgs __) => throw new InvalidOperationException("Synthetic display failure");
        viewModel.ScheduleChanged += FailDisplay;

        var error = await Assert.ThrowsAsync<SchedulePresentationException>(
            () => viewModel.SaveAskSessionsAsync(["id"], default));

        Assert.Contains("saved", error.Message);
        Assert.Contains("Reminders need attention", error.Message);
        Assert.Contains("id", store.SavedIds);
    }

    [Fact]
    public async Task ManualHeartReportsACommittedChangeWhenTimelineRefreshFails()
    {
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        var viewModel = await CreateInitializedAsync(new TestRepository(Snapshot()), store, new TestConnectivity());
        void FailDisplay(object? _, EventArgs __) => throw new KeyNotFoundException("Synthetic display failure");
        viewModel.ScheduleChanged += FailDisplay;
        var method = typeof(ConferenceViewModel).GetMethod("SetSavedWithUpdatesAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        var task = (Task<bool>)method.Invoke(viewModel, ["id", true, CancellationToken.None])!;
        var error = await Assert.ThrowsAsync<SchedulePresentationException>(() => task);

        Assert.Contains("saved", error.Message);
        Assert.Contains("id", store.SavedIds);
        Assert.Contains("schedule display couldn't refresh", viewModel.RefreshMessage);
        viewModel.ScheduleChanged -= FailDisplay;
        await viewModel.InitializeAsync();
        Assert.Equal("id", Assert.Single(viewModel.SavedTimelineSessions).Id);
    }

    [Fact]
    public async Task AskSpeakerSearchDoesNotIncludeAnotherSpeakersMatchingTitle()
    {
        var original = Snapshot();
        var other = original.Document.Sessions[0] with
        {
            Id = "other", Title = "Test speaker techniques", SpeakerIds = ["another-speaker"]
        };
        var snapshot = original with { Document = original.Document with
        {
            Sessions = [original.Document.Sessions[0], other],
            Speakers = [original.Document.Speakers[0], new() { Id = "another-speaker", Name = "Another person" }]
        } };
        var viewModel = await CreateInitializedAsync(new TestRepository(snapshot), new TestScheduleStore(), new TestConnectivity());

        viewModel.ApplyAskSearch(new(AskAction.FindSessions, "", "", "Test speaker"));
        Assert.Equal("id", Assert.Single(viewModel.Sessions).Id);
        Assert.Equal("", viewModel.SessionQuery);
        Assert.True(viewModel.HasAskSpeakerFilter);
        Assert.Contains("Speaker: Test speaker", viewModel.FilterSummary);

        viewModel.SessionQuery = "techniques";
        viewModel.SearchSessionsCommand.Execute(null);
        Assert.Empty(viewModel.Sessions);
        viewModel.ClearAskSpeakerFilterCommand.Execute(null);
        Assert.Equal("other", Assert.Single(viewModel.Sessions).Id);
        Assert.Equal("techniques", viewModel.SessionQuery);
        Assert.False(viewModel.HasAskSpeakerFilter);
        Assert.DoesNotContain("Speaker:", viewModel.FilterSummary);
    }

    [Fact]
    public async Task AskSpeakerFilterDoesNotSatisfyAnIndependentSearchTerm()
    {
        var original = Snapshot();
        var first = original.Document.Sessions[0] with
        {
            Title = "AI workshop", Topics = ["Technology"], SpeakerIds = ["ai"]
        };
        var second = first with { Id = "other", Title = "Web performance", Topics = ["Web"] };
        var snapshot = original with { Document = original.Document with
        {
            Sessions = [first, second], Speakers = [new() { Id = "ai", Name = "Ai Tanaka" }]
        } };
        var viewModel = await CreateInitializedAsync(new TestRepository(snapshot), new TestScheduleStore(), new TestConnectivity());

        viewModel.ApplyAskSearch(new(AskAction.FindSessions, "AI", "", "Ai Tanaka"));

        Assert.Equal("id", Assert.Single(viewModel.Sessions).Id);
        Assert.Contains("Ai Tanaka", viewModel.FilterSummary);
    }

    [Fact]
    public async Task AskSpeakerFilterDoesNotBroadenAFullNameOnTheSessionsPage()
    {
        var original = Snapshot();
        var snapshot = original with { Document = original.Document with
        {
            Sessions =
            [
                original.Document.Sessions[0] with { SpeakerIds = ["smith"] },
                original.Document.Sessions[0] with { Id = "other", SpeakerIds = ["smithson"] }
            ],
            Speakers = [new() { Id = "smith", Name = "Daniel Smith" }, new() { Id = "smithson", Name = "Daniel Smithson" }]
        } };
        var repository = new TestRepository(snapshot);
        var viewModel = await CreateInitializedAsync(repository, new TestScheduleStore(), new TestConnectivity());

        viewModel.ApplyAskSearch(new(AskAction.FindSessions, "", "", "Daniel Smith"));

        Assert.Equal("id", Assert.Single(viewModel.Sessions).Id);
        repository.Snapshot = snapshot with { Document = snapshot.Document with
        {
            Sessions = [snapshot.Document.Sessions[1]],
            Speakers = [snapshot.Document.Speakers[1]]
        } };
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Sessions);
    }

    [Fact]
    public async Task AskTopicSearchAndTheSessionsPageUseTheSameWordBoundaries()
    {
        var original = Snapshot();
        var snapshot = original with { Document = original.Document with
        {
            Sessions =
            [
                original.Document.Sessions[0] with { Title = "Synthetic AI session", Topics = ["AI"] },
                original.Document.Sessions[0] with { Id = "other", Title = "Maintaining apps", Topics = [".NET MAUI"] }
            ]
        } };
        var viewModel = await CreateInitializedAsync(new TestRepository(snapshot), new TestScheduleStore(), new TestConnectivity());

        viewModel.ApplyAskSearch(new(AskAction.FindSessions, "AI", "", ""));

        Assert.Equal("id", Assert.Single(viewModel.Sessions).Id);
        viewModel.SessionQuery = "Maint";
        viewModel.SearchSessionsCommand.Execute(null);
        Assert.Equal("other", Assert.Single(viewModel.Sessions).Id);
    }

    [Fact]
    public async Task AskExactTitleSearchStaysExactUntilTheUserEditsIt()
    {
        var original = Snapshot();
        const string title = "Getting started with Daniel Roth";
        var snapshot = original with { Document = original.Document with
        {
            Sessions =
            [
                original.Document.Sessions[0] with { Title = title },
                original.Document.Sessions[0] with
                {
                    Id = "other", Title = "Getting started with Blazor", SpeakerIds = ["roth"]
                }
            ],
            Speakers = [original.Document.Speakers[0], new() { Id = "roth", Name = "Daniel Roth" }]
        } };
        var viewModel = await CreateInitializedAsync(new TestRepository(snapshot), new TestScheduleStore(), new TestConnectivity());
        var intent = AskRequest.Ground(snapshot.Document, $"Find {title}",
            new(AskAction.FindSessions, title, "", ""));
        AskRequest.ValidateIntent($"Find {title}", intent);

        viewModel.ApplyAskSearch(intent);

        Assert.Equal("id", Assert.Single(viewModel.Sessions).Id);
        viewModel.SessionQuery = "Getting started";
        viewModel.SearchSessionsCommand.Execute(null);
        Assert.Equal(2, viewModel.Sessions.Count);
    }

    [Fact]
    public async Task AskRemovalRejectsChangedDocumentAndPreservesUnknownBookmarks()
    {
        var snapshot = Snapshot();
        var store = new TestScheduleStore();
        store.SavedIds.Add("unknown");
        var viewModel = await CreateInitializedAsync(new TestRepository(snapshot), store, new TestConnectivity());

        Assert.Null(await viewModel.RemoveAskSessionsAsync(["id"], ["id", "unknown"],
            snapshot.Document with { }, default));
        Assert.Equal(2, viewModel.SavedCount);
        Assert.NotNull(await viewModel.RemoveAskSessionsAsync(["id"], ["id", "unknown"],
            viewModel.AskDocument!, default));

        Assert.False(viewModel.FindSession("id")!.IsSaved);
        Assert.Empty(viewModel.SavedSessions);
        Assert.Empty(viewModel.SavedTimelineSessions);
        Assert.Equal(1, viewModel.SavedCount);
        Assert.Equal("unknown", Assert.Single(store.SavedIds));
    }

    [Fact]
    public async Task RemovingAndResavingLastFavoritePublishesFreshTimelineSnapshots()
    {
        var store = new TestScheduleStore();
        var viewModel = await CreateInitializedAsync(new TestRepository(Snapshot()), store, new TestConnectivity());
        var row = Assert.Single(viewModel.SavedTimelineSessions);
        var original = viewModel.SavedTimelineSessions;
        var updates = new List<string[]>();
        viewModel.ScheduleChanged += (_, _) => updates.Add(viewModel.SavedTimelineSessions.Select(s => s.Id).ToArray());

        Assert.NotNull(await viewModel.RemoveAskSessionsAsync(["id"], ["id"], viewModel.AskDocument!, default));
        Assert.Empty(viewModel.SavedTimelineSessions);
        Assert.Empty(viewModel.SavedSessions);
        Assert.False(row.IsSaved);
        Assert.Equal(0, viewModel.SavedCount);

        await viewModel.SaveAskSessionAsync("id", default);

        Assert.NotSame(original, viewModel.SavedTimelineSessions);
        Assert.Same(row, Assert.Single(viewModel.SavedTimelineSessions));
        Assert.Same(row, Assert.Single(viewModel.SavedSessions));
        Assert.True(row.IsSaved);
        Assert.Equal(1, viewModel.SavedCount);
        Assert.Collection(updates, empty => Assert.Empty(empty), saved => Assert.Equal(["id"], saved));
    }

    [Fact]
    public async Task AgendaRefreshDuringSaveKeepsTheWithdrawnFavoriteOnTheTimeline()
    {
        var original = Snapshot();
        var repository = new TestRepository(original) { HoldFirstRequest = true };
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        var connectivity = new TestConnectivity { NetworkAccess = NetworkAccess.None };
        var viewModel = await CreateInitializedAsync(repository, store, connectivity);
        repository.Snapshot = original with { Document = original.Document with
        {
            Sessions = [], Speakers = [], WithdrawnSessions = original.Document.Sessions,
            WithdrawnSpeakers = original.Document.Speakers
        } };
        connectivity.NetworkAccess = NetworkAccess.Internet;
        var refresh = StartRefreshWithImmediateProgress(viewModel);
        await repository.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        store.HoldWrites = true;
        var save = viewModel.SaveAskSessionAsync("id", default);
        await store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        repository.ReportProgress(repository.Snapshot);
        store.ReleaseWrite.SetResult();
        await save;
        var savedBeforeFinalRefresh = viewModel.SavedTimelineSessions.ToArray();
        var countBeforeFinalRefresh = viewModel.SavedCount;
        repository.Release.SetResult();
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, countBeforeFinalRefresh);
        var saved = Assert.Single(savedBeforeFinalRefresh);
        Assert.Equal("id", saved.Id);
        Assert.True(saved.IsWithdrawn);
    }

    [Fact]
    public async Task AgendaRefreshDuringPlannerSaveKeepsTheWithdrawnFavoriteOnTheTimeline()
    {
        var original = Snapshot();
        var repository = new TestRepository(original) { HoldFirstRequest = true };
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        var schedule = new ScheduleService(store);
        var connectivity = new TestConnectivity { NetworkAccess = NetworkAccess.None };
        var conference = await CreateInitializedAsync(repository, store, connectivity, schedule);
        var planner = await CreateReviewedPlannerAsync(original.Document, schedule, conference);
        repository.Snapshot = original with { Document = original.Document with
        {
            Sessions = [], Speakers = [], WithdrawnSessions = original.Document.Sessions,
            WithdrawnSpeakers = original.Document.Speakers
        } };
        connectivity.NetworkAccess = NetworkAccess.Internet;
        var refresh = StartRefreshWithImmediateProgress(conference);
        await repository.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        store.HoldWrites = true;
        var save = planner.ApplyCommand.ExecuteAsync(null);
        await store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        repository.ReportProgress(repository.Snapshot);
        store.ReleaseWrite.SetResult();
        await save;
        var savedBeforeFinalRefresh = conference.SavedTimelineSessions.ToArray();
        var countBeforeFinalRefresh = conference.SavedCount;
        repository.Release.SetResult();
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(planner.HasAddedSessions);
        Assert.Equal(1, countBeforeFinalRefresh);
        var saved = Assert.Single(savedBeforeFinalRefresh);
        Assert.Equal("id", saved.Id);
        Assert.True(saved.IsWithdrawn);
    }

    [Fact]
    public async Task PlannerSaveRejectsAnAgendaAlreadyPublishedAfterItsReview()
    {
        var original = Snapshot();
        var repository = new TestRepository(original);
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        var schedule = new ScheduleService(store);
        var connectivity = new TestConnectivity { NetworkAccess = NetworkAccess.None };
        var conference = await CreateInitializedAsync(repository, store, connectivity, schedule);
        var planner = await CreateReviewedPlannerAsync(original.Document, schedule, conference);
        repository.Snapshot = original with { Document = original.Document with
        {
            Sessions = [original.Document.Sessions[0] with { Title = "Synthetic changed title" }]
        } };
        connectivity.NetworkAccess = NetworkAccess.Internet;
        await conference.RefreshCommand.ExecuteAsync(null);

        await planner.ApplyCommand.ExecuteAsync(null);

        Assert.False(planner.HasAddedSessions);
        Assert.Empty(store.SavedIds);
        Assert.Equal(0, store.Writes);
        Assert.Contains("agenda was refreshed", planner.Status);
    }

    [Fact]
    public async Task PlannerReportsAPresentationFailureAfterTheSave()
    {
        var original = Snapshot();
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        var schedule = new ScheduleService(store);
        var conference = await CreateInitializedAsync(new TestRepository(original), store, new TestConnectivity(), schedule);
        var planner = await CreateReviewedPlannerAsync(original.Document, schedule, conference);
        conference.ScheduleChanged += (_, _) => throw new KeyNotFoundException("Synthetic display failure");

        await planner.ApplyCommand.ExecuteAsync(null);

        Assert.True(planner.HasAddedSessions);
        Assert.Contains("id", store.SavedIds);
        Assert.Contains("were saved", planner.Status);
        Assert.DoesNotContain("have not changed", planner.Status);
        Assert.Contains("schedule display couldn't refresh", conference.RefreshMessage);
    }

    [Fact]
    public async Task ReopeningScheduleRetriesAPresentationFailureAfterPlannerSave()
    {
        var original = Snapshot();
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        var schedule = new ScheduleService(store);
        var conference = await CreateInitializedAsync(new TestRepository(original), store, new TestConnectivity(), schedule);
        var planner = await CreateReviewedPlannerAsync(original.Document, schedule, conference);
        var row = Assert.Single(conference.Sessions);
        void FailOnce(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(row.IsSaved))
                throw new InvalidOperationException("Synthetic display failure");
        }
        row.PropertyChanged += FailOnce;

        await planner.ApplyCommand.ExecuteAsync(null);
        row.PropertyChanged -= FailOnce;
        Assert.True(planner.HasAddedSessions);
        Assert.Contains("id", store.SavedIds);
        Assert.Empty(conference.SavedTimelineSessions);
        Assert.Equal(0, conference.SavedCount);

        await conference.InitializeAsync();

        Assert.Equal("id", Assert.Single(conference.SavedTimelineSessions).Id);
        Assert.Equal(1, conference.SavedCount);
    }

    [Fact]
    public async Task SchedulePresentationRetryKeepsAnErrorVisibleUntilItSucceeds()
    {
        var original = Snapshot();
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        var schedule = new ScheduleService(store);
        var conference = await CreateInitializedAsync(new TestRepository(original), store, new TestConnectivity(), schedule);
        var planner = await CreateReviewedPlannerAsync(original.Document, schedule, conference);
        void FailDisplay(object? _, EventArgs __) => throw new InvalidOperationException("Synthetic display failure");
        conference.ScheduleChanged += FailDisplay;

        await planner.ApplyCommand.ExecuteAsync(null);
        await conference.InitializeAsync();

        Assert.True(planner.HasAddedSessions);
        Assert.Contains("id", store.SavedIds);
        Assert.True(conference.HasRefreshError);
        Assert.Contains("schedule display couldn't refresh", conference.RefreshMessage);

        conference.ScheduleChanged -= FailDisplay;
        await conference.InitializeAsync();
        Assert.DoesNotContain("schedule display couldn't refresh", conference.RefreshMessage);
        Assert.Equal("id", Assert.Single(conference.SavedTimelineSessions).Id);
    }

    [Fact]
    public async Task DeferredAgendaPublicationIsRetriedInFullAfterDisplayFailure()
    {
        var original = Snapshot();
        var repository = new TestRepository(original) { HoldFirstRequest = true };
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        var schedule = new ScheduleService(store);
        var connectivity = new TestConnectivity { NetworkAccess = NetworkAccess.None };
        var conference = await CreateInitializedAsync(repository, store, connectivity, schedule);
        var planner = await CreateReviewedPlannerAsync(original.Document, schedule, conference);
        repository.Snapshot = original with { Document = original.Document with
        {
            Sessions = [], Speakers = [], WithdrawnSessions = original.Document.Sessions,
            WithdrawnSpeakers = original.Document.Speakers
        } };
        connectivity.NetworkAccess = NetworkAccess.Internet;
        var refresh = StartRefreshWithImmediateProgress(conference);
        await repository.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        store.HoldWrites = true;
        var save = planner.ApplyCommand.ExecuteAsync(null);
        await store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        repository.ReportProgress(repository.Snapshot);
        var failed = false;
        void FailOnce(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (!failed && args.PropertyName == nameof(conference.HasAgenda))
            {
                failed = true;
                throw new InvalidOperationException("Synthetic interrupted snapshot");
            }
        }
        conference.PropertyChanged += FailOnce;
        store.ReleaseWrite.SetResult();
        await save;
        conference.PropertyChanged -= FailOnce;

        Assert.True(failed);
        Assert.True(planner.HasAddedSessions);
        Assert.Contains("id", store.SavedIds);
        Assert.Contains("schedule display couldn't refresh", conference.RefreshMessage);
        await conference.InitializeAsync();
        Assert.True(Assert.Single(conference.SavedTimelineSessions).IsWithdrawn);
        Assert.DoesNotContain("schedule display couldn't refresh", conference.RefreshMessage);

        repository.Release.SetResult();
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImmediateAgendaPublicationIsRetriedInFullAfterDisplayFailure(bool ioFailure)
    {
        var original = Snapshot();
        var repository = new TestRepository(original) { HoldFirstRequest = true };
        var store = new TestScheduleStore();
        var connectivity = new TestConnectivity { NetworkAccess = NetworkAccess.None };
        var conference = await CreateInitializedAsync(repository, store, connectivity);
        repository.Snapshot = original with { Document = original.Document with
        {
            Sessions = [], Speakers = [], WithdrawnSessions = original.Document.Sessions,
            WithdrawnSpeakers = original.Document.Speakers
        } };
        connectivity.NetworkAccess = NetworkAccess.Internet;
        var refresh = conference.RefreshCommand.ExecuteAsync(null);
        await repository.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var failed = false;
        void FailOnce(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (!failed && args.PropertyName == nameof(conference.HasAgenda))
            {
                failed = true;
                throw ioFailure
                    ? new IOException("Synthetic interrupted snapshot")
                    : new InvalidOperationException("Synthetic interrupted snapshot");
            }
        }
        conference.PropertyChanged += FailOnce;

        repository.Release.SetResult();
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        conference.PropertyChanged -= FailOnce;

        Assert.True(failed);
        Assert.True(conference.HasRefreshError);
        await conference.InitializeAsync();
        Assert.True(Assert.Single(conference.SavedTimelineSessions).IsWithdrawn);
        Assert.DoesNotContain("schedule display couldn't refresh", conference.RefreshMessage);
    }

    [Fact]
    public async Task FailedMutationPreservesItsErrorAndRetriesARejectedDeferredSnapshot()
    {
        var original = Snapshot();
        var repository = new TestRepository(original) { HoldFirstRequest = true };
        var store = new TestScheduleStore { HoldWrites = true, WriteFailure = new IOException("Synthetic store failure") };
        store.SavedIds.Clear();
        var connectivity = new TestConnectivity { NetworkAccess = NetworkAccess.None };
        var conference = await CreateInitializedAsync(repository, store, connectivity);
        repository.Snapshot = original with { Document = original.Document with
        {
            Sessions = [original.Document.Sessions[0] with { Id = "new", Title = "Synthetic updated session" }]
        } };
        connectivity.NetworkAccess = NetworkAccess.Internet;
        var refresh = StartRefreshWithImmediateProgress(conference);
        await repository.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var save = conference.SaveAskSessionsAsync(["id"], default);
        await store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        repository.ReportProgress(repository.Snapshot);
        var failed = false;
        void FailOnce(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (!failed && args.PropertyName == nameof(conference.HasAgenda))
            {
                failed = true;
                throw new KeyNotFoundException("Synthetic interrupted snapshot");
            }
        }
        conference.PropertyChanged += FailOnce;
        store.ReleaseWrite.SetResult();

        var error = await Assert.ThrowsAsync<IOException>(() => save);
        conference.PropertyChanged -= FailOnce;
        Assert.Equal("Synthetic store failure", error.Message);
        Assert.True(failed);
        Assert.Empty(store.SavedIds);
        await conference.InitializeAsync();
        Assert.Equal("new", Assert.Single(conference.Sessions).Id);

        repository.Release.SetResult();
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ADisplayErrorNotificationCannotMakeASuccessfulPlannerSaveLookUnchanged()
    {
        var original = Snapshot();
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        var schedule = new ScheduleService(store);
        var conference = await CreateInitializedAsync(new TestRepository(original), store, new TestConnectivity(), schedule);
        var planner = await CreateReviewedPlannerAsync(original.Document, schedule, conference);
        void FailDisplayAndReporting(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(conference.SavedTimelineSessions) or nameof(conference.RefreshMessage))
                throw new InvalidOperationException("Synthetic display notification failure");
        }
        conference.PropertyChanged += FailDisplayAndReporting;

        await planner.ApplyCommand.ExecuteAsync(null);
        conference.PropertyChanged -= FailDisplayAndReporting;

        Assert.Contains("id", store.SavedIds);
        Assert.True(planner.HasAddedSessions);
        Assert.Contains("were saved", planner.Status);
        Assert.DoesNotContain("have not changed", planner.Status);
        Assert.True(conference.HasRefreshError);
        Assert.Contains("schedule display couldn't refresh", conference.RefreshMessage);
        await conference.InitializeAsync();
        Assert.DoesNotContain("schedule display couldn't refresh", conference.RefreshMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedDeferredFinalPublicationDoesNotFaultTheRefreshCommand(bool keyNotFound)
    {
        var original = Snapshot();
        var repository = new TestRepository(original) { HoldFirstRequest = true };
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        var schedule = new ScheduleService(store);
        var connectivity = new TestConnectivity { NetworkAccess = NetworkAccess.None };
        var conference = await CreateInitializedAsync(repository, store, connectivity, schedule);
        var planner = await CreateReviewedPlannerAsync(original.Document, schedule, conference);
        repository.Snapshot = original with { Document = original.Document with
        {
            Sessions = [], Speakers = [], WithdrawnSessions = original.Document.Sessions,
            WithdrawnSpeakers = original.Document.Speakers
        } };
        connectivity.NetworkAccess = NetworkAccess.Internet;
        var refresh = StartRefreshWithImmediateProgress(conference);
        await repository.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        store.HoldWrites = true;
        var save = planner.ApplyCommand.ExecuteAsync(null);
        await store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        repository.Release.SetResult();
        var deferred = typeof(ConferenceViewModel).GetField("deferredPublication",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (deferred.GetValue(conference) is null && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.NotNull(deferred.GetValue(conference));
        var failed = false;
        void FailOnce(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (!failed && args.PropertyName == nameof(conference.HasAgenda))
            {
                failed = true;
                throw keyNotFound
                    ? new KeyNotFoundException("Synthetic interrupted final snapshot")
                    : new InvalidOperationException("Synthetic interrupted final snapshot");
            }
        }
        conference.PropertyChanged += FailOnce;
        store.ReleaseWrite.SetResult();
        await save;
        conference.PropertyChanged -= FailOnce;

        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(failed);
        Assert.True(planner.HasAddedSessions);
        Assert.Contains("id", store.SavedIds);
        Assert.Contains("schedule display couldn't refresh", conference.RefreshMessage);
        await conference.InitializeAsync();
        Assert.True(Assert.Single(conference.SavedTimelineSessions).IsWithdrawn);
    }

    [Fact]
    public async Task InitialPublicationFailureIsVisibleAndTheFullSnapshotCanBeRetried()
    {
        var conference = Create(new TestRepository(Snapshot()), new TestScheduleStore(), new TestConnectivity());
        var failed = false;
        void FailOnce(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (!failed && conference.AskDocument is not null && args.PropertyName == nameof(conference.HasAgenda))
            {
                failed = true;
                throw new KeyNotFoundException("Synthetic initial display failure");
            }
        }
        conference.PropertyChanged += FailOnce;
        var load = typeof(ConferenceViewModel).GetMethod("LoadAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        var error = await (Task<string?>)load.Invoke(conference, null)!;
        Assert.NotNull(error);
        Assert.True(failed);
        Assert.True(conference.HasRefreshError);
        Assert.Contains("schedule display couldn't refresh", conference.RefreshMessage);

        conference.PropertyChanged -= FailOnce;
        void FailRetry(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(conference.HasAgenda))
                throw new KeyNotFoundException("Synthetic persistent display failure");
        }
        conference.PropertyChanged += FailRetry;
        Assert.NotNull(await (Task<string?>)load.Invoke(conference, null)!);
        Assert.True(conference.HasRefreshError);
        conference.PropertyChanged -= FailRetry;

        Assert.Null(await (Task<string?>)load.Invoke(conference, null)!);
        Assert.Equal("id", Assert.Single(conference.SavedTimelineSessions).Id);
        Assert.DoesNotContain("schedule display couldn't refresh", conference.RefreshMessage);
    }

    [Fact]
    public async Task InitialPublicationRetryHandlesLocalTimeDisplayFailures()
    {
        var conference = Create(new TestRepository(Snapshot()), new TestScheduleStore(), new TestConnectivity());
        void FailDisplay(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (conference.AskDocument is not null && args.PropertyName == nameof(conference.SelectedDayIndex))
                throw new KeyNotFoundException("Synthetic local-time display failure");
        }
        conference.PropertyChanged += FailDisplay;
        var load = typeof(ConferenceViewModel).GetMethod("LoadAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        Assert.NotNull(await (Task<string?>)load.Invoke(conference, null)!);
        Assert.NotNull(await (Task<string?>)load.Invoke(conference, null)!);
        Assert.True(conference.HasRefreshError);

        conference.PropertyChanged -= FailDisplay;
        Assert.Null(await (Task<string?>)load.Invoke(conference, null)!);
        Assert.Equal("id", Assert.Single(conference.SavedTimelineSessions).Id);
        Assert.DoesNotContain("schedule display couldn't refresh", conference.RefreshMessage);
    }

    [Fact]
    public async Task OfflineRetryRepairsAnInterruptedLocalTimeRefreshBeforeClearingTheWarning()
    {
        var conference = await CreateInitializedAsync(new TestRepository(Snapshot()), new TestScheduleStore(),
            new TestConnectivity { NetworkAccess = NetworkAccess.None });
        var session = Assert.Single(conference.Sessions);
        var expectedDate = session.DateLabel;
        var expectedTime = session.TimeLabel;
        var alternateZones = new[]
        {
            TimeZoneInfo.CreateCustomTimeZone("synthetic-minus-12", TimeSpan.FromHours(-12), "test", "test"),
            TimeZoneInfo.CreateCustomTimeZone("synthetic-plus-14", TimeSpan.FromHours(14), "test", "test")
        };
        var localDay = TimeZoneInfo.ConvertTime(session.Model.StartsAt, TimeZoneInfo.Local).Date;
        var alternate = alternateZones.First(zone =>
            TimeZoneInfo.ConvertTime(session.Model.StartsAt, zone).Date != localDay);
        session.RefreshTime(alternate);
        Assert.NotEqual(expectedTime, session.TimeLabel);
        void FailDateChange(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(session.DateLabel))
                throw new KeyNotFoundException("Synthetic interrupted local-time display");
        }
        var dateNotifications = 0;
        void ObserveDate(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(session.DateLabel))
                dateNotifications++;
        }
        session.PropertyChanged += FailDateChange;
        session.PropertyChanged += ObserveDate;
        var load = typeof(ConferenceViewModel).GetMethod("LoadAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        Assert.Null(await (Task<string?>)load.Invoke(conference, null)!);
        Assert.Contains("schedule display couldn't refresh", conference.RefreshMessage);
        Assert.NotEqual(expectedTime, session.TimeLabel);
        Assert.Equal(0, dateNotifications);

        conference.RefreshSavedState();
        Assert.Contains("schedule display couldn't refresh", conference.RefreshMessage);
        await conference.RefreshCommand.ExecuteAsync(null);
        Assert.Contains("schedule display couldn't refresh", conference.RefreshMessage);
        Assert.Equal(0, dateNotifications);

        session.PropertyChanged -= FailDateChange;
        await conference.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(expectedDate, session.DateLabel);
        Assert.Equal(expectedTime, session.TimeLabel);
        Assert.True(dateNotifications > 0);
        Assert.DoesNotContain("schedule display couldn't refresh", conference.RefreshMessage);
    }

    [Fact]
    public async Task OfflineRetryKeepsTheWarningWhileLocalTimePublicationStillFails()
    {
        var conference = await CreateInitializedAsync(new TestRepository(Snapshot()), new TestScheduleStore(),
            new TestConnectivity { NetworkAccess = NetworkAccess.None });
        void FailDisplay(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(conference.SelectedDayIndex))
                throw new KeyNotFoundException("Synthetic persistent local-time display failure");
        }
        conference.PropertyChanged += FailDisplay;
        var load = typeof(ConferenceViewModel).GetMethod("LoadAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        Assert.Null(await (Task<string?>)load.Invoke(conference, null)!);
        await conference.RefreshCommand.ExecuteAsync(null);
        Assert.Contains("schedule display couldn't refresh", conference.RefreshMessage);

        conference.PropertyChanged -= FailDisplay;
        await conference.RefreshCommand.ExecuteAsync(null);
        Assert.DoesNotContain("schedule display couldn't refresh", conference.RefreshMessage);
    }

    [Fact]
    public async Task OfflineRetryRepublishesAnInterruptedTimeZoneLabel()
    {
        var conference = await CreateInitializedAsync(new TestRepository(Snapshot()), new TestScheduleStore(),
            new TestConnectivity { NetworkAccess = NetworkAccess.None });
        var expected = conference.TimeZoneLabel;
        conference.TimeZoneLabel = "Synthetic outdated timezone";
        void FailLabel(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(conference.TimeZoneLabel))
                throw new KeyNotFoundException("Synthetic interrupted timezone display");
        }
        var observed = 0;
        void ObserveLabel(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(conference.TimeZoneLabel))
                observed++;
        }
        conference.PropertyChanged += FailLabel;
        conference.PropertyChanged += ObserveLabel;
        var load = typeof(ConferenceViewModel).GetMethod("LoadAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        Assert.Null(await (Task<string?>)load.Invoke(conference, null)!);
        Assert.Equal(expected, conference.TimeZoneLabel);
        Assert.Equal(0, observed);
        await conference.RefreshCommand.ExecuteAsync(null);
        Assert.Contains("schedule display couldn't refresh", conference.RefreshMessage);

        conference.PropertyChanged -= FailLabel;
        await conference.RefreshCommand.ExecuteAsync(null);
        Assert.True(observed > 0);
        Assert.DoesNotContain("schedule display couldn't refresh", conference.RefreshMessage);
    }

    [Fact]
    public async Task OfflineRetryRepublishesInterruptedLocalDayChanges()
    {
        var conference = await CreateInitializedAsync(new TestRepository(Snapshot()), new TestScheduleStore(),
            new TestConnectivity { NetworkAccess = NetworkAccess.None });
        var localDay = Assert.Single(conference.Days, day => day.Day is not null);
        conference.Days.Remove(localDay);
        void FailDays(object? _, System.Collections.Specialized.NotifyCollectionChangedEventArgs __) =>
            throw new KeyNotFoundException("Synthetic interrupted day-filter display");
        var observed = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        void ObserveDays(object? _, System.Collections.Specialized.NotifyCollectionChangedEventArgs args) =>
            observed.Add(args.Action);
        conference.Days.CollectionChanged += FailDays;
        conference.Days.CollectionChanged += ObserveDays;
        var load = typeof(ConferenceViewModel).GetMethod("LoadAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        Assert.Null(await (Task<string?>)load.Invoke(conference, null)!);
        Assert.Contains("schedule display couldn't refresh", conference.RefreshMessage);
        Assert.Empty(observed);
        await conference.RefreshCommand.ExecuteAsync(null);
        Assert.Contains("schedule display couldn't refresh", conference.RefreshMessage);

        conference.Days.CollectionChanged -= FailDays;
        await conference.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(localDay, Assert.Single(conference.Days, day => day.Day is not null));
        Assert.Contains(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, observed);
        Assert.DoesNotContain("schedule display couldn't refresh", conference.RefreshMessage);
    }

    [Fact]
    public async Task OfflineRetryRepublishesInterruptedSessionResults()
    {
        var original = Snapshot();
        var first = original.Document.Sessions[0] with { Title = "Synthetic first session" };
        var second = first with { Id = "second", Title = "Synthetic second session" };
        var snapshot = original with { Document = original.Document with { Sessions = [first, second] } };
        var conference = await CreateInitializedAsync(new TestRepository(snapshot), new TestScheduleStore(),
            new TestConnectivity { NetworkAccess = NetworkAccess.None });
        conference.SessionQuery = "first";
        conference.SearchSessionsCommand.Execute(null);
        Assert.Equal("id", Assert.Single(conference.Sessions).Id);
        conference.SessionQuery = "second";
        var results = conference.Sessions;
        void FailResults(object? _, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
        {
            if (args.Action is System.Collections.Specialized.NotifyCollectionChangedAction.Add
                or System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                throw new KeyNotFoundException("Synthetic interrupted session-results display");
        }
        var observed = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        void ObserveResults(object? _, System.Collections.Specialized.NotifyCollectionChangedEventArgs args) =>
            observed.Add(args.Action);
        var labelNotifications = 0;
        void ObserveLabel(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(conference.ShowFilteredSessionsLabel))
                labelNotifications++;
        }
        results.CollectionChanged += FailResults;
        results.CollectionChanged += ObserveResults;
        conference.PropertyChanged += ObserveLabel;
        var load = typeof(ConferenceViewModel).GetMethod("LoadAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        Assert.Null(await (Task<string?>)load.Invoke(conference, null)!);
        Assert.Contains("schedule display couldn't refresh", conference.RefreshMessage);
        Assert.Equal("second", Assert.Single(results).Id);
        Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Remove], observed);
        Assert.Equal(0, labelNotifications);
        await conference.RefreshCommand.ExecuteAsync(null);
        Assert.Contains("schedule display couldn't refresh", conference.RefreshMessage);

        results.CollectionChanged -= FailResults;
        await conference.RefreshCommand.ExecuteAsync(null);
        Assert.Same(results, conference.Sessions);
        Assert.Contains(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, observed);
        Assert.True(labelNotifications > 0);
        Assert.DoesNotContain("schedule display couldn't refresh", conference.RefreshMessage);
    }

    [Fact]
    public async Task FailedProgressPublicationIsVisibleAndTheFinalSnapshotCanRecover()
    {
        var original = Snapshot();
        var repository = new TestRepository(original) { HoldFirstRequest = true };
        var connectivity = new TestConnectivity { NetworkAccess = NetworkAccess.None };
        var conference = await CreateInitializedAsync(repository, new TestScheduleStore(), connectivity);
        repository.Snapshot = original with { Document = original.Document with
        {
            Sessions = [original.Document.Sessions[0] with { Title = "Synthetic updated session" }]
        } };
        connectivity.NetworkAccess = NetworkAccess.Internet;
        var refresh = StartRefreshWithImmediateProgress(conference);
        await repository.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var failed = false;
        void FailOnce(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (!failed && args.PropertyName == nameof(conference.HasAgenda))
            {
                failed = true;
                throw new KeyNotFoundException("Synthetic progress display failure");
            }
        }
        conference.PropertyChanged += FailOnce;

        repository.ReportProgress(repository.Snapshot);
        Assert.True(failed);
        Assert.True(conference.HasRefreshError);
        repository.Release.SetResult();
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));

        conference.PropertyChanged -= FailOnce;
        Assert.Equal("Synthetic updated session", Assert.Single(conference.Sessions).Title);
        Assert.False(conference.HasRefreshError);
    }

    [Fact]
    public async Task AgendaPublicationCannotInvalidateAnInFlightConfirmedRemoval()
    {
        var original = Snapshot();
        var repository = new TestRepository(original) { HoldFirstRequest = true };
        var store = new TestScheduleStore();
        var connectivity = new TestConnectivity { NetworkAccess = NetworkAccess.None };
        var viewModel = await CreateInitializedAsync(repository, store, connectivity);
        var reviewed = viewModel.AskDocument!;
        var updated = original with { Document = original.Document with
        {
            Sessions = [original.Document.Sessions[0] with { Title = "Synthetic changed title" }]
        } };
        repository.Snapshot = updated;
        connectivity.NetworkAccess = NetworkAccess.Internet;
        var refresh = StartRefreshWithImmediateProgress(viewModel);
        await repository.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        store.HoldWrites = true;
        var removal = viewModel.RemoveAskSessionsAsync(["id"], ["id"], reviewed, default);
        await store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        repository.ReportProgress(original with { Document = original.Document with
        {
            Sessions = [original.Document.Sessions[0] with { Title = "Synthetic intermediate title" }]
        } });
        repository.ReportProgress(updated);
        var publishedBeforeWriteCompleted = !ReferenceEquals(reviewed, viewModel.AskDocument);
        store.ReleaseWrite.SetResult();
        var outcome = await removal;
        repository.Release.SetResult();
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));

        if (publishedBeforeWriteCompleted)
        {
            Assert.Null(outcome);
            Assert.Contains("id", store.SavedIds);
        }
        else
        {
            Assert.NotNull(outcome);
            Assert.DoesNotContain("id", store.SavedIds);
        }
        Assert.Same(updated.Document, viewModel.AskDocument);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledScheduleWriteStillPublishesTheDeferredAgenda(bool removing)
    {
        var original = Snapshot();
        var repository = new TestRepository(original) { HoldFirstRequest = true };
        var store = new TestScheduleStore();
        if (!removing)
            store.SavedIds.Clear();
        var connectivity = new TestConnectivity { NetworkAccess = NetworkAccess.None };
        var viewModel = await CreateInitializedAsync(repository, store, connectivity);
        var updated = original with { Document = original.Document with
        {
            Sessions = [original.Document.Sessions[0] with { Title = "Synthetic refreshed title" }]
        } };
        repository.Snapshot = updated;
        connectivity.NetworkAccess = NetworkAccess.Internet;
        var refresh = StartRefreshWithImmediateProgress(viewModel);
        await repository.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        store.HoldWrites = true;
        var mutation = removing
            ? viewModel.RemoveAskSessionsAsync(["id"], ["id"], viewModel.AskDocument!, cancellation.Token)
            : (Task)viewModel.SaveAskSessionAsync("id", cancellation.Token);
        await store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        repository.ReportProgress(updated);

        cancellation.Cancel();
        store.ReleaseWrite.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mutation);
        Assert.Same(updated.Document, viewModel.AskDocument);
        Assert.Equal(removing ? 1 : 0, viewModel.SavedCount);
        repository.Release.SetResult();
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UnknownSavedIdsStayLocalWithoutInventingAgendaDetails()
    {
        var snapshot = Snapshot();
        var store = new TestScheduleStore();
        store.SavedIds.Add("unavailable-id");
        var viewModel = await CreateInitializedAsync(new TestRepository(snapshot), store, new TestConnectivity());

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
        var viewModel = await CreateInitializedAsync(new TestRepository(snapshot), store, new TestConnectivity());

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
        var viewModel = await CreateInitializedAsync(repository, store, new TestConnectivity());
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
        var viewModel = await CreateInitializedAsync(repository, new TestScheduleStore(), new TestConnectivity());

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
        var viewModel = await CreateInitializedAsync(repository, store, connectivity);
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
        var viewModel = await CreateInitializedAsync(new TestRepository(snapshot), store, new TestConnectivity
        {
            NetworkAccess = NetworkAccess.None
        }, schedule);

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
        var viewModel = await CreateInitializedAsync(new TestRepository(snapshot), new TestScheduleStore(), new TestConnectivity
        {
            NetworkAccess = NetworkAccess.None
        });

        var session = Assert.Single(viewModel.SavedSessions);
        Assert.True(session.IsWithdrawn);
        Assert.Same(session, Assert.Single(viewModel.SavedTimelineSessions));
        Assert.Contains("No longer in the published agenda", session.NavigationDescription);
        Assert.Contains("No longer in the published agenda", session.SaveHint);
        Assert.Equal("Archived speaker", Assert.Single(session.Speakers).Name);
        Assert.Equal("future", Assert.Single(viewModel.Speakers).Id);
    }

    private static ConferenceViewModel Create(TestRepository repository, TestScheduleStore store, TestConnectivity connectivity,
        ScheduleService? schedule = null, ISessionReminderUpdates? reminders = null) =>
        new(repository, schedule ?? new ScheduleService(store), new DetailNavigationService(), null!, connectivity,
            new SpeakerImageCache(new HttpClient(), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
                NullLogger<SpeakerImageCache>.Instance),
            NullLogger<ConferenceViewModel>.Instance, reminders ?? new TestReminders());

    private static async Task<ConferenceViewModel> CreateInitializedAsync(
        TestRepository repository, TestScheduleStore store, TestConnectivity connectivity,
        ScheduleService? schedule = null, ISessionReminderUpdates? reminders = null)
    {
        var viewModel = Create(repository, store, connectivity, schedule, reminders);
        await viewModel.InitializeAsync();
        await (viewModel.RefreshCommand.ExecutionTask ?? Task.CompletedTask);
        return viewModel;
    }

    private static async Task<PlannerViewModel> CreateReviewedPlannerAsync(
        ConferenceDocument document, ScheduleService schedule, ConferenceViewModel conference)
    {
        var planner = new PlannerViewModel(new FixedDataSource(document), schedule, new TestPlanner(),
            conference, NullLogger<PlannerViewModel>.Instance, new TestModelSelection());
        await planner.InitializeAsync();
        planner.Request = "Test";
        await planner.GenerateCommand.ExecuteAsync(null);
        planner.ConstraintsConfirmed = true;
        return planner;
    }

    private static Task StartRefreshWithImmediateProgress(ConferenceViewModel viewModel)
    {
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
            return viewModel.RefreshCommand.ExecuteAsync(null);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

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
        public bool HoldWrites { get; set; }
        public Exception? WriteFailure { get; set; }
        public int Writes { get; private set; }
        public HashSet<string> SavedIds { get; } = ["id"];
        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyCollection<string>> ReadAsync(CancellationToken cancellationToken = default) =>
            FailReads
                ? Task.FromException<IReadOnlyCollection<string>>(new IOException("Transient read failure"))
                : Task.FromResult<IReadOnlyCollection<string>>(SavedIds.ToArray());
        public async Task WriteAsync(IReadOnlyCollection<string> sessionIds, CancellationToken cancellationToken = default)
        {
            Writes++;
            if (HoldWrites)
            {
                WriteStarted.TrySetResult();
                await ReleaseWrite.Task;
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (WriteFailure is not null)
                throw WriteFailure;
            SavedIds.Clear();
            SavedIds.UnionWith(sessionIds);
        }
    }

    private sealed class FailingReminders(Exception? failure = null) : ISessionReminderUpdates
    {
        public Task<bool> RefreshAsync() =>
            Task.FromException<bool>(failure ?? new IOException("Synthetic reminder failure"));
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

    private sealed class FixedDataSource(ConferenceDocument document) : IConferenceDataSource
    {
        public Task<ConferenceDocument> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(document);
    }

    private sealed class TestPlanner : IOnDevicePlanner
    {
        public string? UnsupportedReason => null;
        public Task<PlannerModelState> CheckReadinessAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PlannerModelState(true, false, ""));
        public IAsyncEnumerable<PlannerModelDownload> DownloadModelAsync(string? modelId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlannerStartingPoint>> SuggestStartingPointsAsync(
            ConferenceDocument conference, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlannerStartingPoint>>([]);
        public Task<PlannerResult> ProposeAsync(ConferenceDocument conference, string request,
            TimeZoneInfo zone, string[] savedIds, CancellationToken cancellationToken, bool fillGaps = false,
            PlannerWatchingHours? watchingHours = null) =>
            Task.FromResult(new PlannerResult(new(["id"], [], []), ""));
    }

    private sealed class TestModelSelection : IPlannerModelSelection
    {
        public bool IsSupported => false;
        public string? SelectedModelId => null;
        public string? Warning => null;
        public event EventHandler? Changed { add { } remove { } }
        public void Select(string? modelId) => throw new NotSupportedException();
    }

    private sealed class TestRepository(ConferenceSnapshot snapshot) : IConferenceRepository
    {
        private IProgress<ConferenceSnapshot>? progress;
        public ConferenceSnapshot Snapshot { get; set; } = snapshot;
        public bool HoldFirstRequest { get; init; }
        public int RefreshCalls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReportProgress(ConferenceSnapshot value) => progress!.Report(value);
        public Task<ConferenceSnapshot> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public async Task<ConferenceSnapshot> RefreshAsync(CancellationToken cancellationToken = default,
            IProgress<ConferenceSnapshot>? progress = null)
        {
            this.progress = progress;
            RefreshCalls++;
            Started.TrySetResult();
            if (HoldFirstRequest && RefreshCalls == 1)
                await Release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return Snapshot;
        }
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }
}
