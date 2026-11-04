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

    private static PlannerViewModel Create(SyntheticPlanner planner, ConferenceDocument? agenda = null)
    {
        var data = Substitute.For<IConferenceDataSource>();
        data.LoadAsync(Arg.Any<CancellationToken>()).Returns(agenda ?? new ConferenceDocument());
        var store = Substitute.For<IScheduleStore>();
        store.ReadAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        return new(data, new ScheduleService(store), planner, Substitute.For<IPlannerScheduleUpdates>(),
            NullLogger<PlannerViewModel>.Instance);
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
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsDownloadingModel);
        Assert.Equal(1, planner.Downloads);
        Assert.Equal(3, planner.Checks);
        Assert.Equal(0, planner.Generations);
        Assert.Equal("Synthetic request", vm.Request);
        Assert.Contains("ready", vm.Status);
    }

    [Fact]
    public async Task DownloadableModelKeepsRandomDiscoveryUntilSetupSucceeds()
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

        Assert.True(vm.IsAiMode);
        Assert.False(vm.CanExplore);
        Assert.False(vm.HasProposal);
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

    private sealed class SyntheticPlanner : IOnDevicePlanner
    {
        public PlannerModelState State { get; set; } = DownloadRequired;
        public Task<PlannerModelState>? PendingCheck { get; set; }
        public Channel<PlannerModelDownload> Updates { get; } = Channel.CreateUnbounded<PlannerModelDownload>();
        public int Downloads { get; private set; }
        public int Checks { get; private set; }
        public int Generations { get; private set; }
        public string? UnsupportedReason => null;
        public Task<PlannerModelState> CheckReadinessAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Checks++;
            return PendingCheck ?? Task.FromResult(State);
        }
        public async IAsyncEnumerable<PlannerModelDownload> DownloadModelAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Downloads++;
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
