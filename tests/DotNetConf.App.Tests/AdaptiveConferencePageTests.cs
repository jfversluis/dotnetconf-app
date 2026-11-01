using DotNetConf.App.Pages;
using DotNetConf.App.Services;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using System.Reflection;

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

    [Theory]
    [InlineData(false, 413.5, 40, 867, true)]
    [InlineData(true, 400, 40, 1000, false)]
    [InlineData(false, 300, 40, 867, false)]
    public void SchedulePolicyIsAppliedBeforePaneVisibilityAndSizing(
        bool stacked, double first, double gap, double width, bool expectedSplit)
    {
        var page = new ScheduleHost();
        var candidate = new DetailLayout(true, stacked, new(first), new(gap), GridLength.Star);
        typeof(AdaptiveConferencePage).GetMethod("UpdateLayoutPolicy", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, [width, 1000d, 1d, candidate]);
        Assert.Equal(expectedSplit, page.CanShowInline);
        var panes = Assert.IsType<Grid>(page.Content);
        var detail = Assert.Single(panes.Children.OfType<Grid>(), child => child.AutomationId == "detail-pane");
        Assert.Equal(expectedSplit, detail.IsVisible);
        Assert.Equal(0, Grid.GetRow(detail));
        Assert.Equal(expectedSplit ? gap : 0, panes.ColumnDefinitions[1].Width.Value);
    }

    private sealed class ScheduleHost : AdaptiveConferencePage
    {
        public ScheduleHost() : base(null!)
        {
            Content = new Grid();
            ConfigureDetails(DetailKind.Session, new DetailNavigationService());
        }

        protected override DetailLayout AdaptDetailLayout(DetailLayout layout, double width, double textScale) =>
            ScheduleLayout.ForDetails(layout, width, textScale);
    }
}
