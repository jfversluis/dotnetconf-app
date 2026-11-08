using DotNetConf.App.Pages;
using DotNetConf.App.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace DotNetConf.App.Tests;

public sealed class CommunityExplorerLayoutTests
{
    [Theory]
    [InlineData(354, 1, false)]
    [InlineData(334, 1, false)]
    [InlineData(319, 1, true)]
    [InlineData(320, 1, false)]
    [InlineData(354, 1.5, true)]
    [InlineData(334, 2, true)]
    [InlineData(480, 1.5, false)]
    public void HeaderModesStackRatherThanTruncatingLabels(
        double width, double scale, bool stacked)
    {
        Assert.Equal(stacked, CommunityEventsPage.ShouldStackModeControls(width, scale));
    }

    [Theory]
    [InlineData(466, 678, 1, false)]
    [InlineData(640, 951, 1, false)]
    [InlineData(669, 951, 1, true)]
    [InlineData(951, 669, 1, true)]
    [InlineData(669, 466, 1, false)]
    [InlineData(669, 951, 2, false)]
    public void CompactAndExpandedWindowsRetainReadablePanes(double width, double height, double scale, bool split)
    {
        var layout = CommunityEventsPage.AdaptExplorerLayout(
            DetailLayout.ForWindow(width, height, Rect.Zero, scale), true, height, scale);

        Assert.Equal(split, layout.IsSplit);
        Assert.False(layout.IsStacked);
    }

    [Fact]
    public void UnavailableMapAlwaysUsesTheFullWidthList()
    {
        var layout = CommunityEventsPage.AdaptExplorerLayout(
            DetailLayout.ForWindow(951, 669, Rect.Zero), false, 669, 1);

        Assert.False(layout.IsSplit);
        Assert.Equal(GridLength.Star, layout.ListLength);
        Assert.Equal(new GridLength(0), layout.DetailLength);
    }

    [Fact]
    public void VerticalDivisionKeepsListAndMapOnOppositeSidesOfTheHinge()
    {
        var layout = CommunityEventsPage.AdaptExplorerLayout(
            DetailLayout.ForWindow(951, 669, new Rect(455.5, 0, 40, 669)), true, 669, 1);

        Assert.True(layout.IsSplit);
        Assert.False(layout.IsStacked);
        Assert.Equal(new GridLength(455.5), layout.ListLength);
        Assert.Equal(new GridLength(40), layout.HingeLength);
        Assert.Equal(GridLength.Star, layout.DetailLength);
    }

    [Theory]
    [InlineData(319, 400, 1, false)]
    [InlineData(320, 400, 1, true)]
    [InlineData(400, 319, 1, false)]
    [InlineData(400, 320, 1, true)]
    [InlineData(479, 600, 1.5, false)]
    [InlineData(480, 600, 1.5, true)]
    public void TabletopRequiresRoomForTheLegendAndNativeMap(
        double top, double bottom, double scale, bool split)
    {
        var height = top + 40 + bottom;
        var layout = CommunityEventsPage.AdaptExplorerLayout(
            DetailLayout.ForWindow(951, height, new Rect(0, top, 951, 40), scale), true, height, scale);

        Assert.Equal(split, layout.IsSplit);
        Assert.Equal(split, layout.IsStacked);
        if (split)
            Assert.Equal(new GridLength(40), layout.HingeLength);
    }
}
