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
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var store = Substitute.For<IScheduleStore>();
        store.ReadAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        return new(data, new ScheduleService(store), planner, Substitute.For<IPlannerScheduleUpdates>(),
            logger ?? NullLogger<PlannerViewModel>.Instance);
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
    public void FallbackStartingPointsAreVisibleImmediately()
    {
        var vm = Create(Substitute.For<IConferenceDataSource>(), Substitute.For<IOnDevicePlanner>());
        Assert.Equal(["Feed my AI + mobile curiosity", "Web, hold the boring bits", "Down the .NET rabbit hole"],
            vm.StartingPoints.Select(point => point.Label));
    }

    [Fact]
    public async Task EditingTheRequestKeepsFallbacksVisibleAndDoesNotCancelBackgroundSuggestions()
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
        Assert.NotEmpty(vm.StartingPoints);

        var request = vm.StartingPoints[0].Request;
        vm.UseExampleCommand.Execute(request);
        Assert.False(token.IsCancellationRequested);
        pending.SetResult(suggestions);
        await finished;
        Assert.Same(suggestions, vm.StartingPoints);
        Assert.Equal(request, vm.Request);
        Assert.NotEmpty(vm.StartingPoints);
    }

    [Fact]
    public async Task FailedSuggestionsKeepFallbacksAndLogTheFailureWithoutAPlanningError()
    {
        var data = Substitute.For<IConferenceDataSource>();
        data.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ConferenceDocument>(new IOException("Synthetic suggestion failure")));
        var logger = Substitute.For<ILogger<PlannerViewModel>>();
        var vm = Create(data, Substitute.For<IOnDevicePlanner>(), logger);
        var fallback = vm.StartingPoints;
        var finished = RefreshFinished(vm);
        await vm.InitializeAsync();
        await finished;
        Assert.Same(fallback, vm.StartingPoints);
        Assert.False(vm.HasStatus);
        Assert.Contains(logger.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(ILogger.Log)
            && Equals(call.GetArguments()[0], LogLevel.Warning));
    }

    [Fact]
    public async Task LeavingRejectsLateSuggestionsAndKeepsFallbacks()
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
        pending.SetResult(PlannerStartingPoints.Ground(agenda, ["Synthetic AI", "Synthetic Web", "Synthetic Mobile"]));
        await finished;
        Assert.Same(fallback, vm.StartingPoints);
        Assert.False(vm.IsLoadingStartingPoints);
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
    }
}
