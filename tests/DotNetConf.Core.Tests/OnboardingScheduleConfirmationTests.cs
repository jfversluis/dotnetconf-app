using DotNetConf.App;

namespace DotNetConf.Core.Tests;

public sealed class OnboardingScheduleConfirmationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CheckedSuggestionsAlwaysRequireExplicitDeparture(bool hasAddedSessions, bool continuing)
    {
        var prompt = OnboardingScheduleConfirmation.For(true, hasAddedSessions, continuing);

        Assert.NotNull(prompt);
        Assert.Equal("Leave without adding?", prompt.Title);
        Assert.Contains("haven't been added", prompt.Message);
        Assert.Equal("Leave without adding", prompt.Accept);
        Assert.Equal("Keep choosing", OnboardingScheduleConfirmation.Cancel);
    }

    [Fact]
    public void ContinuingWithoutAddingAnythingAsksWhetherThatWasIntentional()
    {
        var prompt = OnboardingScheduleConfirmation.For(false, false, true);

        Assert.NotNull(prompt);
        Assert.Equal("Continue without sessions?", prompt.Title);
        Assert.Contains("Was that intentional?", prompt.Message);
        Assert.Equal("Continue without sessions", prompt.Accept);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SavedSessionsOrExplicitSkipNeedNoEmptySchedulePrompt(bool hasAddedSessions, bool continuing)
    {
        Assert.Null(OnboardingScheduleConfirmation.For(false, hasAddedSessions, continuing));
    }
}
