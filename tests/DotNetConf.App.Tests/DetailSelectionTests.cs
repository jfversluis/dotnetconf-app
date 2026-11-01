using DotNetConf.App.Services;

namespace DotNetConf.App.Tests;

public sealed class DetailSelectionTests
{
    private static readonly DetailDestination Session = new(DetailKind.Session, "synthetic-session");
    private static readonly DetailDestination Speaker = new(DetailKind.Speaker, "synthetic-speaker");

    [Fact]
    public void StartsWithoutSelection()
    {
        var selection = new DetailSelection(DetailKind.Session);
        Assert.Null(selection.Current);
        Assert.Null(selection.ListItemId);
    }

    [Fact]
    public void CrossLinksReplaceTheDetailAndRetainTheMasterSelection()
    {
        var selection = new DetailSelection(DetailKind.Session);
        selection.Select(Session);
        selection.Select(Speaker);
        Assert.Equal(Speaker, selection.Current);
        Assert.Equal(Session.Id, selection.ListItemId);
    }

    [Fact]
    public void SelectingAnotherListItemReplacesTheSelection()
    {
        var selection = new DetailSelection(DetailKind.Session);
        selection.Select(Session);
        selection.Select(Speaker);
        var next = new DetailDestination(DetailKind.Session, "another-synthetic-session");
        selection.Select(next);
        Assert.Equal(next, selection.Current);
        Assert.Equal(next.Id, selection.ListItemId);
    }

    [Fact]
    public void ReselectingTheCurrentDetailPreservesTheSelection()
    {
        var selection = new DetailSelection(DetailKind.Session);
        selection.Select(Session);
        selection.Select(Session);
        Assert.Equal(Session, selection.Current);
        Assert.Equal(Session.Id, selection.ListItemId);
    }

    [Fact]
    public void IdenticalIdsForDifferentKindsAreDifferentDestinations()
    {
        var selection = new DetailSelection(DetailKind.Session);
        selection.Select(Session);
        var speaker = new DetailDestination(DetailKind.Speaker, Session.Id);
        selection.Select(speaker);
        Assert.Equal(speaker, selection.Current);
        Assert.Equal(Session.Id, selection.ListItemId);
    }

    [Fact]
    public void SpeakerMasterSelectionSurvivesASessionCrossLink()
    {
        var selection = new DetailSelection(DetailKind.Speaker);
        selection.Select(Speaker);
        selection.Select(Session);
        Assert.Equal(Session, selection.Current);
        Assert.Equal(Speaker.Id, selection.ListItemId);
    }

    [Fact]
    public void TabsKeepIndependentSelections()
    {
        var sessions = new DetailSelection(DetailKind.Session);
        var speakers = new DetailSelection(DetailKind.Speaker);
        sessions.Select(Session);
        speakers.Select(Speaker);
        sessions.Select(new(DetailKind.Session, "another-synthetic-session"));
        Assert.Equal(Speaker, speakers.Current);
        Assert.Equal(Speaker.Id, speakers.ListItemId);
    }

    [Theory]
    [InlineData(DetailKind.Session, "session-detail")]
    [InlineData(DetailKind.Speaker, "speaker-detail")]
    public void UsesTheExistingNativeRoutes(DetailKind kind, string route) =>
        Assert.Equal(route, new DetailDestination(kind, "synthetic-id").Route);
}
