using DotNetConf.App.Services;
using DotNetConf.App.ViewModels;
using DotNetConf.Core;
using IconFont.Maui.FluentIcons;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Networking;

namespace DotNetConf.App.Tests;

public sealed class ConferenceViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkedSessionLookupWaitsForTheSharedDownloadWithoutStartingAnotherRefresh(bool hasStaleCache)
    {
        var published = Snapshot();
        var initial = published with
        {
            Document = published.Document with
            {
                Sessions = hasStaleCache ? [published.Document.Sessions[0] with { Id = "synthetic-cached" }] : []
            }
        };
        var repository = new TestRepository(initial) { HoldFirstRequest = true };
        var viewModel = Create(repository, new(), new());
        await viewModel.InitializeAsync();
        await repository.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var lookup = viewModel.ResolveLinkedSessionAsync("id");
        var completedBeforeDownload = lookup.IsCompleted;
        repository.Snapshot = published;
        repository.Release.TrySetResult();
        var result = await lookup.WaitAsync(TimeSpan.FromSeconds(5));
        await viewModel.RefreshCommand.ExecutionTask!;
        Assert.False(completedBeforeDownload);
        Assert.Same(viewModel.FindSession("id"), result);
        Assert.NotNull(result);
        Assert.Equal(1, repository.RefreshCalls);
    }

    [Fact]
    public async Task LinkedSessionLookupUsesAValidCachedSessionWithoutWaitingForNetwork()
    {
        var repository = new TestRepository(Snapshot()) { HoldFirstRequest = true };
        var viewModel = Create(repository, new(), new());
        await viewModel.InitializeAsync();
        await repository.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var lookup = viewModel.ResolveLinkedSessionAsync("id");
        var completedFromCache = lookup.IsCompleted;
        repository.Release.TrySetResult();
        var result = await lookup;
        await viewModel.RefreshCommand.ExecutionTask!;
        Assert.True(completedFromCache);
        Assert.NotNull(result);
        Assert.Equal(1, repository.RefreshCalls);
    }

    [Fact]
    public async Task LinkedSessionLookupWaitsForAResumeTriggeredDownload()
    {
        var published = Snapshot();
        var stale = published with { Document = published.Document with { Sessions = [] } };
        var repository = new TestRepository(stale) { HoldRefreshCall = 2 };
        var viewModel = await CreateInitializedAsync(repository, new(), new());
        viewModel.PauseRefresh();
        var resume = viewModel.ResumeAsync();
        Assert.True(viewModel.IsRefreshing);
        var lookup = viewModel.ResolveLinkedSessionAsync("id");
        var completedBeforeDownload = lookup.IsCompleted;
        repository.Snapshot = published;
        repository.Release.TrySetResult();
        await resume.WaitAsync(TimeSpan.FromSeconds(5));
        var result = await lookup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(completedBeforeDownload);
        Assert.NotNull(result);
        Assert.Equal(2, repository.RefreshCalls);
    }

    [Fact]
    public async Task QuickActionsReuseTheVisibleCommandsAndCurrentState()
    {
        var viewModel = await CreateInitializedAsync(new TestRepository(Snapshot()), new TestScheduleStore(), new TestConnectivity());
        var session = Assert.Single(viewModel.Sessions);
        var actions = session.GetQuickActions();
        Assert.Equal("Watched", session.WatchedSwipeLabel);
        var originalSaveSwipeInstruction = session.IsSaved ? "Swipe to remove" : "Swipe to save";
        Assert.Equal(originalSaveSwipeInstruction, session.SaveSwipeInstruction);
        Assert.Equal(session.IsSaved ? "Release to remove" : "Release to save", session.SaveSwipeReleaseInstruction);
        List<string?> changedProperties = [];
        session.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        Assert.Contains(actions, action => action.Label == session.SaveLabel && action.Command == session.ToggleSaveCommand);
        Assert.Contains(actions, action => action.Label == session.WatchedActionLabel && action.Command == session.ToggleWatchedCommand);
        Assert.Contains(actions, action => action.Label == session.ShareLabel && action.Command == session.ShareCommand);
        session.IsSaved = !session.IsSaved;
        Assert.NotEqual(originalSaveSwipeInstruction, session.SaveSwipeInstruction);
        Assert.Equal(session.IsSaved ? "Swipe to remove" : "Swipe to save", session.SaveSwipeInstruction);
        Assert.Equal(session.IsSaved ? "Release to remove" : "Release to save", session.SaveSwipeReleaseInstruction);
        Assert.Contains(nameof(SessionViewModel.SaveSwipeInstruction), changedProperties);
        Assert.Contains(nameof(SessionViewModel.SaveSwipeReleaseInstruction), changedProperties);
        session.IsWatched = !session.IsWatched;
        Assert.Equal("Unwatch", session.WatchedSwipeLabel);
        Assert.DoesNotContain(session.GetQuickActions(), action => action.Label == actions[0].Label);
        Assert.Contains(session.GetQuickActions(), action => action.Label == session.WatchedActionLabel);
    }

    [Fact]
    public async Task QuickActionsOmitUnavailableWatchedAndShareActions()
    {
        var viewModel = await CreateInitializedAsync(new TestRepository(Snapshot()), new TestScheduleStore(), new TestConnectivity());
        var session = Assert.Single(viewModel.Sessions);
        viewModel.IsWatchedAvailable = false;
        Assert.DoesNotContain(session.GetQuickActions(), action => action.Command == session.ToggleWatchedCommand);
        session.IsWithdrawn = true;
        Assert.DoesNotContain(session.GetQuickActions(), action => action.Command == session.ShareCommand);
        viewModel.IsScheduleAvailable = false;
        Assert.Empty(session.GetQuickActions());
    }

    [Fact]
    public async Task SessionSharingLeadsWithThePublicWebsiteAndLabelsTheOptionalAppLink()
    {
        var viewModel = await CreateInitializedAsync(new TestRepository(Snapshot()), new TestScheduleStore(), new TestConnectivity());
        var session = Assert.Single(viewModel.Sessions);
        var website = ConferenceLinks.SessionUrl(session.Id);
        var companion = SessionAppLinks.Create(session.Id);
        Assert.Contains($"\n\n{website}\n", session.ShareText);
        Assert.Contains($"Open in the companion (requires the app): {companion}", session.ShareText);
        Assert.True(session.ShareText.IndexOf(website, StringComparison.Ordinal) <
            session.ShareText.IndexOf(companion, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("synthetic/id")]
    [InlineData("synthetic%id")]
    [InlineData("synthetic?id")]
    public async Task SessionSharingPreservesWebsiteLinksForFeedIdsOutsideTheAppRouteContract(string id)
    {
        var original = Snapshot();
        var snapshot = original with
        {
            Document = original.Document with { Sessions = [original.Document.Sessions[0] with { Id = id }] }
        };
        var viewModel = await CreateInitializedAsync(new TestRepository(snapshot), new TestScheduleStore(), new TestConnectivity());
        var session = Assert.Single(viewModel.Sessions);
        Assert.Contains(ConferenceLinks.SessionUrl(id), session.ShareText);
        Assert.DoesNotContain($"{SessionAppLinks.Scheme}://", session.ShareText);
        Assert.DoesNotContain("Open in the companion", session.ShareText);
        await session.ShareCommand.ExecuteAsync(null);
    }

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
        Assert.Contains("agenda changed", planner.Status);
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
        var compactObserved = 0;
        void ObserveLabel(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(conference.TimeZoneLabel))
                observed++;
            if (args.PropertyName == nameof(conference.CompactTimeZoneLabel))
                compactObserved++;
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
        Assert.True(compactObserved > 0);
        Assert.Equal($"Local: {TimeZoneInfo.Local.Id}", conference.CompactTimeZoneLabel);
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

    [Fact]
    public async Task RecordingRefreshUpdatesOpenDetailsDiscoveryAndFilteredListWithoutAnAppRestart()
    {
        var original = Snapshot();
        var repository = new TestRepository(original);
        var viewModel = await CreateInitializedAsync(repository, new(), new());
        var openDetail = Assert.Single(viewModel.Sessions);
        Assert.False(openDetail.HasRecording);
        viewModel.RecordingsOnly = true;
        Assert.Empty(viewModel.Sessions);
        Assert.Equal("No session videos linked yet", viewModel.SessionsEmptyTitle);
        Assert.Contains("doesn't have any session video links", viewModel.SessionsEmptyMessage);

        repository.Snapshot = original with { Document = original.Document with
        {
            Sessions = [original.Document.Sessions[0] with
            {
                RecordingUrl = "https://youtu.be/synthetic01",
                ResourceLinks = [new("Synthetic resource", "https://example.com/resource")]
            }]
        }};
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Same(openDetail, Assert.Single(viewModel.Sessions));
        Assert.True(openDetail.HasRecording);
        Assert.False(openDetail.HasViewingGuidance);
        Assert.True(openDetail.WatchRecordingCommand.CanExecute(null));
        Assert.True(openDetail.HasResources);
        Assert.Contains("Video available", openDetail.NavigationDescription);
        Assert.Contains("Video available", viewModel.FilterSummary);
        Assert.Equal("1", viewModel.FilterBadgeText);

        repository.Snapshot = original;
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.False(openDetail.HasRecording);
        Assert.True(openDetail.HasViewingGuidance);
        Assert.False(openDetail.WatchRecordingCommand.CanExecute(null));
        Assert.Empty(openDetail.Resources);
        Assert.Empty(viewModel.Sessions);
        viewModel.ClearSessionFiltersCommand.Execute(null);
        Assert.Same(openDetail, Assert.Single(viewModel.Sessions));
    }

    [Fact]
    public async Task EmptyResultsCanClearEverySearchConstraintWithoutChangingSavedSessions()
    {
        var original = Snapshot();
        var document = original.Document with
        {
            Sessions = [original.Document.Sessions[0] with { Topics = ["Synthetic topic"] }]
        };
        var viewModel = await CreateInitializedAsync(new TestRepository(original with { Document = document }),
            new TestScheduleStore(), new TestConnectivity());
        viewModel.ApplyAskSearch(new(AskAction.FindSessions, "missing", "", "Test speaker"));
        viewModel.SelectedDay = Assert.Single(viewModel.Days, day => day.Day is not null);
        Assert.Single(viewModel.Topics).IsSelected = true;
        viewModel.RecordingsOnly = true;
        viewModel.HideWatched = true;

        Assert.Empty(viewModel.Sessions);
        Assert.True(viewModel.HasActiveSessionCriteria);
        viewModel.ClearAllSessionCriteriaCommand.Execute(null);

        Assert.False(viewModel.HasActiveSessionCriteria);
        Assert.False(viewModel.HasAskSpeakerFilter);
        Assert.False(viewModel.HasSessionFilters);
        Assert.Equal("", viewModel.SessionQuery);
        Assert.Null(viewModel.SelectedDay!.Day);
        Assert.Equal("id", Assert.Single(viewModel.Sessions).Id);
        Assert.True(Assert.Single(viewModel.Sessions).IsSaved);
    }

    [Fact]
    public async Task HomeShowsCurrentOrNextSavedSessionButNotWithdrawnOrEndedTalks()
    {
        var original = Snapshot();
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var past = original.Document.Sessions[0] with
        {
            Id = "past", StartsAt = now.AddHours(-2), EndsAt = now.AddHours(-1)
        };
        var live = past with { Id = "live", StartsAt = now.AddMinutes(-10), EndsAt = now.AddMinutes(20) };
        var future = past with { Id = "future", StartsAt = now.AddHours(1), EndsAt = now.AddHours(2) };
        var withdrawn = past with { Id = "withdrawn", StartsAt = now, EndsAt = now.AddHours(3) };
        var document = original.Document with { Sessions = [future, past, live], WithdrawnSessions = [withdrawn] };
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        store.SavedIds.UnionWith(["past", "live", "future", "withdrawn"]);
        var viewModel = await CreateInitializedAsync(new TestRepository(original with { Document = document }),
            store, new TestConnectivity());

        viewModel.RefreshSessionStatuses(now);
        Assert.Equal("live", viewModel.NextSavedSession?.Id);
        Assert.Equal("Happening now", viewModel.NextSavedStatus);
        Assert.True(viewModel.NextSavedSession!.IsLive);
        Assert.False(viewModel.SavedSessions.Single(session => session.Id == "withdrawn").IsLive);
        viewModel.RefreshSessionStatuses(live.EndsAt);
        Assert.Equal("future", viewModel.NextSavedSession?.Id);
        Assert.Equal("Up next", viewModel.NextSavedStatus);
        Assert.False(viewModel.SavedSessions.Single(session => session.Id == "live").IsLive);
        viewModel.RefreshSessionStatuses(future.EndsAt);
        Assert.Null(viewModel.NextSavedSession);
        Assert.False(viewModel.HasNextSavedSession);
    }

    [Fact]
    public async Task LiveStatusUpdatesUnsavedSessionsAndPublishedTimeChanges()
    {
        var original = Snapshot();
        var start = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var model = original.Document.Sessions[0] with { StartsAt = start, EndsAt = start.AddMinutes(30) };
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        var viewModel = await CreateInitializedAsync(
            new TestRepository(original with { Document = original.Document with { Sessions = [model] } }), store, new());
        var session = Assert.Single(viewModel.Sessions);
        var changed = new List<string?>();
        session.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.RefreshSessionStatuses(start.AddTicks(-1));
        Assert.False(session.IsLive);
        viewModel.RefreshSessionStatuses(start);
        Assert.True(session.IsLive);
        Assert.Contains("Live now", session.NavigationDescription);
        Assert.Contains(nameof(session.IsLive), changed);
        Assert.Contains(nameof(session.NavigationDescription), changed);
        Assert.False(viewModel.HasNextSavedSession);
        Assert.False(session.IsSaved);

        session.Update(model with { StartsAt = start.AddHours(1), EndsAt = start.AddHours(2) }, []);
        viewModel.RefreshSessionStatuses(start);
        Assert.False(session.IsLive);
        viewModel.RefreshSessionStatuses(start.AddHours(1));
        Assert.True(session.IsLive);
        session.IsWithdrawn = true;
        viewModel.RefreshSessionStatuses(start.AddHours(1));
        Assert.False(session.IsLive);
    }

    [Fact]
    public async Task ScheduleRetryRepublishesHomeVisibilityAfterAnInterruptedNotification()
    {
        var snapshot = Snapshot();
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var session = snapshot.Document.Sessions[0] with { StartsAt = start, EndsAt = start.AddHours(1) };
        snapshot = snapshot with { Document = snapshot.Document with { Sessions = [session] } };
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        var viewModel = await CreateInitializedAsync(new TestRepository(snapshot), store, new TestConnectivity());
        var visibilityNotifications = 0;
        void FailNext(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(viewModel.NextSavedSession))
                throw new InvalidOperationException("Synthetic notification failure.");
        }
        void ObserveVisibility(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(viewModel.HasNextSavedSession))
                visibilityNotifications++;
        }
        viewModel.PropertyChanged += FailNext;
        viewModel.PropertyChanged += ObserveVisibility;

        await Assert.ThrowsAsync<SchedulePresentationException>(() => viewModel.SaveAskSessionsAsync(["id"], default));
        Assert.True(viewModel.HasNextSavedSession);
        Assert.Equal(0, visibilityNotifications);
        Assert.True(viewModel.HasRefreshError);
        viewModel.PropertyChanged -= FailNext;
        await viewModel.InitializeAsync();

        Assert.True(visibilityNotifications > 0);
        Assert.False(viewModel.HasRefreshError);
    }

    [Fact]
    public async Task HomeTimerFailureShowsAnErrorAndRetryRepublishesVisibility()
    {
        var snapshot = Snapshot();
        var start = DateTimeOffset.UtcNow.AddHours(-2);
        var session = snapshot.Document.Sessions[0] with { StartsAt = start, EndsAt = start.AddHours(1) };
        snapshot = snapshot with { Document = snapshot.Document with { Sessions = [session] } };
        var viewModel = await CreateInitializedAsync(new TestRepository(snapshot), new TestScheduleStore(),
            new TestConnectivity());
        viewModel.RefreshSessionStatuses(start.AddMinutes(1));
        Assert.True(viewModel.HasNextSavedSession);

        var visibilityNotifications = 0;
        void FailNext(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(viewModel.NextSavedSession))
                throw new InvalidOperationException("Synthetic timer notification failure.");
        }
        void ObserveVisibility(object? _, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(viewModel.HasNextSavedSession))
                visibilityNotifications++;
        }
        viewModel.PropertyChanged += FailNext;
        viewModel.PropertyChanged += ObserveVisibility;

        viewModel.RefreshSessionStatuses(session.EndsAt);

        Assert.False(viewModel.HasNextSavedSession);
        Assert.Equal(0, visibilityNotifications);
        Assert.True(viewModel.HasRefreshError);
        viewModel.PropertyChanged -= FailNext;
        await viewModel.InitializeAsync();
        Assert.True(visibilityNotifications > 0);
        Assert.False(viewModel.HasRefreshError);
    }

    [Fact]
    public async Task SharingAStaleOrWithdrawnSessionIsNotOfferedAndDoesNotMutateTheSchedule()
    {
        var original = Snapshot();
        var sharing = new TestConferenceSharing();
        var viewModel = Create(new(original), new(), new(), sharing: sharing);
        await viewModel.InitializeAsync();
        var session = Assert.Single(viewModel.Sessions);

        await session.ShareCommand.ExecuteAsync(null);

        Assert.Contains(session.Title, sharing.Text);
        Assert.StartsWith("Check out this .NET Conf session:", sharing.Text);
        Assert.Contains("Test speaker", sharing.Text);
        Assert.DoesNotContain("UTC", sharing.Text);
        Assert.DoesNotContain("planning to watch", sharing.Text);
        Assert.Contains(ConferenceLinks.Website, sharing.Text);
        Assert.Contains(ConferenceLinks.Hashtag, sharing.Text);
        Assert.True(session.IsSaved);
        session.IsWithdrawn = true;
        Assert.False(session.CanShare);
        Assert.False(session.ShareCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConferenceDatesAndInvitationFollowUpdatedMetadata()
    {
        var original = Snapshot();
        var repository = new TestRepository(original with { Document = original.Document with
        {
            StartDate = new(2026, 11, 10), EndDate = new(2026, 11, 13)
        } });
        var sharing = new TestConferenceSharing();
        var viewModel = Create(repository, new(), new(), sharing: sharing);
        await viewModel.InitializeAsync();
        await (viewModel.RefreshCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Equal("November 10–13 · Online", viewModel.ConferenceDateContext);
        await viewModel.ShareConferenceCommand.ExecuteAsync(null);
        Assert.Contains("Test conference, November 10–13.", sharing.Text);

        repository.Snapshot = original with { Document = original.Document with
        {
            StartDate = new(2027, 12, 31), EndDate = new(2028, 1, 2)
        } };
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("December 31, 2027–January 2, 2028 · Online", viewModel.ConferenceDateContext);
        Assert.Contains("December 31, 2027–January 2, 2028", viewModel.ConferenceShareText);

        repository.Snapshot = original;
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasConferenceDates);
        Assert.Equal("", viewModel.ConferenceDateContext);
        Assert.DoesNotContain("November", viewModel.ConferenceShareText);
    }

    [Fact]
    public async Task CommunityOverviewDistinguishesUnloadedEmptyAndPopulatedEvents()
    {
        var original = Snapshot();
        var repository = new TestRepository(original with { Document = original.Document with
        {
            CommunityEventsAreMock = false
        } });
        var viewModel = Create(repository, new(), new());
        Assert.False(viewModel.IsCommunityEventsEmpty);
        Assert.False(viewModel.HasCommunityEvents);

        await viewModel.InitializeAsync();
        await (viewModel.RefreshCommand.ExecutionTask ?? Task.CompletedTask);
        Assert.True(viewModel.IsCommunityEventsEmpty);
        Assert.Equal("No community events are listed in the app.", viewModel.CommunityEventSummary);
        Assert.False(viewModel.HasMockCommunityEvents);

        repository.Snapshot = repository.Snapshot with { Document = repository.Snapshot.Document with
        {
            CommunityEventsAreMock = true,
            CommunityEvents = [new CommunityEvent
            {
                Id = "synthetic", Name = "Synthetic event", Kind = "watch-party", Venue = "Test venue",
                City = "Test city", Country = "Test country", Latitude = 0, Longitude = 0,
                WebsiteUrl = "https://example.com"
            }]
        } };
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsCommunityEventsEmpty);
        Assert.True(viewModel.HasCommunityEvents);
        Assert.True(viewModel.HasMockCommunityEvents);
        Assert.Contains("fictional", viewModel.CommunityEventSummary);
    }
    [Fact]
    public async Task WatchingIsExplicitSeparateFromSavingAndPersistsAcrossViewModels()
    {
        var original = Snapshot();
        var repository = new TestRepository(original with { Document = original.Document with
        {
            Sessions = [original.Document.Sessions[0] with { RecordingUrl = "https://youtu.be/synthetic01" }]
        } });
        var savedStore = new TestScheduleStore();
        var watchedStore = new TestScheduleStore();
        watchedStore.SavedIds.Clear();
        var browser = new TestBrowser();
        var videoLauncher = new TestSessionVideoLauncher();
        var viewModel = await CreateInitializedAsync(repository, savedStore, new(),
            watched: new(watchedStore), browser: browser, videoLauncher: videoLauncher);
        var session = Assert.Single(viewModel.Sessions);
        Assert.True(session.IsSaved);
        Assert.False(session.IsWatched);
        Assert.Equal("Mark as watched", session.WatchedActionLabel);
        Assert.Equal("Mark as watched", session.WatchedButtonLabel);
        Assert.Equal("Not watched. Mark as watched", session.WatchedDescription);
        Assert.Equal(FluentIconsRegular.Circle24, session.WatchedGlyph);
        Assert.Equal(FluentIconsRegular.FontFamily, session.WatchedFontFamily);
        var changed = new List<string?>();
        session.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await session.WatchRecordingCommand.ExecuteAsync(null);
        Assert.Equal("https://youtu.be/synthetic01", videoLauncher.OpenedUri?.ToString());
        Assert.Null(browser.OpenedUri);
        Assert.False(session.IsWatched);
        Assert.Equal(0, watchedStore.Writes);

        await session.ToggleWatchedCommand.ExecuteAsync(null);
        Assert.True(session.IsWatched);
        Assert.True(session.IsSaved);
        Assert.Equal("Watched", session.VideoStatusLabel);
        Assert.Equal("Mark as not watched", session.WatchedActionLabel);
        Assert.Equal("Watched", session.WatchedButtonLabel);
        Assert.Equal("Watched. Mark as not watched", session.WatchedDescription);
        Assert.Equal(FluentIconsFilled.CheckmarkCircle24, session.WatchedGlyph);
        Assert.Equal(FluentIconsFilled.FontFamily, session.WatchedFontFamily);
        Assert.Contains(nameof(session.WatchedButtonLabel), changed);
        Assert.Contains(nameof(session.WatchedDescription), changed);
        Assert.Contains(nameof(session.WatchedGlyph), changed);
        Assert.Contains(nameof(session.WatchedFontFamily), changed);
        Assert.Contains("Marked as watched", session.NavigationDescription);
        Assert.Equal("id", Assert.Single(watchedStore.SavedIds));
        Assert.Equal("id", Assert.Single(savedStore.SavedIds));

        var restarted = await CreateInitializedAsync(repository, savedStore, new(), watched: new(watchedStore));
        var restored = Assert.Single(restarted.Sessions);
        Assert.True(restored.IsWatched);
        Assert.True(restored.IsSaved);
        await restored.ToggleWatchedCommand.ExecuteAsync(null);
        Assert.False(restored.IsWatched);
        Assert.Equal("Mark as watched", restored.WatchedButtonLabel);
        Assert.Equal("Not watched. Mark as watched", restored.WatchedDescription);
        Assert.Equal(FluentIconsRegular.Circle24, restored.WatchedGlyph);
        Assert.True(restored.IsSaved);
        Assert.Empty(watchedStore.SavedIds);
        Assert.Equal("id", Assert.Single(savedStore.SavedIds));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionVideoFallsBackToBrowserWhenSystemLauncherFails(bool throwOnLaunch)
    {
        var browser = new TestBrowser();
        var videoLauncher = new TestSessionVideoLauncher
        {
            Result = false,
            Failure = throwOnLaunch ? new FeatureNotSupportedException("No video handler.") : null
        };
        var original = Snapshot();
        var repository = new TestRepository(original with { Document = original.Document with
        {
            Sessions = [original.Document.Sessions[0] with { RecordingUrl = "https://youtu.be/synthetic01" }]
        } });
        var viewModel = await CreateInitializedAsync(repository, new(), new(), browser: browser, videoLauncher: videoLauncher);
        var session = Assert.Single(viewModel.Sessions);

        await session.WatchRecordingCommand.ExecuteAsync(null);

        Assert.Equal("https://youtu.be/synthetic01", videoLauncher.OpenedUri?.ToString());
        Assert.Equal(videoLauncher.OpenedUri, browser.OpenedUri);
        Assert.Equal(BrowserLaunchMode.SystemPreferred, browser.LaunchMode);
        Assert.False(session.IsWatched);
    }

    [Fact]
    public async Task SessionVideoReportsWhenNeitherSystemLauncherNorBrowserCanOpen()
    {
        var browser = new TestBrowser { Result = false };
        var launcher = new TestSessionVideoLauncher { Result = false };
        var viewModel = Create(new(Snapshot()), new(), new(), browser: browser, videoLauncher: launcher);

        var opened = await viewModel.TryOpenSessionVideoAsync(new Uri("https://youtu.be/synthetic01"));

        Assert.False(opened);
        Assert.Equal(launcher.OpenedUri, browser.OpenedUri);
    }

    [Fact]
    public async Task SessionVideoPropagatesBrowserFailureForVisibleRecovery()
    {
        var browser = new TestBrowser { Failure = new FeatureNotSupportedException("No browser.") };
        var launcher = new TestSessionVideoLauncher { Result = false };
        var viewModel = Create(new(Snapshot()), new(), new(), browser: browser, videoLauncher: launcher);

        await Assert.ThrowsAsync<FeatureNotSupportedException>(() =>
            viewModel.TryOpenSessionVideoAsync(new Uri("https://youtu.be/synthetic01")));
    }

    [Theory]
    [InlineData("https://example.com/watch?v=synthetic01")]
    [InlineData("https://youtube.com.example.com/watch?v=synthetic01")]
    [InlineData("http://www.youtube.com/watch?v=synthetic01")]
    public async Task SessionVideoRejectsUnvalidatedUrlsBeforeOpening(string url)
    {
        var browser = new TestBrowser();
        var launcher = new TestSessionVideoLauncher();
        var viewModel = Create(new(Snapshot()), new(), new(), browser: browser, videoLauncher: launcher);

        await Assert.ThrowsAsync<UriFormatException>(() => viewModel.TryOpenSessionVideoAsync(new Uri(url)));

        Assert.Null(launcher.OpenedUri);
        Assert.Null(browser.OpenedUri);
    }

    [Fact]
    public async Task ConferenceWebsiteAndRegistrationKeepUsingTheBrowser()
    {
        var browser = new TestBrowser();
        var launcher = new TestSessionVideoLauncher();
        var viewModel = Create(new(Snapshot()), new(), new(), browser: browser, videoLauncher: launcher);

        await viewModel.OpenWebsiteCommand.ExecuteAsync(null);
        Assert.Equal(ConferenceLinks.Website, browser.OpenedUri?.ToString());
        Assert.Equal(BrowserLaunchMode.SystemPreferred, browser.LaunchMode);
        await viewModel.OpenRegistrationCommand.ExecuteAsync(null);
        Assert.Equal(ConferenceLinks.Registration, browser.OpenedUri?.ToString());
        Assert.Equal(BrowserLaunchMode.SystemPreferred, browser.LaunchMode);
        Assert.Null(launcher.OpenedUri);
    }

    [Fact]
    public async Task AvaloniaVideoLauncherUsesThePlatformBrowser()
    {
        var browser = new TestBrowser();
        var launcher = new SessionVideoLauncher(browser);
        var uri = new Uri("https://youtu.be/synthetic01");

        Assert.True(await launcher.OpenAsync(uri));

        Assert.Equal(uri, browser.OpenedUri);
        Assert.Equal(BrowserLaunchMode.SystemPreferred, browser.LaunchMode);
    }

    [Fact]
    public async Task HideWatchedComposesWithVideoTopicDayAndSearchAndCanBeCleared()
    {
        var original = Snapshot();
        var first = original.Document.Sessions[0] with
        {
            Title = "Synthetic watched", Topics = ["Cloud"], RecordingUrl = "https://youtu.be/synthetic01"
        };
        var second = first with { Id = "second", Title = "Synthetic unwatched" };
        var noVideo = first with { Id = "third", Title = "Synthetic no video", RecordingUrl = "" };
        var otherTopic = first with { Id = "fourth", Title = "Synthetic other", Topics = ["Desktop"] };
        var repository = new TestRepository(original with { Document = original.Document with
        {
            Sessions = [first, second, noVideo, otherTopic]
        } });
        var watchedStore = new TestScheduleStore();
        var viewModel = await CreateInitializedAsync(repository, new(), new(), watched: new(watchedStore));
        viewModel.SessionQuery = "Synthetic";
        viewModel.SearchSessionsCommand.Execute(null);
        viewModel.SelectedDay = Assert.Single(viewModel.Days, day => day.Day is not null);
        viewModel.RecordingsOnly = true;
        viewModel.HideWatched = true;
        Assert.Single(viewModel.Topics, topic => topic.Name == "Cloud").IsSelected = true;
        var filtered = Assert.Single(viewModel.Sessions);
        Assert.Equal("second", filtered.Id);
        Assert.Equal("3", viewModel.FilterBadgeText);
        Assert.Contains("Cloud", viewModel.FilterSummary);
        Assert.Contains("Video available", viewModel.FilterSummary);
        Assert.Contains("Hide watched", viewModel.FilterSummary);

        await filtered.ToggleWatchedCommand.ExecuteAsync(null);
        Assert.Empty(viewModel.Sessions);
        Assert.Equal("Show 0 sessions", viewModel.ShowFilteredSessionsLabel);
        Assert.Contains("unwatched videos", viewModel.SessionsEmptyMessage);
        await filtered.ToggleWatchedCommand.ExecuteAsync(null);
        Assert.Same(filtered, Assert.Single(viewModel.Sessions));

        var selectedDay = viewModel.SelectedDay;
        viewModel.ClearSessionFiltersCommand.Execute(null);
        Assert.False(viewModel.RecordingsOnly);
        Assert.False(viewModel.HideWatched);
        Assert.False(viewModel.HasSessionFilters);
        Assert.Null(viewModel.FilterBadgeText);
        Assert.Equal("Synthetic", viewModel.SessionQuery);
        Assert.Same(selectedDay, viewModel.SelectedDay);
        Assert.Equal(4, viewModel.Sessions.Count);
        Assert.True(viewModel.FindSession("id")!.IsSaved);
    }

    [Fact]
    public async Task LiveViewedStatusDoesNotRequireAVideoAndSurvivesVideoChanges()
    {
        var original = Snapshot();
        var repository = new TestRepository(original);
        var viewModel = await CreateInitializedAsync(repository, new(), new());
        var session = Assert.Single(viewModel.Sessions);
        Assert.True(session.HasWatchActions);
        Assert.True(session.HasViewingGuidance);
        Assert.True(session.ToggleWatchedCommand.CanExecute(null));
        await session.ToggleWatchedCommand.ExecuteAsync(null);
        Assert.True(session.IsWatched);
        Assert.Equal("Mark as not watched", session.WatchedActionLabel);

        repository.Snapshot = original with { Document = original.Document with
        {
            Sessions = [original.Document.Sessions[0] with { RecordingUrl = "https://youtu.be/synthetic01" }]
        } };
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.True(session.HasWatchActions);
        Assert.True(session.IsWatched);

        repository.Snapshot = original;
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.False(session.HasRecording);
        Assert.True(session.IsWatched);
        Assert.True(session.HasWatchActions);
        Assert.True(session.ToggleWatchedCommand.CanExecute(null));
        Assert.Equal("Watched", session.VideoStatusLabel);
        await session.ToggleWatchedCommand.ExecuteAsync(null);
        Assert.False(session.IsWatched);
        Assert.True(session.HasWatchActions);
        Assert.Equal("Mark as watched", session.WatchedActionLabel);
        session.IsWithdrawn = true;
        Assert.False(session.HasWatchActions);
        Assert.False(session.HasViewingGuidance);
    }

    [Fact]
    public async Task WatchedStorageFailuresAreVisibleAndRetryPreservesTheSchedule()
    {
        var original = Snapshot();
        var repository = new TestRepository(original with { Document = original.Document with
        {
            Sessions = [original.Document.Sessions[0] with { RecordingUrl = "https://youtu.be/synthetic01" }]
        } });
        var watchedStore = new TestScheduleStore { FailReads = true };
        watchedStore.SavedIds.Clear();
        var scheduleStore = new TestScheduleStore();
        var viewModel = await CreateInitializedAsync(repository, scheduleStore, new(), watched: new(watchedStore));
        var session = Assert.Single(viewModel.Sessions);
        Assert.False(viewModel.IsWatchedAvailable);
        Assert.False(viewModel.CanUseWatchedFilter);
        Assert.False(session.ToggleWatchedCommand.CanExecute(null));
        Assert.Contains("watched status couldn't be read", viewModel.RefreshMessage);
        Assert.True(session.IsSaved);

        watchedStore.FailReads = false;
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsWatchedAvailable);
        Assert.True(session.ToggleWatchedCommand.CanExecute(null));
        Assert.False(viewModel.HasRefreshError);
        watchedStore.WriteFailure = new IOException("Synthetic write failure");
        await session.ToggleWatchedCommand.ExecuteAsync(null);
        Assert.False(session.IsWatched);
        Assert.Contains("Couldn't update your watched status", viewModel.RefreshMessage);
        Assert.Empty(watchedStore.SavedIds);
        Assert.Equal("id", Assert.Single(scheduleStore.SavedIds));

        watchedStore.WriteFailure = null;
        await session.ToggleWatchedCommand.ExecuteAsync(null);
        Assert.True(session.IsWatched);
        Assert.False(viewModel.HasRefreshError);
        Assert.True(session.IsSaved);

        viewModel.HideWatched = true;
        watchedStore.FailReads = true;
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsWatchedAvailable);
        Assert.True(viewModel.CanUseWatchedFilter);
        viewModel.HideWatched = false;
        Assert.False(viewModel.CanUseWatchedFilter);
        Assert.Same(session, Assert.Single(viewModel.Sessions));
        Assert.True(session.IsWatched);
    }

    [Fact]
    public async Task WatchedPresentationFailureReportsSavedStateAndRetryRepublishesIt()
    {
        var original = Snapshot();
        var repository = new TestRepository(original with { Document = original.Document with
        {
            Sessions = [original.Document.Sessions[0] with { RecordingUrl = "https://youtu.be/synthetic01" }]
        } });
        var watchedStore = new TestScheduleStore();
        watchedStore.SavedIds.Clear();
        var viewModel = await CreateInitializedAsync(repository, new(), new(), watched: new(watchedStore));
        var session = Assert.Single(viewModel.Sessions);
        System.ComponentModel.PropertyChangedEventHandler failure = (_, args) =>
        {
            if (args.PropertyName == nameof(SessionViewModel.IsWatched))
                throw new InvalidOperationException("Synthetic UI notification failure");
        };
        session.PropertyChanged += failure;

        await session.ToggleWatchedCommand.ExecuteAsync(null);
        Assert.Equal("id", Assert.Single(watchedStore.SavedIds));
        Assert.Contains("watched status is kept", viewModel.RefreshMessage);
        session.PropertyChanged -= failure;
        var republished = false;
        session.PropertyChanged += (_, args) => republished |= args.PropertyName == "";
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.True(republished);
        Assert.True(session.IsWatched);
        Assert.False(viewModel.HasRefreshError);
    }

    private sealed class TestBrowser : IBrowser
    {
        public Uri? OpenedUri { get; private set; }
        public BrowserLaunchMode? LaunchMode { get; private set; }
        public bool Result { get; set; } = true;
        public Exception? Failure { get; set; }
        public Task<bool> OpenAsync(Uri uri, BrowserLaunchOptions options)
        {
            OpenedUri = uri;
            LaunchMode = options.LaunchMode;
            return Failure is null ? Task.FromResult(Result) : Task.FromException<bool>(Failure);
        }
    }

    private sealed class TestSessionVideoLauncher : ISessionVideoLauncher
    {
        public Uri? OpenedUri { get; private set; }
        public bool Result { get; set; } = true;
        public Exception? Failure { get; set; }
        public Task<bool> OpenAsync(Uri uri)
        {
            OpenedUri = uri;
            return Failure is null ? Task.FromResult(Result) : Task.FromException<bool>(Failure);
        }
    }

    private sealed class TestConferenceSharing : IConferenceSharing
    {
        public bool CanShare => true;
        public string Text { get; private set; } = "";
        public Task ShareAsync(string title, string text) { Text = text; return Task.CompletedTask; }
        public Task CopyAsync(string text) { Text = text; return Task.CompletedTask; }
    }

    private static ConferenceViewModel Create(TestRepository repository, TestScheduleStore store, TestConnectivity connectivity,
        ScheduleService? schedule = null, ISessionReminderUpdates? reminders = null, IConferenceSharing? sharing = null,
        WatchedSessionService? watched = null, IBrowser? browser = null, ISessionVideoLauncher? videoLauncher = null) =>
        new(repository, schedule ?? new ScheduleService(store), watched ?? EmptyWatchedService(), new DetailNavigationService(),
            browser!, videoLauncher ?? new TestSessionVideoLauncher(), connectivity,
            NullLogger<ConferenceViewModel>.Instance, reminders ?? new TestReminders(), sharing ?? new TestConferenceSharing());

    private static WatchedSessionService EmptyWatchedService()
    {
        var store = new TestScheduleStore();
        store.SavedIds.Clear();
        return new(store);
    }

    private static async Task<ConferenceViewModel> CreateInitializedAsync(
        TestRepository repository, TestScheduleStore store, TestConnectivity connectivity,
        ScheduleService? schedule = null, ISessionReminderUpdates? reminders = null,
        WatchedSessionService? watched = null, IBrowser? browser = null, ISessionVideoLauncher? videoLauncher = null)
    {
        var viewModel = Create(repository, store, connectivity, schedule, reminders, watched: watched, browser: browser,
            videoLauncher: videoLauncher);
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
        public int HoldRefreshCall { get; init; }
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
            if ((HoldFirstRequest && RefreshCalls == 1) || RefreshCalls == HoldRefreshCall)
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
