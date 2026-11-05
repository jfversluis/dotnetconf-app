using DotNetConf.App.Services;

namespace DotNetConf.App.Tests;

public sealed class SessionHeaderScrollStateTests
{
    [Fact]
    public void CollapsesAfterThirtyTwoUnitsOfDownwardTravel()
    {
        var state = new SessionHeaderScrollState();
        state.Update(16, 500);
        state.Update(31, 500);
        Assert.False(state.IsCollapsed);
        state.Update(32, 500);
        Assert.True(state.IsCollapsed);
    }

    [Fact]
    public void ExpandsAfterTwentyFourUnitsUpwardWithoutReturningToTheTop()
    {
        var state = new SessionHeaderScrollState();
        state.Update(200, 500);
        state.Update(177, 500);
        Assert.True(state.IsCollapsed);
        state.Update(176, 500);
        Assert.False(state.IsCollapsed);
        state.Update(177, 500);
        Assert.False(state.IsCollapsed);
        state.Update(208, 500);
        Assert.True(state.IsCollapsed);
    }

    [Fact]
    public void SmallDirectionChangesDoNotFlicker()
    {
        var state = new SessionHeaderScrollState();
        state.Update(100, 500);
        foreach (var offset in new[] { 95d, 100, 98, 102, 99, 104 })
        {
            state.Update(offset, 500);
            Assert.True(state.IsCollapsed);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-40)]
    public void ReturningToTheTopOrPullingToRefreshExpands(double offset)
    {
        var state = new SessionHeaderScrollState();
        state.Update(32, 500);
        Assert.True(state.IsCollapsed);
        state.Update(offset, 500);
        Assert.False(state.IsCollapsed);
        state.Update(0, 500);
        Assert.False(state.IsCollapsed);
    }

    [Fact]
    public void FocusedSearchAndCompactLayoutsKeepTheHeaderExpanded()
    {
        var state = new SessionHeaderScrollState();
        state.Update(100, 500);
        state.Update(200, 500, keepExpanded: true);
        Assert.False(state.IsCollapsed);
        state.Update(400, 500, keepExpanded: true);
        Assert.False(state.IsCollapsed);
        state.Update(401, 500);
        Assert.False(state.IsCollapsed);
        state.Update(432, 500);
        Assert.True(state.IsCollapsed);
    }

    [Fact]
    public void GrowingTheListDoesNotInterpretOffsetClampingAsAnUpwardGesture()
    {
        var state = new SessionHeaderScrollState();
        state.Update(50, 500);
        Assert.True(state.IsCollapsed);
        state.Update(0, 622);
        state.Update(0, 622);
        Assert.True(state.IsCollapsed);
        state.Update(-10, 622);
        Assert.False(state.IsCollapsed);
    }

    [Fact]
    public void AnimatedLayoutOffsetsDoNotReverseTheTargetOrConsumeTheNextGesture()
    {
        var state = new SessionHeaderScrollState();
        state.Update(200, 500);
        state.Update(170, 520, rebase: true);
        state.Update(140, 540, rebase: true);
        state.Update(140, 622, rebase: true);
        Assert.True(state.IsCollapsed);
        state.Update(116, 622);
        Assert.False(state.IsCollapsed);
        state.Update(160, 600, rebase: true);
        state.Update(180, 500, rebase: true);
        Assert.False(state.IsCollapsed);
        state.Update(212, 500);
        Assert.True(state.IsCollapsed);
    }
}
