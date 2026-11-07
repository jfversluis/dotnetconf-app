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
}
