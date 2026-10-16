using DotNetConf.App;

namespace DotNetConf.Core.Tests;

public class FirstRunPlannerTests
{
    [Fact]
    public async Task CompletionWaitsForSuccessfulDismissalAndFailureCanBeRetried()
    {
        var writes = 0;
        var state = new FirstRunPlannerState(false, () => writes++);
        Assert.True(state.TryBegin());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            state.CompleteAfterDismissalAsync(() => Task.FromException(new InvalidOperationException("Navigation"))));
        Assert.Equal(0, writes);
        var pending = new TaskCompletionSource();
        var finish = state.CompleteAfterDismissalAsync(() => pending.Task);
        Assert.Equal(0, writes);
        pending.SetResult();
        await finish;
        Assert.Equal(1, writes);
        Assert.False(state.TryBegin());
    }

    [Fact]
    public async Task FailedPersistenceAfterDismissalAllowsAnotherLaunch()
    {
        var state = new FirstRunPlannerState(false, () => throw new IOException("Preferences"));
        Assert.True(state.TryBegin());
        await Assert.ThrowsAsync<IOException>(() => state.CompleteAfterDismissalAsync(() => Task.CompletedTask));
        Assert.True(state.TryBegin());
    }

    [Fact]
    public void FreshInstallStartsOnceWithoutPersistingBeforeDismissal()
    {
        var writes = 0;
        var state = new FirstRunPlannerState(false, () => writes++);

        Assert.True(state.TryBegin());
        Assert.False(state.TryBegin());
        Assert.Equal(0, writes);
    }

    [Fact]
    public void PreviouslyDismissedOnboardingDoesNotStart()
    {
        var state = new FirstRunPlannerState(true, () => Assert.Fail("Already completed."));
        Assert.False(state.TryBegin());
        state.RetryLaunch();
        Assert.False(state.TryBegin());
    }

    [Fact]
    public void SkipOrCompletionPersistsOnlyOnce()
    {
        var writes = 0;
        var state = new FirstRunPlannerState(false, () => writes++);
        Assert.True(state.TryBegin());

        state.Complete();
        state.Complete();

        Assert.Equal(1, writes);
        Assert.False(state.TryBegin());
        Assert.False(new FirstRunPlannerState(writes > 0, () => writes++).TryBegin());
    }

    [Fact]
    public void FailedNavigationCanBeRetriedWithoutMarkingCompleted()
    {
        var writes = 0;
        var state = new FirstRunPlannerState(false, () => writes++);
        Assert.True(state.TryBegin());

        state.RetryLaunch();

        Assert.True(state.TryBegin());
        Assert.Equal(0, writes);
    }

    [Fact]
    public void InterruptedFirstRunIsOfferedOnNextLaunch()
    {
        var completed = false;
        var state = new FirstRunPlannerState(completed, () => completed = true);
        Assert.True(state.TryBegin());

        Assert.True(new FirstRunPlannerState(completed, () => completed = true).TryBegin());
        Assert.False(completed);
    }
}
