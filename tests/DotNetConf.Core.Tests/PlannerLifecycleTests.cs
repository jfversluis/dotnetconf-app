using DotNetConf.App.Services;
using DotNetConf.App.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DotNetConf.Core.Tests;

public sealed class PlannerLifecycleTests
{
    private static async Task<(PlannerViewModel ViewModel, IOnDevicePlanner Planner,
        IPlannerScheduleUpdates Updates, DelayedStore Store)> CreateAsync()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "conference.mock.json"));
        var document = await ConferenceJson.ReadAsync(stream);
        var data = Substitute.For<IConferenceDataSource>();
        data.LoadAsync(Arg.Any<CancellationToken>()).Returns(document);
        var planner = Substitute.For<IOnDevicePlanner>();
        planner.UnsupportedReason.Returns((string?)null);
        planner.CheckReadinessAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        planner.SuggestStartingPointsAsync(document, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PlannerStartingPoint>>([]));
        planner.ProposeAsync(document, Arg.Any<string>(), Arg.Any<TimeZoneInfo>(),
                Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(new PlannerResult(new(["building-with-maui"], [], []), ""));
        var updates = Substitute.For<IPlannerScheduleUpdates>();
        updates.RefreshRemindersAsync().Returns(true);
        var store = new DelayedStore();
        var vm = new PlannerViewModel(data, new ScheduleService(store), planner, updates,
            NullLogger<PlannerViewModel>.Instance);
        await vm.InitializeAsync();
        vm.Request = "Mobile";
        await vm.GenerateCommand.ExecuteAsync(null);
        return (vm, planner, updates, store);
    }

    [Fact]
    public async Task SaveFreezesSelectionAndPersistsOnlyTheConfirmedSnapshot()
    {
        var (vm, _, updates, store) = await CreateAsync();
        var session = Assert.Single(vm.Sessions);
        vm.ConstraintsConfirmed = true;
        var outcomes = new List<string>();
        vm.OutcomeAvailable += (_, message) => outcomes.Add(message);
        store.Delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = vm.ApplyCommand.ExecuteAsync(null);
        await store.Started.Task;
        Assert.True(vm.IsBusy);
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
    public async Task FailedWriteKeepsThePreviewAndOriginalLikesAndAnnouncesFailureOnce()
    {
        var (vm, _, _, store) = await CreateAsync();
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
        var (vm, _, updates, store) = await CreateAsync();
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
        var (vm, planner, _, _) = await CreateAsync();
        var pending = new TaskCompletionSource<PlannerResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        planner.ProposeAsync(Arg.Any<ConferenceDocument>(), Arg.Any<string>(), Arg.Any<TimeZoneInfo>(),
            Arg.Any<string[]>(), Arg.Any<CancellationToken>()).Returns(call =>
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
        var (vm, planner, _, store) = await CreateAsync();
        planner.UnsupportedReason.Returns("No model");
        await vm.CheckModelCommand.ExecuteAsync(null);
        Assert.False(vm.IsReady);
        Assert.False(vm.CanGenerate);
        Assert.True(vm.CanEdit);
        Assert.Equal("No model", vm.Status);
        Assert.Equal(["already-liked"], store.Saved);
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
