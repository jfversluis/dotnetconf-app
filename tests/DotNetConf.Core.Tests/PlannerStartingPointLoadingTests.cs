using DotNetConf.App.Services;
using DotNetConf.App.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DotNetConf.Core.Tests;

public sealed class PlannerStartingPointLoadingTests
{
    private static ConferenceDocument SyntheticAgenda => new()
    {
        Sessions = [new() { Id = "synthetic-session", Topics = ["Synthetic AI", "Synthetic Web", "Synthetic Mobile"] }]
    };

    private static PlannerViewModel Create(IConferenceDataSource data, IOnDevicePlanner planner,
        ILogger<PlannerViewModel>? logger = null)
    {
        planner.UnsupportedReason.Returns((string?)null);
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(new PlannerModelState(true, false, ""));
        var store = Substitute.For<IScheduleStore>();
        store.ReadAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        return new(data, new ScheduleService(store), planner, Substitute.For<IPlannerScheduleUpdates>(),
            logger ?? NullLogger<PlannerViewModel>.Instance, new TestPlannerModelSelection());
    }

    private static Task RefreshFinished(PlannerViewModel vm)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.IsLoadingStartingPoints) && !vm.IsLoadingStartingPoints)
                finished.TrySetResult();
        };
        return finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void InitialStartingPointsShowOnlyTheLoadingState()
    {
        var vm = Create(Substitute.For<IConferenceDataSource>(), Substitute.For<IOnDevicePlanner>());
        Assert.True(vm.IsLoadingStartingPoints);
        Assert.False(vm.ShowStartingPoints);
    }

    [Fact]
    public async Task EditingTheRequestKeepsStartingPointsHiddenUntilTheBackgroundLoadSucceeds()
    {
        var data = Substitute.For<IConferenceDataSource>();
        var pending = new TaskCompletionSource<IReadOnlyList<PlannerStartingPoint>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        var planner = Substitute.For<IOnDevicePlanner>();
        var agenda = SyntheticAgenda;
        data.LoadAsync(Arg.Any<CancellationToken>()).Returns(agenda);
        var suggestions = PlannerStartingPoints.Ground(agenda, ["Synthetic AI", "Synthetic Web", "Synthetic Mobile"]);
        planner.SuggestStartingPointsAsync(agenda, Arg.Any<CancellationToken>()).Returns(call =>
        {
            token = call.Arg<CancellationToken>();
            return pending.Task;
        });
        var vm = Create(data, planner);
        var finished = RefreshFinished(vm);
        await vm.InitializeAsync();
        Assert.True(vm.IsLoadingStartingPoints);
        Assert.False(vm.ShowStartingPoints);

        const string request = "Synthetic request typed while starting points load.";
        vm.Request = request;
        Assert.False(token.IsCancellationRequested);
        pending.SetResult(suggestions);
        await finished;
        Assert.Same(suggestions, vm.StartingPoints);
        Assert.Equal(request, vm.Request);
        Assert.True(vm.ShowStartingPoints);
        Assert.False(vm.IsLoadingStartingPoints);
    }

    [Fact]
    public async Task FailedSuggestionsRevealFallbacksAndLogTheFailureWithoutAPlanningError()
    {
        var data = Substitute.For<IConferenceDataSource>();
        data.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ConferenceDocument>(new IOException("Synthetic suggestion failure")));
        var logger = Substitute.For<ILogger<PlannerViewModel>>();
        var vm = Create(data, Substitute.For<IOnDevicePlanner>(), logger);
        var fallback = vm.StartingPoints;
        Assert.False(vm.ShowStartingPoints);
        var finished = RefreshFinished(vm);
        await vm.InitializeAsync();
        await finished;
        Assert.Same(fallback, vm.StartingPoints);
        Assert.True(vm.ShowStartingPoints);
        Assert.False(vm.HasStatus);
        Assert.Contains(logger.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(ILogger.Log)
            && Equals(call.GetArguments()[0], LogLevel.Warning));
    }

    [Fact]
    public async Task LeavingStopsTheSpinnerWithoutWaitingForANonCooperativeModel()
    {
        var data = Substitute.For<IConferenceDataSource>();
        var agenda = SyntheticAgenda;
        data.LoadAsync(Arg.Any<CancellationToken>()).Returns(agenda);
        var planner = Substitute.For<IOnDevicePlanner>();
        var pending = new TaskCompletionSource<IReadOnlyList<PlannerStartingPoint>>(TaskCreationOptions.RunContinuationsAsynchronously);
        planner.SuggestStartingPointsAsync(agenda, Arg.Any<CancellationToken>()).Returns(pending.Task);
        var vm = Create(data, planner);
        var fallback = vm.StartingPoints;
        var finished = RefreshFinished(vm);
        await vm.InitializeAsync();
        vm.Cancel();
        await finished;
        Assert.Same(fallback, vm.StartingPoints);
        Assert.False(vm.IsLoadingStartingPoints);
        Assert.False(pending.Task.IsCompleted);

        pending.SetResult(PlannerStartingPoints.Ground(agenda, ["Synthetic AI", "Synthetic Web", "Synthetic Mobile"]));
        await Task.Yield();
        Assert.Same(fallback, vm.StartingPoints);
    }

    [Fact]
    public async Task AnEmptyModelResponseCannotRemoveTheFallbacks()
    {
        var data = Substitute.For<IConferenceDataSource>();
        var agenda = SyntheticAgenda;
        data.LoadAsync(Arg.Any<CancellationToken>()).Returns(agenda);
        var planner = Substitute.For<IOnDevicePlanner>();
        planner.SuggestStartingPointsAsync(agenda, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PlannerStartingPoint>());
        var vm = Create(data, planner);
        var fallback = vm.StartingPoints;
        var finished = RefreshFinished(vm);
        await vm.InitializeAsync();
        await finished;
        Assert.Same(fallback, vm.StartingPoints);
        Assert.False(vm.HasStatus);
        Assert.True(vm.ShowStartingPoints);
    }

    [Fact]
    public async Task RefreshHidesExistingStartingPointsUntilReplacementFinishes()
    {
        var data = Substitute.For<IConferenceDataSource>();
        var agenda = SyntheticAgenda;
        data.LoadAsync(Arg.Any<CancellationToken>()).Returns(agenda);
        var planner = Substitute.For<IOnDevicePlanner>();
        var first = PlannerStartingPoints.Ground(agenda, ["Synthetic AI", "Synthetic Web", "Synthetic Mobile"]);
        var replacement = new TaskCompletionSource<IReadOnlyList<PlannerStartingPoint>>(TaskCreationOptions.RunContinuationsAsynchronously);
        planner.SuggestStartingPointsAsync(agenda, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(first), replacement.Task);
        var vm = Create(data, planner);
        var firstFinished = RefreshFinished(vm);
        await vm.InitializeAsync();
        await firstFinished;
        Assert.True(vm.ShowStartingPoints);
        await vm.ExploreCommand.ExecuteAsync(null);

        var refreshFinished = RefreshFinished(vm);
        vm.UseAiModeCommand.Execute(null);

        Assert.True(vm.IsLoadingStartingPoints);
        Assert.False(vm.ShowStartingPoints);
        var refreshed = PlannerStartingPoints.Ground(agenda, ["Synthetic Mobile", "Synthetic AI", "Synthetic Web"]);
        replacement.SetResult(refreshed);
        await refreshFinished;
        Assert.True(vm.ShowStartingPoints);
        Assert.Same(refreshed, vm.StartingPoints);
    }

    [Fact]
    public async Task FailedRefreshAfterSuccessRevealsGenericFallbacksInsteadOfOldSuggestions()
    {
        var data = Substitute.For<IConferenceDataSource>();
        var agenda = SyntheticAgenda;
        data.LoadAsync(Arg.Any<CancellationToken>()).Returns(agenda);
        var planner = Substitute.For<IOnDevicePlanner>();
        var first = PlannerStartingPoints.Ground(agenda, ["Synthetic AI", "Synthetic Web", "Synthetic Mobile"]);
        var replacement = new TaskCompletionSource<IReadOnlyList<PlannerStartingPoint>>(TaskCreationOptions.RunContinuationsAsynchronously);
        planner.SuggestStartingPointsAsync(agenda, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(first), replacement.Task);
        var vm = Create(data, planner);
        var fallback = vm.StartingPoints;
        var firstFinished = RefreshFinished(vm);
        await vm.InitializeAsync();
        await firstFinished;
        Assert.Same(first, vm.StartingPoints);
        await vm.ExploreCommand.ExecuteAsync(null);

        var refreshFinished = RefreshFinished(vm);
        vm.UseAiModeCommand.Execute(null);
        Assert.False(vm.ShowStartingPoints);
        replacement.SetException(new IOException("Synthetic suggestion refresh failure"));
        await refreshFinished;

        Assert.False(vm.IsLoadingStartingPoints);
        Assert.True(vm.ShowStartingPoints);
        Assert.Same(fallback, vm.StartingPoints);
    }

    [Fact]
    public async Task ReadinessFinishingAfterExplorationDoesNotLeaveAStartingPointSpinnerRunning()
    {
        var data = Substitute.For<IConferenceDataSource>();
        data.LoadAsync(Arg.Any<CancellationToken>()).Returns(SyntheticAgenda);
        var planner = Substitute.For<IOnDevicePlanner>();
        var vm = Create(data, planner);
        var readiness = new TaskCompletionSource<PlannerModelState>(TaskCreationOptions.RunContinuationsAsynchronously);
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(readiness.Task);
        var initialization = vm.InitializeAsync();
        Assert.True(vm.IsLoadingStartingPoints);

        await vm.ExploreCommand.ExecuteAsync(null);
        readiness.SetResult(new PlannerModelState(true, false, ""));
        await initialization;

        Assert.False(vm.IsLoadingStartingPoints);
        Assert.False(vm.IsAiMode);
        await planner.DidNotReceive().SuggestStartingPointsAsync(Arg.Any<ConferenceDocument>(), Arg.Any<CancellationToken>());
    }
}
