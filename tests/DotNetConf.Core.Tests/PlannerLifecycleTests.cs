using DotNetConf.App.Services;
using DotNetConf.App.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Storage;
using NSubstitute;

namespace DotNetConf.Core.Tests;

public sealed class PlannerLifecycleTests
{
    private static async Task<(PlannerViewModel ViewModel, IOnDevicePlanner Planner,
        IPlannerScheduleUpdates Updates, DelayedStore Store, IConferenceDataSource Data)> CreateAsync(
            IPlannerModelSelection? selection = null)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var document = await ConferenceJson.ReadAsync(stream);
        var data = Substitute.For<IConferenceDataSource>();
        data.LoadAsync(Arg.Any<CancellationToken>()).Returns(document);
        var planner = Substitute.For<IOnDevicePlanner>();
        planner.UnsupportedReason.Returns((string?)null);
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(new PlannerModelState(true, false, ""));
        planner.SuggestStartingPointsAsync(document, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PlannerStartingPoint>>([]));
        planner.ProposeAsync(document, Arg.Any<string>(), Arg.Any<TimeZoneInfo>(),
                Arg.Any<string[]>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<PlannerWatchingHours>())
            .Returns(new PlannerResult(new(["building-with-maui"], [], []), ""));
        var updates = Substitute.For<IPlannerScheduleUpdates>();
        updates.RefreshRemindersAsync().Returns(true);
        var store = new DelayedStore();
        var vm = new PlannerViewModel(data, new ScheduleService(store), planner, updates,
            NullLogger<PlannerViewModel>.Instance, selection ?? new TestPlannerModelSelection());
        await vm.InitializeAsync();
        vm.Request = "Mobile";
        await vm.GenerateCommand.ExecuteAsync(null);
        return (vm, planner, updates, store, data);
    }

    [Fact]
    public async Task SaveFreezesSelectionAndPersistsOnlyTheConfirmedSnapshot()
    {
        var (vm, _, updates, store, _) = await CreateAsync();
        var session = Assert.Single(vm.Sessions);
        vm.ConstraintsConfirmed = true;
        var outcomes = new List<string>();
        vm.OutcomeAvailable += (_, message) => outcomes.Add(message);
        store.Delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = vm.ApplyCommand.ExecuteAsync(null);
        await store.Started.Task;
        Assert.True(vm.IsBusy);
        Assert.True(vm.IsSaving);
        Assert.False(vm.CanCancel);
        Assert.False(session.CanSelect);
        Assert.False(vm.CanConfirm);
        Assert.False(vm.CanApply);
        Assert.Empty(outcomes);
        session.IsSelected = false;
        vm.ConstraintsConfirmed = false;
        store.Delay.SetResult();
        await save;
        Assert.Equal(["already-liked", "building-with-maui"], store.Saved.Order().ToArray());
        Assert.True(vm.HasAddedSessions);
        Assert.Empty(vm.Sessions);
        Assert.Contains("added to My Schedule", Assert.Single(outcomes));
        updates.Received(1).RefreshSavedState();
    }

    [Fact]
    public async Task ReturningDuringASaveRechecksTheChangedModelAfterSaving()
    {
        var selection = new TestPlannerModelSelection { IsSupported = true };
        var (vm, planner, _, store, _) = await CreateAsync(selection);
        planner.ClearReceivedCalls();
        vm.ConstraintsConfirmed = true;
        store.Delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = vm.ApplyCommand.ExecuteAsync(null);
        await store.Started.Task;

        vm.Cancel();
        selection.Select("gemini-nano-preview-fast");
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(
            new PlannerModelState(false, false, "Download the selected model.")
            {
                Setup = PlannerModelSetup.DownloadRequired,
                ModelId = "gemini-nano-preview-fast"
            });
        var returning = vm.InitializeAsync();
        store.Delay.SetResult();
        await Task.WhenAll(save, returning).WaitAsync(TimeSpan.FromSeconds(5));

        await planner.Received(1).CheckReadinessAsync(Arg.Any<CancellationToken>());
        Assert.True(vm.CanDownloadModel);
        Assert.Contains("building-with-maui", store.Saved);
        Assert.Contains("added to My Schedule", vm.Status);
        Assert.Empty(vm.Sessions);
    }

    [Fact]
    public async Task ReturningDuringAFailedSaveKeepsThePreviewAndFailureAfterTheModelChanges()
    {
        var selection = new TestPlannerModelSelection { IsSupported = true };
        var (vm, planner, _, store, _) = await CreateAsync(selection);
        planner.ClearReceivedCalls();
        var row = Assert.Single(vm.Sessions);
        vm.ConstraintsConfirmed = true;
        store.Fail = true;
        store.Delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = vm.ApplyCommand.ExecuteAsync(null);
        await store.Started.Task;

        vm.Cancel();
        selection.Select("gemini-nano-preview-fast");
        var returning = vm.InitializeAsync();
        store.Delay.SetResult();
        await Task.WhenAll(save, returning).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(vm.IsAiMode);
        Assert.False(vm.IsReady);
        Assert.True(vm.CanRetryModel);
        Assert.Same(row, Assert.Single(vm.Sessions));
        Assert.True(row.IsSelected);
        Assert.False(vm.ConstraintsConfirmed);
        Assert.Contains("Couldn't save", vm.Status);
        Assert.Equal(["already-liked"], store.Saved);
        await planner.DidNotReceive().CheckReadinessAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturningAfterASaveFinishesKeepsItsOutcomeWhileRecheckingTheModel()
    {
        var selection = new TestPlannerModelSelection { IsSupported = true };
        var (vm, planner, _, store, _) = await CreateAsync(selection);
        planner.ClearReceivedCalls();
        vm.ConstraintsConfirmed = true;
        store.Delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = vm.ApplyCommand.ExecuteAsync(null);
        await store.Started.Task;

        vm.Cancel();
        selection.Select("gemini-nano-preview-fast");
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(
            new PlannerModelState(false, false, "Download the selected model.")
            {
                Setup = PlannerModelSetup.DownloadRequired,
                ModelId = "gemini-nano-preview-fast"
            });
        store.Delay.SetResult();
        await save;
        await vm.InitializeAsync();

        await planner.Received(1).CheckReadinessAsync(Arg.Any<CancellationToken>());
        Assert.True(vm.CanDownloadModel);
        Assert.Contains("added to My Schedule", vm.Status);
        Assert.Empty(vm.Sessions);
        Assert.Contains("building-with-maui", store.Saved);
    }

    [Fact]
    public async Task ReturningAfterASavedPresentationFailsKeepsTheWarning()
    {
        var selection = new TestPlannerModelSelection { IsSupported = true };
        var (vm, planner, updates, store, _) = await CreateAsync(selection);
        updates.When(value => value.RefreshSavedState()).Do(_ => throw new IOException("Synthetic display failure"));
        vm.ConstraintsConfirmed = true;
        store.Delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = vm.ApplyCommand.ExecuteAsync(null);
        await store.Started.Task;

        vm.Cancel();
        selection.Select("gemini-nano-preview-fast");
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(
            new PlannerModelState(false, false, "Download the selected model.")
            {
                Setup = PlannerModelSetup.DownloadRequired,
                ModelId = "gemini-nano-preview-fast"
            });
        var returning = vm.InitializeAsync();
        store.Delay.SetResult();
        await Task.WhenAll(save, returning).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(vm.HasAddedSessions);
        Assert.Contains("were saved, but", vm.Status);
        Assert.Empty(vm.Sessions);
        Assert.Contains("building-with-maui", store.Saved);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ModelChangeAfterAHiddenSaveKeepsItsOutcome(bool fail)
    {
        var selection = new TestPlannerModelSelection { IsSupported = true };
        var (vm, planner, _, store, _) = await CreateAsync(selection);
        var row = Assert.Single(vm.Sessions);
        vm.ConstraintsConfirmed = true;
        store.Fail = fail;
        store.Delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = vm.ApplyCommand.ExecuteAsync(null);
        await store.Started.Task;

        vm.Cancel();
        store.Delay.SetResult();
        await save;
        selection.Select("gemini-nano-preview-fast");
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(
            new PlannerModelState(false, false, "Download the selected model.")
            {
                Setup = PlannerModelSetup.DownloadRequired,
                ModelId = "gemini-nano-preview-fast"
            });
        await vm.InitializeAsync();

        if (fail)
        {
            Assert.Contains("Couldn't save", vm.Status);
            Assert.Same(row, Assert.Single(vm.Sessions));
            Assert.True(vm.IsAiMode);
            Assert.Equal(["already-liked"], store.Saved);
        }
        else
        {
            Assert.Contains("added to My Schedule", vm.Status);
            Assert.Empty(vm.Sessions);
            Assert.Contains("building-with-maui", store.Saved);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedAutomaticDownloadingCheckKeepsTheSaveOutcomeForTheNextCheck(bool changeModel)
    {
        var selection = new TestPlannerModelSelection { IsSupported = true };
        var (vm, planner, _, store, _) = await CreateAsync(selection);
        vm.ConstraintsConfirmed = true;
        store.Delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = vm.ApplyCommand.ExecuteAsync(null);
        await store.Started.Task;

        vm.Cancel();
        selection.Select("gemini-nano-preview-fast");
        store.Delay.SetResult();
        await save;
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(
            new PlannerModelState(false, false, "Android is downloading the selected model.")
            {
                Setup = PlannerModelSetup.Downloading,
                ModelId = "gemini-nano-preview-fast"
            });
        var check = vm.InitializeAsync();
        Assert.True(vm.IsDownloadingModel);
        Assert.Contains("added to My Schedule", vm.Status);
        if (changeModel)
            selection.Select("gemini-nano-fast");
        else
            vm.Cancel();
        await check;
        Assert.Contains("added to My Schedule", vm.Status);

        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(
            new PlannerModelState(true, false, "") { ModelId = selection.SelectedModelId });
        await vm.InitializeAsync();
        Assert.True(vm.IsReady);
        Assert.Contains("added to My Schedule", vm.Status);
        Assert.Empty(vm.Sessions);
    }

    [Fact]
    public async Task TimedOutAutomaticCheckKeepsTheSaveOutcomeForTheNextCheck()
    {
        var selection = new TestPlannerModelSelection { IsSupported = true };
        var (vm, planner, _, store, _) = await CreateAsync(selection);
        vm.ConstraintsConfirmed = true;
        store.Delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = vm.ApplyCommand.ExecuteAsync(null);
        await store.Started.Task;

        vm.Cancel();
        selection.Select("gemini-nano-preview-fast");
        store.Delay.SetResult();
        await save;
        var pending = new TaskCompletionSource<PlannerModelState>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken checkToken = default;
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            checkToken = call.Arg<CancellationToken>();
            return pending.Task;
        });

        await vm.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(45));
        Assert.True(checkToken.IsCancellationRequested);
        Assert.Contains("added to My Schedule", vm.Status);

        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(
            new PlannerModelState(true, false, "") { ModelId = "gemini-nano-preview-fast" });
        await vm.InitializeAsync();
        Assert.True(vm.IsReady);
        Assert.Contains("added to My Schedule", vm.Status);
        Assert.Empty(vm.Sessions);
    }

    [Fact]
    public async Task RealWeakSelectionNotifiesALivePlannerAfterCollection()
    {
        var selection = new PlannerModelSelection(Substitute.For<IPreferences>(),
            NullLogger<PlannerModelSelection>.Instance);
        var (vm, _, _, _, _) = await CreateAsync(selection);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        selection.Select("gemini-nano-preview-fast");

        Assert.False(vm.IsReady);
        Assert.False(vm.HasProposal);
        Assert.Contains("model changed", vm.ModelStatus, StringComparison.OrdinalIgnoreCase);
        GC.KeepAlive(vm);
    }

    [Fact]
    public async Task LeavingAgainDuringASaveDoesNotStartAHiddenRecheck()
    {
        var selection = new TestPlannerModelSelection { IsSupported = true };
        var (vm, planner, _, store, _) = await CreateAsync(selection);
        planner.ClearReceivedCalls();
        vm.ConstraintsConfirmed = true;
        store.Delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = vm.ApplyCommand.ExecuteAsync(null);
        await store.Started.Task;

        vm.Cancel();
        selection.Select("gemini-nano-preview-fast");
        var returning = vm.InitializeAsync();
        vm.Cancel();
        store.Delay.SetResult();
        await Task.WhenAll(save, returning).WaitAsync(TimeSpan.FromSeconds(5));

        await planner.DidNotReceive().CheckReadinessAsync(Arg.Any<CancellationToken>());
        Assert.Contains("building-with-maui", store.Saved);
        await vm.InitializeAsync();
        await planner.Received(1).CheckReadinessAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedWriteKeepsThePreviewAndOriginalLikesAndAnnouncesFailureOnce()
    {
        var (vm, _, _, store, _) = await CreateAsync();
        store.Fail = true;
        vm.ConstraintsConfirmed = true;
        var outcomes = new List<string>();
        vm.OutcomeAvailable += (_, message) => outcomes.Add(message);
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.False(vm.IsBusy);
        Assert.True(Assert.Single(vm.Sessions).CanSelect);
        Assert.Equal(["already-liked"], store.Saved);
        Assert.False(vm.HasAddedSessions);
        Assert.Contains("Couldn't save", Assert.Single(outcomes));
        Assert.False(vm.ConstraintsConfirmed);
    }

    [Fact]
    public async Task ReminderFailureCannotClaimTheSuccessfulSaveDidNotHappen()
    {
        var (vm, _, updates, store, _) = await CreateAsync();
        updates.RefreshRemindersAsync().Returns(Task.FromException<bool>(new IOException("Reminder storage")));
        vm.ConstraintsConfirmed = true;
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.Contains("building-with-maui", store.Saved);
        Assert.True(vm.HasAddedSessions);
        Assert.Contains("were saved", vm.Status);
        Assert.DoesNotContain("have not changed", vm.Status);
    }

    [Fact]
    public async Task CancellingGenerationRejectsLateResultsAndEmitsOneOutcome()
    {
        var (vm, planner, _, _, _) = await CreateAsync();
        var pending = new TaskCompletionSource<PlannerResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        planner.ProposeAsync(Arg.Any<ConferenceDocument>(), Arg.Any<string>(), Arg.Any<TimeZoneInfo>(),
            Arg.Any<string[]>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<PlannerWatchingHours>()).Returns(call =>
            {
                token = call.Arg<CancellationToken>();
                return pending.Task;
            });
        var outcomes = new List<string>();
        vm.OutcomeAvailable += (_, message) => outcomes.Add(message);
        var generate = vm.GenerateCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        vm.Cancel();
        Assert.True(token.IsCancellationRequested);
        pending.SetResult(new(new(["building-with-maui"], [], []), ""));
        await generate;
        Assert.Empty(vm.Sessions);
        Assert.False(vm.HasProposal);
        Assert.Contains("cancelled", Assert.Single(outcomes));
    }

    [Fact]
    public async Task UnsupportedModelDoesNotGenerateOrSave()
    {
        var (vm, planner, _, store, _) = await CreateAsync();
        planner.UnsupportedReason.Returns("No model");
        await vm.CheckModelCommand.ExecuteAsync(null);
        Assert.False(vm.IsReady);
        Assert.False(vm.CanGenerate);
        Assert.True(vm.CanEdit);
        Assert.Equal(
            "On-device AI isn't supported on this device. You can still explore the agenda or build My Schedule with hearts.",
            vm.ModelStatus);
        Assert.Equal(["already-liked"], store.Saved);
    }

    [Fact]
    public async Task AnUnchangedAgendaRefreshDoesNotDiscardAReviewedProposal()
    {
        var (vm, _, _, store, data) = await CreateAsync();
        var original = await data.LoadAsync();
        data.LoadAsync(Arg.Any<CancellationToken>()).Returns(original with
        {
            Sessions = original.Sessions.Select(session => session with
            {
                Topics = [.. session.Topics], SpeakerIds = [.. session.SpeakerIds]
            }).ToList(),
            Speakers = original.Speakers.Select(speaker => speaker with { }).ToList()
        });
        vm.ConstraintsConfirmed = true;
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.True(vm.HasAddedSessions);
        Assert.Equal(["already-liked", "building-with-maui"], store.Saved.Order().ToArray());
    }

    [Fact]
    public async Task ChangedSessionTimesRequireAUserToReviewANewProposal()
    {
        var (vm, _, _, store, data) = await CreateAsync();
        var original = await data.LoadAsync();
        data.LoadAsync(Arg.Any<CancellationToken>()).Returns(original with
        {
            Sessions = original.Sessions.Select(session => session.Id == "building-with-maui"
                ? session with { StartsAt = session.StartsAt.AddHours(1), EndsAt = session.EndsAt.AddHours(1) }
                : session).ToList()
        });
        vm.ConstraintsConfirmed = true;
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.False(vm.HasAddedSessions);
        Assert.Equal(["already-liked"], store.Saved);
        Assert.Contains("latest session times", vm.Status);
    }

    [Fact]
    public async Task AnEmptyAgendaExplainsTheDownloadRequirementInsteadOfBlamingTheModel()
    {
        var (vm, planner, _, store, data) = await CreateAsync();
        data.LoadAsync(Arg.Any<CancellationToken>()).Returns(new ConferenceDocument());
        planner.ClearReceivedCalls();
        var outcomes = new List<string>();
        vm.OutcomeAvailable += (_, message) => outcomes.Add(message);
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.Contains("Download", Assert.Single(outcomes));
        Assert.Contains("internet", vm.Status);
        Assert.DoesNotContain("model may be unavailable", vm.Status);
        Assert.Equal(["already-liked"], store.Saved);
        await planner.DidNotReceive().ProposeAsync(Arg.Any<ConferenceDocument>(), Arg.Any<string>(),
            Arg.Any<TimeZoneInfo>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<PlannerWatchingHours>());
    }

    [Fact]
    public async Task WatchingHourChangesInvalidateAnAlreadyConfirmedShortlist()
    {
        var (vm, planner, _, store, _) = await CreateAsync();
        vm.ToggleWatchingHoursCommand.Execute(null);
        vm.ConstraintsConfirmed = true;
        var changed = new List<string?>();
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        vm.WatchingStart = TimeSpan.FromHours(8);
        Assert.Equal("Watching hours: 08:00\u201323:00", vm.WatchingHoursSummary);
        Assert.Contains(nameof(vm.WatchingHoursSummary), changed);
        Assert.Contains(nameof(vm.WatchingHoursActionDescription), changed);
        Assert.False(vm.HasProposal);
        Assert.False(vm.CanApply);
        Assert.False(vm.ConstraintsConfirmed);
        Assert.Empty(vm.Sessions);
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.False(vm.ShowWatchingHours);
        await planner.Received().ProposeAsync(Arg.Any<ConferenceDocument>(), Arg.Any<string>(), Arg.Any<TimeZoneInfo>(),
            Arg.Any<string[]>(), Arg.Any<CancellationToken>(), false,
            new PlannerWatchingHours(new(8, 0), new(23, 0)));
        Assert.Equal(["already-liked"], store.Saved);
    }

    [Fact]
    public async Task WatchingHoursDisclosureDoesNotInvalidateAReviewedShortlist()
    {
        var (vm, _, _, store, _) = await CreateAsync();
        var row = Assert.Single(vm.Sessions);
        vm.ConstraintsConfirmed = true;
        Assert.False(vm.ShowWatchingHours);
        Assert.Equal("Watching hours: 07:00\u201323:00", vm.WatchingHoursSummary);
        Assert.Equal("Change", vm.WatchingHoursAction);

        vm.ToggleWatchingHoursCommand.Execute(null);
        Assert.True(vm.ShowWatchingHours);
        Assert.Equal("Hide", vm.WatchingHoursAction);
        vm.ToggleWatchingHoursCommand.Execute(null);

        Assert.False(vm.ShowWatchingHours);
        Assert.Same(row, Assert.Single(vm.Sessions));
        Assert.True(vm.ConstraintsConfirmed);
        Assert.True(vm.CanApply);
        Assert.Equal(["already-liked"], store.Saved);
    }

    [Fact]
    public async Task MissingWatchingHoursReopenTheEditorWithARecoverableError()
    {
        var (vm, _, _, store, _) = await CreateAsync();
        vm.WatchingStart = null;
        Assert.Equal("Watching hours: choose times", vm.WatchingHoursSummary);

        await vm.GenerateCommand.ExecuteAsync(null);

        Assert.True(vm.ShowWatchingHours);
        Assert.True(vm.HasStatus);
        Assert.Contains("Choose different", vm.Status);
        Assert.False(vm.CanApply);
        Assert.Empty(vm.Sessions);
        Assert.Equal(["already-liked"], store.Saved);
    }

    [Fact]
    public async Task InvalidWatchingHoursShowAnErrorBeforeInference()
    {
        var (vm, planner, _, _, _) = await CreateAsync();
        planner.ClearReceivedCalls();
        vm.WatchingEnd = vm.WatchingStart;
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.Contains("Choose different", vm.Status);
        Assert.True(vm.ShowWatchingHours);
        Assert.False(vm.HasProposal);
        await planner.DidNotReceive().ProposeAsync(Arg.Any<ConferenceDocument>(), Arg.Any<string>(), Arg.Any<TimeZoneInfo>(),
            Arg.Any<string[]>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<PlannerWatchingHours>());
    }

    private sealed class DelayedStore : IScheduleStore
    {
        public string[] Saved { get; private set; } = ["already-liked"];
        public TaskCompletionSource? Delay { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Fail { get; set; }
        public Task<IReadOnlyCollection<string>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<string>>(Saved);
        public async Task WriteAsync(IReadOnlyCollection<string> sessionIds, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            if (Delay is not null)
                await Delay.Task;
            if (Fail)
                throw new IOException("Disk full");
            Saved = sessionIds.ToArray();
        }
    }
}
