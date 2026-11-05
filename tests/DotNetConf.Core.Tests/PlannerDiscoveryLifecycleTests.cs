using DotNetConf.App.Services;
using DotNetConf.App.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DotNetConf.Core.Tests;

public sealed class PlannerDiscoveryLifecycleTests
{
    private static PlannerModelState Ready => new(true, false, "");

    private static readonly ConferenceDocument Agenda = new()
    {
        IsMock = true,
        Sessions = Enumerable.Range(0, 18).Select(index => new Session
        {
            Id = $"synthetic-{index}", Title = $"Synthetic test session {index}",
            StartsAt = DateTimeOffset.Parse("2026-11-10T09:00:00Z").AddHours(index),
            EndsAt = DateTimeOffset.Parse("2026-11-10T09:45:00Z").AddHours(index),
            Topics = [$"Synthetic topic {index % 4}"]
        }).ToList()
    };

    private static (PlannerViewModel Vm, IOnDevicePlanner Planner, MemoryStore Store) Create(bool supported = false)
    {
        var source = Substitute.For<IConferenceDataSource>();
        source.LoadAsync(Arg.Any<CancellationToken>()).Returns(Agenda);
        var planner = Substitute.For<IOnDevicePlanner>();
        planner.UnsupportedReason.Returns(supported ? null : "No local model on this device.");
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(Ready);
        planner.SuggestStartingPointsAsync(Arg.Any<ConferenceDocument>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PlannerStartingPoint>>([]));
        var updates = Substitute.For<IPlannerScheduleUpdates>();
        updates.RefreshRemindersAsync().Returns(true);
        var store = new MemoryStore();
        return (new(source, new ScheduleService(store), planner, updates,
            NullLogger<PlannerViewModel>.Instance, new TestPlannerModelSelection()), planner, store);
    }

    [Fact]
    public async Task UnsupportedDevicesCanReviewAndSaveWithoutAiOrExtraConfirmation()
    {
        var (vm, planner, store) = Create();
        await vm.InitializeAsync();
        Assert.False(vm.IsReady);
        Assert.False(vm.IsAiMode);
        Assert.True(vm.CanExplore);
        Assert.False(vm.ShowModelSetup);
        Assert.True(vm.ShowAvailabilityStatus);
        Assert.Contains("explore the agenda", vm.ModelStatus, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(vm.SelectedCount, 1, 6);
        Assert.True(vm.CanApply);
        Assert.False(vm.ConstraintsConfirmed);
        Assert.Empty(store.Saved);
        var selected = vm.Sessions.Where(session => session.IsSelected).Select(session => session.Id).Order().ToArray();
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.Equal(selected, store.Saved.Order().ToArray());
        Assert.True(vm.HasAddedSessions);
        await planner.DidNotReceive().ProposeAsync(Arg.Any<ConferenceDocument>(), Arg.Any<string>(),
            Arg.Any<TimeZoneInfo>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(),
            Arg.Any<PlannerWatchingHours>());
    }

    [Fact]
    public async Task UnavailableModelUsesActionableCopyWithoutPlatformDetails()
    {
        var (vm, planner, _) = Create(true);
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PlannerModelState>(new NotSupportedException("Unknown: UNAVAILABLE (0): model is not ready.")));

        await vm.InitializeAsync();

        Assert.Equal(
            "On-device AI isn't ready on this device. Explore a mix from the downloaded agenda, or check your device's AI settings and retry. No model download starts automatically.",
            vm.ModelStatus);
        Assert.DoesNotContain("UNAVAILABLE", vm.ModelStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadinessChoosesAiWithoutCreatingARandomPreview()
    {
        var (vm, planner, _) = Create(true);
        var ready = new TaskCompletionSource<PlannerModelState>(TaskCreationOptions.RunContinuationsAsynchronously);
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(ready.Task);
        var initialization = vm.InitializeAsync();
        Assert.True(vm.IsCheckingModel);
        Assert.False(vm.IsBusy);
        Assert.False(vm.CanExplore);
        Assert.False(vm.IsAiMode);
        Assert.Empty(vm.Sessions);
        ready.SetResult(Ready);
        await initialization;
        Assert.True(vm.IsReady);
        Assert.True(vm.IsAiMode);
        Assert.False(vm.CanExplore);
        Assert.Empty(vm.Sessions);
        Assert.False(vm.HasProposal);
    }

    [Fact]
    public async Task ReadyAiCannotBeReplacedWithRandomSuggestions()
    {
        var (vm, _, store) = Create(true);
        await vm.InitializeAsync();
        await vm.ExploreCommand.ExecuteAsync(null);
        Assert.True(vm.IsAiMode);
        Assert.False(vm.CanExplore);
        Assert.Empty(vm.Sessions);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task SuccessfulRetrySwitchesFromRandomPreviewToAiOnly()
    {
        var (vm, planner, store) = Create(true);
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PlannerModelState>(new NotSupportedException("Synthetic unavailable model")));
        await vm.InitializeAsync();
        Assert.True(vm.CanExplore);
        Assert.NotEmpty(vm.Sessions);

        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(Ready);
        await vm.CheckModelCommand.ExecuteAsync(null);

        Assert.True(vm.IsAiMode);
        Assert.False(vm.CanExplore);
        Assert.Empty(vm.Sessions);
        Assert.False(vm.HasProposal);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task FailedRetryKeepsTheReviewedRandomPreview()
    {
        var (vm, planner, store) = Create(true);
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PlannerModelState>(new NotSupportedException("Synthetic unavailable model")));
        await vm.InitializeAsync();
        var ids = vm.Sessions.Select(session => session.Id).ToArray();
        vm.Sessions[0].IsSelected = false;

        var pending = new TaskCompletionSource<PlannerModelState>(TaskCreationOptions.RunContinuationsAsynchronously);
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(pending.Task);
        var retry = vm.CheckModelCommand.ExecuteAsync(null);
        Assert.False(vm.CanApply);
        Assert.False(vm.CanEdit);
        Assert.False(vm.CanExplore);
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.Empty(store.Saved);
        pending.SetException(new NotSupportedException("Synthetic unavailable model"));
        await retry;

        Assert.False(vm.IsAiMode);
        Assert.True(vm.CanExplore);
        Assert.True(vm.CanApply);
        Assert.Equal(ids, vm.Sessions.Select(session => session.Id));
        Assert.False(vm.Sessions[0].IsSelected);
    }

    [Fact]
    public async Task ReadinessTimeoutFallsBackToRandomDiscovery()
    {
        var (vm, planner, store) = Create(true);
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PlannerModelState>(new OperationCanceledException()));

        await vm.InitializeAsync();

        Assert.False(vm.IsReady);
        Assert.False(vm.IsAiMode);
        Assert.True(vm.CanExplore);
        Assert.Contains("timed out", vm.ModelStatus);
        Assert.NotEmpty(vm.Sessions);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task LostModelReadinessReplacesAiPreviewWithRandomDiscovery()
    {
        var (vm, planner, store) = Create(true);
        planner.ProposeAsync(Arg.Any<ConferenceDocument>(), Arg.Any<string>(), Arg.Any<TimeZoneInfo>(),
            Arg.Any<string[]>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<PlannerWatchingHours>())
            .Returns(new PlannerResult(new(["synthetic-0"], [], []), ""));
        await vm.InitializeAsync();
        vm.Request = "Synthetic request";
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.True(vm.IsAiMode);
        Assert.Single(vm.Sessions);
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PlannerModelState>(new NotSupportedException("Synthetic model no longer available")));

        await vm.CheckModelCommand.ExecuteAsync(null);

        Assert.False(vm.IsAiMode);
        Assert.True(vm.CanExplore);
        Assert.InRange(vm.Sessions.Count, 2, 6);
        Assert.Contains("no AI", vm.DiscoverySource);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task LeavingDuringReadinessDoesNotStartDiscovery()
    {
        var (vm, planner, store) = Create(true);
        var ready = new TaskCompletionSource<PlannerModelState>(TaskCreationOptions.RunContinuationsAsynchronously);
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(ready.Task);
        var initialization = vm.InitializeAsync();
        vm.Cancel();
        ready.SetResult(Ready);
        await initialization;
        Assert.False(vm.IsReady);
        Assert.Empty(vm.Sessions);
        Assert.False(vm.HasProposal);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task CancelledReadinessCanBeRetried()
    {
        var (vm, planner, _) = Create(true);
        var ready = new TaskCompletionSource<PlannerModelState>(TaskCreationOptions.RunContinuationsAsynchronously);
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(ready.Task);
        var initialization = vm.InitializeAsync();
        vm.Cancel();
        ready.SetResult(Ready);
        await initialization;
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(Ready);

        await vm.CheckModelCommand.ExecuteAsync(null);

        Assert.True(vm.IsReady);
        Assert.True(vm.IsAiMode);
        Assert.False(vm.CanExplore);
        Assert.Empty(vm.Sessions);
    }

    [Fact]
    public async Task EmptySelectionHasAnActionableMessageAndShuffleNeverWrites()
    {
        var (vm, _, store) = Create();
        await vm.InitializeAsync();
        foreach (var session in vm.Sessions)
            session.IsSelected = false;
        Assert.False(vm.CanApply);
        Assert.Equal(0, vm.SelectedCount);
        Assert.Contains("Select at least one", vm.Validation);
        Assert.DoesNotContain("12", vm.Validation);
        await vm.ExploreCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.Sessions);
        Assert.Empty(store.Saved);
        Assert.All(vm.Sessions, session => Assert.Contains(Agenda.Sessions, item => item.Id == session.Id));
    }

    [Fact]
    public async Task APreviewLargerThanTwelveCanBeSavedAfterReview()
    {
        var (vm, planner, store) = Create(true);
        planner.ProposeAsync(Arg.Any<ConferenceDocument>(), Arg.Any<string>(), Arg.Any<TimeZoneInfo>(),
            Arg.Any<string[]>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<PlannerWatchingHours>())
            .Returns(new PlannerResult(new(Agenda.Sessions.Select(session => session.Id).ToArray(), [], []), ""));
        await vm.InitializeAsync();
        vm.Request = "Synthetic test request";
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.Equal(18, vm.SelectedCount);
        Assert.False(vm.CanApply);
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.Empty(store.Saved);
        vm.ConstraintsConfirmed = true;
        Assert.True(vm.CanApply);
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.Equal(18, store.Saved.Length);
    }

    private sealed class MemoryStore : IScheduleStore
    {
        public string[] Saved { get; private set; } = [];
        public Task<IReadOnlyCollection<string>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<string>>(Saved);
        public Task WriteAsync(IReadOnlyCollection<string> sessionIds, CancellationToken cancellationToken = default)
        {
            Saved = sessionIds.ToArray();
            return Task.CompletedTask;
        }
    }
}
