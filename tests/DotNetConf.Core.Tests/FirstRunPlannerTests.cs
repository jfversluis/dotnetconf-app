using DotNetConf.App;

namespace DotNetConf.Core.Tests;

public class FirstRunPlannerTests
{
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
