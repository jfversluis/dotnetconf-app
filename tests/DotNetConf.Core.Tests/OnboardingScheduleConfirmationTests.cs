using DotNetConf.App;

namespace DotNetConf.Core.Tests;

public sealed class UnsavedSuggestionsConfirmationTests
{
    [Fact]
    public void CheckedSuggestionsAlwaysRequireExplicitDeparture()
    {
        var prompt = UnsavedSuggestionsConfirmation.For(true);

        Assert.NotNull(prompt);
        Assert.Equal("Leave without adding?", prompt.Title);
        Assert.Contains("haven't been added", prompt.Message);
        Assert.Equal("Leave without adding", prompt.Accept);
        Assert.Equal("Keep choosing", UnsavedSuggestionsConfirmation.Cancel);
    }

    [Fact]
    public void NoCheckedSuggestionsNeedNoDeparturePrompt()
    {
        Assert.Null(UnsavedSuggestionsConfirmation.For(false));
    }

    [Fact]
    public void OnboardingContinueWithoutAddingSessionsRequiresConfirmation()
    {
        var prompt = UnsavedSuggestionsConfirmation.ForOnboarding(false, false, true);

        Assert.NotNull(prompt);
        Assert.Equal("Continue without sessions?", prompt.Title);
        Assert.Contains("Was that intentional?", prompt.Message);
        Assert.Equal("Continue without sessions", prompt.Accept);
        Assert.Equal("Keep choosing", UnsavedSuggestionsConfirmation.Cancel);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OnboardingCheckedSuggestionsTakePriorityOverTheEmptyScheduleWarning(bool hasAddedSessions, bool continuing)
    {
        Assert.Equal(UnsavedSuggestionsConfirmation.For(true),
            UnsavedSuggestionsConfirmation.ForOnboarding(true, hasAddedSessions, continuing));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OnboardingExplicitSkipOrPreviouslyAddedSessionsNeedNoEmptyScheduleWarning(bool hasAddedSessions, bool continuing)
    {
        Assert.Null(UnsavedSuggestionsConfirmation.ForOnboarding(false, hasAddedSessions, continuing));
    }
}
