using DotNetConf.App.Pages;
using DotNetConf.App.Services;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace DotNetConf.App.Tests;

public sealed class AdaptiveConferencePageTests
{
    [Theory]
    [InlineData(DetailKind.Session)]
    [InlineData(DetailKind.Speaker)]
    public void PaneHostsRequestKeyboardAvoidanceWithoutABottomContainerInset(DetailKind kind)
    {
        var page = new TestPage(kind);
        var panes = Assert.IsType<Grid>(page.Content);
        var detail = Assert.Single(panes.Children.OfType<Grid>(), child => child.AutomationId == "detail-pane");

        foreach (var grid in new[] { panes, detail })
        {
            Assert.Equal(SafeAreaRegions.Container, grid.SafeAreaEdges.Left);
            Assert.Equal(SafeAreaRegions.Container, grid.SafeAreaEdges.Top);
            Assert.Equal(SafeAreaRegions.Container, grid.SafeAreaEdges.Right);
            Assert.Equal(SafeAreaRegions.SoftInput, grid.SafeAreaEdges.Bottom);
        }
    }

    [Fact]
    public void MovingFilterControlsLeavesSearchPinnedAndRestoresTheSameViews()
    {
        var search = new Entry();
        var filters = new Grid();
        var pinned = new ContentView { Content = filters };
        var scrolling = new ContentView { IsVisible = false };
        var header = new VerticalStackLayout { Children = { search, pinned } };

        TestPage.MoveHeader(pinned, scrolling, true);
        Assert.Null(pinned.Content);
        Assert.False(pinned.IsVisible);
        Assert.Same(filters, scrolling.Content);
        Assert.True(scrolling.IsVisible);
        Assert.Same(header, search.Parent);

        TestPage.MoveHeader(pinned, scrolling, false);
        Assert.Same(filters, pinned.Content);
        Assert.True(pinned.IsVisible);
        Assert.Null(scrolling.Content);
        Assert.False(scrolling.IsVisible);
        Assert.Same(header, search.Parent);
    }

    private sealed class TestPage : AdaptiveConferencePage
    {
        public TestPage(DetailKind kind) : base(null!)
        {
            Content = new Grid();
            ConfigureDetails(kind, new DetailNavigationService());
        }

        public static void MoveHeader(ContentView pinned, ContentView scrolling, bool scrollHeader) =>
            MoveListHeader(pinned, scrolling, scrollHeader);
    }
}
