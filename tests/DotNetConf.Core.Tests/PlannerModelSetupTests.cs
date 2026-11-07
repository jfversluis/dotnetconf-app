using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DotNetConf.App.Services;
using DotNetConf.App.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DotNetConf.Core.Tests;

public sealed class PlannerModelSetupTests
{
    private static PlannerModelState DownloadRequired => new(false, false, "Download Gemini Nano to get started.")
        { Setup = PlannerModelSetup.DownloadRequired };
    private static PlannerModelState Downloading => new(false, false, "Android is downloading Gemini Nano.")
        { Setup = PlannerModelSetup.Downloading };
    private static PlannerModelState Ready => new(true, false, "");

    private static PlannerViewModel Create(SyntheticPlanner planner, ConferenceDocument? agenda = null,
        IPlannerModelSelection? selection = null)
    {
        var data = Substitute.For<IConferenceDataSource>();
        data.LoadAsync(Arg.Any<CancellationToken>()).Returns(agenda ?? new ConferenceDocument());
        var store = Substitute.For<IScheduleStore>();
        store.ReadAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        return new(data, new ScheduleService(store), planner, Substitute.For<IPlannerScheduleUpdates>(),
            NullLogger<PlannerViewModel>.Instance, selection ?? new TestPlannerModelSelection());
    }

    private static Task When(PlannerViewModel vm, Func<bool> predicate)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, _) =>
        {
            if (predicate())
                completion.TrySetResult();
        };
        if (predicate())
            completion.TrySetResult();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task InitialCheckOffersSetupWithoutDownloadingOrGenerating()
    {
        var planner = new SyntheticPlanner();
        var vm = Create(planner);
        await vm.InitializeAsync();
        vm.Request = "Synthetic request";
        Assert.True(vm.CanDownloadModel);
        Assert.True(vm.CanOfferModelSetup);
        Assert.True(vm.ShowModelSetup);
        Assert.True(vm.CanExplore);
        Assert.True(vm.CanEdit);
        Assert.False(vm.CanGenerate);
        Assert.Contains("Download", vm.ModelStatus);
        Assert.Contains("mobile data", vm.ModelDownloadDisclosure);
        Assert.Equal(0, planner.Downloads);
        Assert.Equal(0, planner.Generations);
    }

    [Fact]
    public async Task ExplicitDownloadShowsRealProgressAndRechecksReadinessWithoutGenerating()
    {
        var planner = new SyntheticPlanner();
        var vm = Create(planner);
        await vm.InitializeAsync();
        vm.Request = "Synthetic request";
        var download = vm.DownloadModelCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.True(vm.IsDownloadingModel);
        Assert.False(vm.CanExplore);
        Assert.True(vm.ShowModelSetup);
        Assert.False(vm.CanDownloadModel);
        Assert.False(vm.CanGenerate);
        Assert.False(vm.HasDownloadProgress);
        var progressed = When(vm, () => vm.DownloadProgressText.Contains("1.0 of 4.0"));
        planner.Updates.Writer.TryWrite(new(null, 4 * 1048576));
        planner.Updates.Writer.TryWrite(new(1048576, null));
        await progressed;
        Assert.True(vm.HasDownloadProgress);
        Assert.Equal(0.25, vm.DownloadProgress);
        planner.State = Ready;
        planner.Updates.Writer.Complete();
        await download;
        Assert.True(vm.IsReady);
        Assert.True(vm.CanGenerate);
        Assert.False(vm.ShowModelSetup);
        Assert.True(vm.CanExplore);
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsDownloadingModel);
        Assert.Equal(1, planner.Downloads);
        Assert.Equal(3, planner.Checks);
        Assert.Equal(0, planner.Generations);
        Assert.Equal("Synthetic request", vm.Request);
        Assert.Contains("ready", vm.Status);
    }

    [Fact]
    public async Task DownloadableModelKeepsRandomDiscoveryAfterSetupSucceeds()
    {
        var start = DateTimeOffset.Parse("2026-11-10T09:00:00Z");
        var agenda = new ConferenceDocument
        {
            IsMock = true,
            Sessions =
            [
                new Session { Id = "synthetic-1", Title = "Synthetic session 1", StartsAt = start, EndsAt = start.AddMinutes(45) },
                new Session { Id = "synthetic-2", Title = "Synthetic session 2", StartsAt = start.AddHours(1), EndsAt = start.AddHours(1).AddMinutes(45) }
            ]
        };
        var planner = new SyntheticPlanner();
        var vm = Create(planner, agenda);

        await vm.InitializeAsync();
        Assert.True(vm.CanExplore);
        Assert.True(vm.CanDownloadModel);
        Assert.False(vm.IsAiMode);
        Assert.NotEmpty(vm.Sessions);
        Assert.Equal(0, planner.Downloads);

        var download = vm.DownloadModelCommand.ExecuteAsync(null);
        planner.State = Ready;
        planner.Updates.Writer.Complete();
        await download;

        Assert.False(vm.IsAiMode);
        Assert.True(vm.CanExplore);
        Assert.True(vm.CanSwitchToAi);
        Assert.True(vm.HasProposal);
        Assert.NotEmpty(vm.Sessions);
        vm.UseAiModeCommand.Execute(null);
        Assert.True(vm.IsAiMode);
        Assert.Empty(vm.Sessions);
    }

    [Fact]
    public async Task UnknownSizeNeverInventsAPercentage()
    {
        var planner = new SyntheticPlanner();
        var vm = Create(planner);
        await vm.InitializeAsync();
        var download = vm.DownloadModelCommand.ExecuteAsync(null);
        var progressed = When(vm, () => vm.DownloadProgressText.Contains("2.0 MB"));
        planner.Updates.Writer.TryWrite(new(2 * 1048576, null));
        await progressed;
        Assert.False(vm.HasDownloadProgress);
        Assert.Equal(0, vm.DownloadProgress);
        vm.Cancel();
        await download;
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task LeavingStopsWaitingAndARecheckCanDiscoverBackgroundCompletion()
    {
        var planner = new SyntheticPlanner();
        var vm = Create(planner);
        await vm.InitializeAsync();
        var download = vm.DownloadModelCommand.ExecuteAsync(null);
        vm.Cancel();
        await download;
        Assert.False(vm.IsReady);
        Assert.False(vm.IsBusy);
        Assert.True(vm.CanRetryModel);
        Assert.Contains("may continue", vm.Status);
        Assert.False(vm.HasDownloadProgress);
        planner.State = Ready;
        await vm.CheckModelCommand.ExecuteAsync(null);
        Assert.True(vm.IsReady);
        Assert.Equal(1, planner.Downloads);
    }

    [Fact]
    public async Task ReturningAfterStoppedReadinessKeepsTheReviewedFallbackUntilAnExplicitRecheck()
    {
        var start = DateTimeOffset.Parse("2026-11-10T09:00:00Z");
        var agenda = new ConferenceDocument
        {
            IsMock = true,
            Sessions =
            [
                new Session { Id = "synthetic-1", Title = "Synthetic session 1", StartsAt = start, EndsAt = start.AddMinutes(45) },
                new Session { Id = "synthetic-2", Title = "Synthetic session 2", StartsAt = start.AddHours(1), EndsAt = start.AddHours(1).AddMinutes(45) }
            ]
        };
        var planner = new SyntheticPlanner { State = Downloading };
        var vm = Create(planner, agenda);
        var initialCheck = vm.InitializeAsync();
        Assert.True(vm.IsDownloadingModel);
        vm.Cancel();
        await initialCheck;
        await vm.ExploreCommand.ExecuteAsync(null);
        var rows = vm.Sessions.ToArray();
        Assert.Equal(2, rows.Length);
        rows[0].IsSelected = false;
        var changes = new List<string?>();
        vm.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        var recheck = vm.CheckModelCommand.ExecuteAsync(null);
        Assert.True(vm.IsCheckingModel);
        Assert.True(vm.IsDownloadingModel);
        Assert.True(vm.CanExplore);
        Assert.True(vm.ShowModelSetup);
        Assert.Contains(nameof(PlannerViewModel.CanExplore), changes);
        vm.Cancel();
        await recheck;
        Assert.True(vm.CanExplore);
        Assert.Equal(vm.ModelStatus, vm.Status);
        Assert.Contains("Stopped waiting", vm.Status);
        Assert.Equal(rows, vm.Sessions);
        Assert.False(rows[0].IsSelected);

        var failingCheck = vm.CheckModelCommand.ExecuteAsync(null);
        Assert.True(vm.IsDownloadingModel);
        planner.PendingCheck = Task.FromException<PlannerModelState>(new InvalidOperationException("Synthetic readiness failure."));
        await failingCheck.WaitAsync(TimeSpan.FromSeconds(5));
        planner.PendingCheck = null;
        Assert.False(vm.IsDownloadingModel);
        Assert.Contains("isn't ready", vm.ModelStatus);
        Assert.Equal(vm.ModelStatus, vm.Status);
        Assert.Equal(rows, vm.Sessions);
        Assert.False(rows[0].IsSelected);

        planner.State = Ready;

        await vm.InitializeAsync();

        Assert.True(vm.HasProposal);
        Assert.False(vm.IsAiMode);
        Assert.False(vm.IsReady);
        Assert.True(vm.CanRetryModel);
        Assert.Equal(rows, vm.Sessions);
        Assert.False(rows[0].IsSelected);
        Assert.True(vm.CanApply);

        await vm.CheckModelCommand.ExecuteAsync(null);
        Assert.True(vm.IsReady);
        Assert.False(vm.IsAiMode);
        Assert.True(vm.CanSwitchToAi);
        Assert.Equal(rows, vm.Sessions);
    }

    [Fact]
    public async Task FailedDownloadExplainsRecoveryAndDoesNotEnablePlanning()
    {
        var planner = new SyntheticPlanner();
        var vm = Create(planner);
        await vm.InitializeAsync();
        var outcomes = new List<string>();
        vm.OutcomeAvailable += (_, message) => outcomes.Add(message);
        var download = vm.DownloadModelCommand.ExecuteAsync(null);
        planner.Updates.Writer.Complete(new IOException("Synthetic network failure"));
        await download;
        Assert.False(vm.IsReady);
        Assert.False(vm.IsBusy);
        Assert.True(vm.CanRetryModel);
        Assert.Contains("internet", vm.Status);
        Assert.Contains("storage", vm.Status);
        Assert.Contains("haven't changed", Assert.Single(outcomes));
        await vm.CheckModelCommand.ExecuteAsync(null);
        Assert.True(vm.CanDownloadModel);
        Assert.Equal(1, planner.Downloads);
    }

    [Fact]
    public async Task DownloadCompletionAloneDoesNotProveReadiness()
    {
        var planner = new SyntheticPlanner();
        var vm = Create(planner);
        await vm.InitializeAsync();
        var download = vm.DownloadModelCommand.ExecuteAsync(null);
        planner.State = new(false, false, "Synthetic model is unavailable.");
        planner.Updates.Writer.Complete();
        await download;
        Assert.False(vm.IsReady);
        Assert.False(vm.CanGenerate);
        Assert.False(vm.CanDownloadModel);
        Assert.True(vm.CanRetryModel);
        Assert.Equal("Synthetic model is unavailable.", vm.Status);
    }

    [Fact]
    public async Task AlreadyDownloadingWaitsWithoutStartingAnotherDownload()
    {
        var planner = new SyntheticPlanner { State = Downloading };
        var vm = Create(planner);
        var check = vm.InitializeAsync();
        Assert.True(vm.IsDownloadingModel);
        Assert.True(vm.CanOfferModelSetup);
        Assert.False(vm.CanDownloadModel);
        planner.State = Ready;
        await check.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsReady);
        Assert.Equal(0, planner.Downloads);
    }

    [Fact]
    public async Task StateChangeBeforeDownloadCannotStartAnUnnecessaryDownload()
    {
        var planner = new SyntheticPlanner();
        var vm = Create(planner);
        await vm.InitializeAsync();
        planner.State = Ready;
        await vm.DownloadModelCommand.ExecuteAsync(null);
        Assert.True(vm.IsReady);
        Assert.Equal(0, planner.Downloads);
    }

    [Fact]
    public async Task CancelledReadinessCheckRejectsLateSuccess()
    {
        var planner = new SyntheticPlanner();
        var vm = Create(planner);
        var pending = new TaskCompletionSource<PlannerModelState>(TaskCreationOptions.RunContinuationsAsynchronously);
        planner.PendingCheck = pending.Task;
        var check = vm.InitializeAsync();
        vm.Cancel();
        pending.SetResult(Ready);
        await check;
        Assert.False(vm.IsReady);
        Assert.True(vm.CanRetryModel);
        Assert.Equal(0, planner.Downloads);
    }

    [Fact]
    public async Task UnsupportedDevicesKeepManualSchedulingWithoutADownloadAction()
    {
        var planner = new SyntheticPlanner { State = new(false, false, "Synthetic unsupported device.") };
        var vm = Create(planner);
        await vm.InitializeAsync();
        Assert.False(vm.CanOfferModelSetup);
        Assert.False(vm.CanDownloadModel);
        Assert.True(vm.CanEdit);
        Assert.Equal("Synthetic unsupported device.", vm.ModelStatus);
        await vm.DownloadModelCommand.ExecuteAsync(null);
        Assert.Equal(0, planner.Downloads);
    }

    [Fact]
    public async Task ChoosingAnotherModelRechecksItWithoutStartingADownload()
    {
        var selection = new TestPlannerModelSelection { IsSupported = true };
        var planner = new SyntheticPlanner { State = Ready };
        var vm = Create(planner, selection: selection);
        await vm.InitializeAsync();
        vm.Request = "Synthetic request";
        var outcomes = new List<string>();
        vm.OutcomeAvailable += (_, message) => outcomes.Add(message);
        Assert.True(vm.ShowModelSelection);
        Assert.Contains("Standard", vm.CurrentModelText);

        vm.ToggleModelSelectionCommand.Execute(null);
        vm.SelectedModelIndex = 2;
        Assert.True(vm.CanUseSelectedModel);
        Assert.Null(selection.SelectedModelId);
        planner.State = DownloadRequired with { ModelId = "gemini-nano-preview-fast" };
        await vm.UseSelectedModelCommand.ExecuteAsync(null);

        Assert.Equal("gemini-nano-preview-fast", selection.SelectedModelId);
        Assert.Contains("Gemini Nano 4 Fast", vm.CurrentModelText);
        Assert.True(vm.CanDownloadModel);
        Assert.Equal("Synthetic request", vm.Request);
        Assert.Equal(0, planner.Downloads);
        Assert.False(vm.ShowModelChoices);
        Assert.Contains(outcomes, message => message.Contains("Gemini Nano 4 Fast") && message.Contains("Download"));
    }

    [Fact]
    public async Task ChangingModelsKeepsAnUnsavedDiscoveryPreviewWhileRechecking()
    {
        var start = DateTimeOffset.Parse("2026-11-10T09:00:00Z");
        var agenda = new ConferenceDocument
        {
            IsMock = true,
            Sessions =
            [
                new Session { Id = "synthetic-1", Title = "Synthetic session", StartsAt = start, EndsAt = start.AddMinutes(45) }
            ]
        };
        var selection = new TestPlannerModelSelection { IsSupported = true };
        var planner = new SyntheticPlanner();
        var vm = Create(planner, agenda, selection);
        await vm.InitializeAsync();
        Assert.True(vm.HasProposal);
        var existing = Assert.Single(vm.Sessions);

        vm.ToggleModelSelectionCommand.Execute(null);
        vm.SelectedModelIndex = 2;
        var pending = new TaskCompletionSource<PlannerModelState>(TaskCreationOptions.RunContinuationsAsynchronously);
        planner.PendingCheck = pending.Task;
        var change = vm.UseSelectedModelCommand.ExecuteAsync(null);

        Assert.True(vm.HasProposal);
        Assert.Same(existing, Assert.Single(vm.Sessions));
        Assert.True(existing.IsSelected);
        Assert.True(vm.IsCheckingModel);

        pending.SetResult(DownloadRequired with { ModelId = "gemini-nano-preview-fast" });
        await change;
        Assert.Equal(0, planner.Downloads);
    }

    [Fact]
    public async Task ChangingTheSharedModelInvalidatesOtherPlannerViews()
    {
        var selection = new TestPlannerModelSelection { IsSupported = true };
        var firstPlanner = new SyntheticPlanner { State = Ready };
        var secondPlanner = new SyntheticPlanner { State = Ready };
        var first = Create(firstPlanner, selection: selection);
        var second = Create(secondPlanner, selection: selection);
        await first.InitializeAsync();
        await second.InitializeAsync();
        second.Request = "Synthetic request";
        Assert.True(second.CanGenerate);
        var modelChanged = false;
        second.PropertyChanged += (_, args) => modelChanged |= args.PropertyName == nameof(second.CurrentModelText);

        first.ToggleModelSelectionCommand.Execute(null);
        first.SelectedModelIndex = 2;
        firstPlanner.State = DownloadRequired with { ModelId = "gemini-nano-preview-fast" };
        await first.UseSelectedModelCommand.ExecuteAsync(null);

        Assert.False(second.IsReady);
        Assert.False(second.CanGenerate);
        Assert.True(second.CanRetryModel);
        Assert.Contains("Gemini Nano 4 Fast", second.CurrentModelText);
        Assert.True(modelChanged);
    }

    [Fact]
    public async Task ASharedModelChangeDiscardsAnOlderPendingReadinessCheck()
    {
        var selection = new TestPlannerModelSelection { IsSupported = true };
        var firstPlanner = new SyntheticPlanner { State = Ready };
        var secondPlanner = new SyntheticPlanner { State = Ready };
        var first = Create(firstPlanner, selection: selection);
        var second = Create(secondPlanner, selection: selection);
        await first.InitializeAsync();
        await second.InitializeAsync();
        var pending = new TaskCompletionSource<PlannerModelState>(TaskCreationOptions.RunContinuationsAsynchronously);
        secondPlanner.PendingCheck = pending.Task;
        var check = second.CheckModelCommand.ExecuteAsync(null);
        Assert.True(second.IsCheckingModel);

        first.ToggleModelSelectionCommand.Execute(null);
        first.SelectedModelIndex = 2;
        firstPlanner.State = DownloadRequired with { ModelId = "gemini-nano-preview-fast" };
        await first.UseSelectedModelCommand.ExecuteAsync(null);
        await check.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(second.IsReady);
        Assert.True(second.CanRetryModel);
        Assert.Contains("model changed", second.ModelStatus);
        pending.SetResult(Ready);
    }

    [Fact]
    public async Task ReturningToARetainedPlannerRechecksTheChangedModel()
    {
        var selection = new TestPlannerModelSelection { IsSupported = true };
        var firstPlanner = new SyntheticPlanner { State = Ready };
        var secondPlanner = new SyntheticPlanner { State = Ready };
        var first = Create(firstPlanner, selection: selection);
        var second = Create(secondPlanner, selection: selection);
        await first.InitializeAsync();
        first.Cancel();
        await second.InitializeAsync();
        var checks = firstPlanner.Checks;

        second.ToggleModelSelectionCommand.Execute(null);
        second.SelectedModelIndex = 2;
        secondPlanner.State = DownloadRequired with { ModelId = "gemini-nano-preview-fast" };
        await second.UseSelectedModelCommand.ExecuteAsync(null);
        firstPlanner.State = DownloadRequired with { ModelId = "gemini-nano-preview-fast" };
        await first.InitializeAsync();

        Assert.Equal(checks + 1, firstPlanner.Checks);
        Assert.False(first.IsReady);
        Assert.True(first.CanDownloadModel);
        Assert.Equal(0, firstPlanner.Downloads);
    }

    [Fact]
    public async Task ReturningAfterACancelledCheckRetriesTheChangedModel()
    {
        var selection = new TestPlannerModelSelection { IsSupported = true };
        var planner = new SyntheticPlanner { State = Ready };
        var vm = Create(planner, selection: selection);
        await vm.InitializeAsync();
        selection.Select("gemini-nano-preview-fast");
        var pending = new TaskCompletionSource<PlannerModelState>(TaskCreationOptions.RunContinuationsAsynchronously);
        planner.PendingCheck = pending.Task;
        var check = vm.InitializeAsync();
        vm.Cancel();
        await check.WaitAsync(TimeSpan.FromSeconds(5));
        planner.PendingCheck = null;
        planner.State = DownloadRequired with { ModelId = "gemini-nano-preview-fast" };
        var checks = planner.Checks;

        await vm.InitializeAsync();

        Assert.Equal(checks + 1, planner.Checks);
        Assert.True(vm.CanDownloadModel);
        pending.SetResult(Ready);
    }

    [Fact]
    public async Task ReturningWhileACancelledCheckIsFinishingWaitsAndRechecks()
    {
        var selection = new TestPlannerModelSelection { IsSupported = true };
        var planner = new SyntheticPlanner { State = Ready };
        var vm = Create(planner, selection: selection);
        await vm.InitializeAsync();
        selection.Select("gemini-nano-preview-fast");
        var pending = new TaskCompletionSource<PlannerModelState>(TaskCreationOptions.RunContinuationsAsynchronously);
        planner.PendingCheck = pending.Task;
        var oldCheck = vm.InitializeAsync();
        vm.Cancel();
        planner.PendingCheck = null;
        planner.State = DownloadRequired with { ModelId = "gemini-nano-preview-fast" };
        var checks = planner.Checks;

        await Task.WhenAll(oldCheck, vm.InitializeAsync()).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(checks + 1, planner.Checks);
        Assert.True(vm.CanDownloadModel);
        pending.SetResult(Ready);
    }

    [Fact]
    public async Task ASharedModelChangeKeepsAnotherViewsUnsavedPreview()
    {
        var start = DateTimeOffset.Parse("2026-11-10T09:00:00Z");
        var agenda = new ConferenceDocument
        {
            IsMock = true,
            Sessions =
            [
                new Session { Id = "synthetic-1", Title = "Synthetic session", StartsAt = start, EndsAt = start.AddMinutes(45) }
            ]
        };
        var selection = new TestPlannerModelSelection { IsSupported = true };
        var firstPlanner = new SyntheticPlanner { State = Ready };
        var secondPlanner = new SyntheticPlanner();
        var first = Create(firstPlanner, selection: selection);
        var second = Create(secondPlanner, agenda, selection);
        await first.InitializeAsync();
        await second.InitializeAsync();
        Assert.True(second.HasProposal);

        first.ToggleModelSelectionCommand.Execute(null);
        first.SelectedModelIndex = 2;
        await first.UseSelectedModelCommand.ExecuteAsync(null);

        Assert.True(second.HasProposal);
        Assert.True(Assert.Single(second.Sessions).IsSelected);
        Assert.True(second.CanRetryModel);
    }

    [Fact]
    public async Task ChangingBackDoesNotRestoreTheFirstModelsPendingResult()
    {
        var selection = new TestPlannerModelSelection { IsSupported = true };
        var firstPlanner = new SyntheticPlanner { State = Ready };
        var secondPlanner = new SyntheticPlanner { State = Ready };
        var first = Create(firstPlanner, selection: selection);
        var second = Create(secondPlanner, selection: selection);
        await first.InitializeAsync();
        await second.InitializeAsync();
        var pending = new TaskCompletionSource<PlannerModelState>(TaskCreationOptions.RunContinuationsAsynchronously);
        secondPlanner.PendingCheck = pending.Task;
        var oldCheck = second.CheckModelCommand.ExecuteAsync(null);

        first.ToggleModelSelectionCommand.Execute(null);
        first.SelectedModelIndex = 2;
        await first.UseSelectedModelCommand.ExecuteAsync(null);
        first.ToggleModelSelectionCommand.Execute(null);
        first.SelectedModelIndex = 0;
        await first.UseSelectedModelCommand.ExecuteAsync(null);
        await oldCheck.WaitAsync(TimeSpan.FromSeconds(5));
        secondPlanner.PendingCheck = null;
        await second.InitializeAsync();
        Assert.True(second.IsReady);

        pending.SetResult(DownloadRequired);
        Assert.True(second.IsReady);
        Assert.False(second.CanDownloadModel);
    }

    [Fact]
    public async Task DownloadUsesTheProfileFromTheReadinessSnapshot()
    {
        var selection = new TestPlannerModelSelection { IsSupported = true };
        selection.Select("gemini-nano-preview-fast");
        var planner = new SyntheticPlanner
        {
            State = DownloadRequired with { ModelId = "gemini-nano-preview-fast" }
        };
        var vm = Create(planner, selection: selection);
        await vm.InitializeAsync();
        var download = vm.DownloadModelCommand.ExecuteAsync(null);
        Assert.False(vm.CanChangeModel);
        selection.Select(null);
        planner.State = Ready;
        planner.Updates.Writer.Complete();
        await download;

        Assert.Equal("gemini-nano-preview-fast", planner.DownloadedModelId);
        Assert.Equal(1, planner.Downloads);
        Assert.False(vm.IsReady);
        Assert.Contains("model changed", vm.ModelStatus);
    }

    [Fact]
    public async Task FailedModelSelectionKeepsTheCurrentModelAndShowsRecovery()
    {
        var selection = new TestPlannerModelSelection
        {
            IsSupported = true,
            SelectionFailure = new IOException("Synthetic preference failure")
        };
        var planner = new SyntheticPlanner { State = Ready };
        var vm = Create(planner, selection: selection);
        await vm.InitializeAsync();
        vm.ToggleModelSelectionCommand.Execute(null);
        vm.SelectedModelIndex = 2;
        var checks = planner.Checks;

        await vm.UseSelectedModelCommand.ExecuteAsync(null);

        Assert.Null(selection.SelectedModelId);
        Assert.True(vm.IsReady);
        Assert.True(vm.ShowModelChoices);
        Assert.Contains("try again", vm.ModelSelectionError);
        Assert.Equal(checks, planner.Checks);
        Assert.Equal(0, planner.Downloads);
    }

    [Fact]
    public async Task ModelChoiceIsHiddenOnOtherPlatforms()
    {
        var vm = Create(new SyntheticPlanner { State = Ready });
        await vm.InitializeAsync();
        Assert.False(vm.ShowModelSelection);
        Assert.False(vm.CanChangeModel);
    }

    private sealed class SyntheticPlanner : IOnDevicePlanner
    {
        public PlannerModelState State { get; set; } = DownloadRequired;
        public Task<PlannerModelState>? PendingCheck { get; set; }
        public Channel<PlannerModelDownload> Updates { get; } = Channel.CreateUnbounded<PlannerModelDownload>();
        public int Downloads { get; private set; }
        public string? DownloadedModelId { get; private set; }
        public int Checks { get; private set; }
        public int Generations { get; private set; }
        public string? UnsupportedReason => null;
        public Task<PlannerModelState> CheckReadinessAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Checks++;
            return PendingCheck ?? Task.FromResult(State);
        }
        public async IAsyncEnumerable<PlannerModelDownload> DownloadModelAsync(string? modelId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Downloads++;
            DownloadedModelId = modelId;
            await foreach (var update in Updates.Reader.ReadAllAsync(cancellationToken))
                yield return update;
        }
        public Task<IReadOnlyList<PlannerStartingPoint>> SuggestStartingPointsAsync(
            ConferenceDocument conference, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlannerStartingPoint>>([]);
        public Task<PlannerResult> ProposeAsync(ConferenceDocument conference, string request,
            TimeZoneInfo zone, string[] savedIds, CancellationToken cancellationToken, bool fillGaps = false,
            PlannerWatchingHours? watchingHours = null)
        {
            Generations++;
            throw new NotSupportedException("Synthetic planner does not generate proposals.");
        }
    }
}
