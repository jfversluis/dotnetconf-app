using DotNetConf.App.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace DotNetConf.App.Tests;

public sealed class DetailLayoutTests
{
    [Theory]
    [InlineData(402, 1, false, true)]
    [InlineData(599, 1, false, true)]
    [InlineData(600, 1, false, false)]
    [InlineData(899, 1.5, false, true)]
    [InlineData(900, 1.5, false, false)]
    [InlineData(900, 1, true, true)]
    [InlineData(599, 0.8, false, true)]
    public void ListHeaderScrollsWhenTheWindowIsShortOrPanesAreStacked(
        double height, double textScale, bool isStacked, bool expected)
    {
        Assert.Equal(expected, DetailLayout.ShouldScrollListHeader(height, textScale, isStacked));
    }

    [Theory]
    [InlineData(true, 874, 402, 0, false, true)]
    [InlineData(true, 874, 402, 119, false, true)]
    [InlineData(true, 874, 402, 120, false, false)]
    [InlineData(true, 874, 402, -1, false, false)]
    [InlineData(true, 874, 402, 100, true, true)]
    [InlineData(false, 874, 402, 0, true, false)]
    [InlineData(true, 402, 874, 0, true, false)]
    [InlineData(true, 874, 0, 0, false, false)]
    public void SearchHidesNavigationOnlyWhenALandscapeViewportIsTooShort(
        bool focused, double width, double height, double resultsHeight, bool wasHidden, bool expected)
    {
        Assert.Equal(expected,
            DetailLayout.ShouldHideNavigationForSearch(focused, width, height, resultsHeight, wasHidden));
    }

    [Theory]
    [InlineData(true, 874, 402, false, true)]
    [InlineData(true, 874, 402, true, false)]
    [InlineData(false, 874, 402, false, false)]
    [InlineData(true, 402, 874, false, false)]
    [InlineData(true, 1000, 600, false, false)]
    [InlineData(true, 874, 0, false, false)]
    public void ShortLandscapeSearchPreparesOnlyForAnExpectedSoftKeyboard(
        bool focused, double width, double height, bool hasHardwareKeyboard, bool expected)
    {
        Assert.Equal(expected,
            DetailLayout.ShouldPrepareShortLandscapeSearch(focused, width, height, hasHardwareKeyboard));
    }

    [Theory]
    [InlineData(1009, 1083, 1840, 841, 84)]
    [InlineData(1009, 999, 1840, 841, 0)]
    [InlineData(1009, 900, 1840, 841, 0)]
    [InlineData(300, 1300, 1840, 841, 300)]
    [InlineData(400, 2000, 2400, 500, 100)]
    [InlineData(1009, 1083, 1840, 0, 0)]
    public void KeyboardOverlapIsLimitedToTheObscuredPartOfTheView(
        double height, double bottom, double windowBottom, double imeInset, double expected)
    {
        Assert.Equal(expected, DetailLayout.KeyboardOverlap(height, bottom, windowBottom, imeInset));
    }

    [Theory]
    [InlineData(466, 678, false)]
    [InlineData(640, 951, false)]
    [InlineData(641, 951, true)]
    [InlineData(669, 951, true)]
    [InlineData(951, 669, true)]
    [InlineData(669, 599, false)]
    [InlineData(669, 600, true)]
    [InlineData(669, 466, false)]
    public void WindowThresholds(double width, double height, bool expected)
    {
        var layout = DetailLayout.ForWindow(width, height, Rect.Zero);
        Assert.Equal(expected, layout.IsSplit);
        Assert.False(layout.IsStacked);
        Assert.Equal(new GridLength(0), layout.HingeLength);
        Assert.Equal(expected ? new GridLength(0.42, GridUnitType.Star) : GridLength.Star, layout.ListLength);
        Assert.Equal(expected ? new GridLength(0.58, GridUnitType.Star) : new GridLength(0), layout.DetailLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public void VerticalFoldReservesItsColumn(double gap)
    {
        var layout = DetailLayout.ForWindow(840, 700, new Rect(410, 0, gap, 700));
        Assert.True(layout.IsSplit);
        Assert.False(layout.IsStacked);
        Assert.Equal(new GridLength(410), layout.ListLength);
        Assert.Equal(new GridLength(gap), layout.HingeLength);
        Assert.Equal(GridLength.Star, layout.DetailLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public void HorizontalFoldReservesItsRow(double gap)
    {
        var layout = DetailLayout.ForWindow(700, 840, new Rect(0, 410, 700, gap));
        Assert.True(layout.IsSplit);
        Assert.True(layout.IsStacked);
        Assert.Equal(new GridLength(410), layout.ListLength);
        Assert.Equal(new GridLength(gap), layout.HingeLength);
        Assert.Equal(GridLength.Star, layout.DetailLength);
    }

    [Theory]
    [InlineData(199, 300, 1, false)]
    [InlineData(200, 300, 1, true)]
    [InlineData(300, 199, 1, false)]
    [InlineData(300, 200, 1, true)]
    [InlineData(299, 400, 1.5, false)]
    [InlineData(300, 400, 1.5, true)]
    [InlineData(400, 299, 1.5, false)]
    [InlineData(400, 300, 1.5, true)]
    public void HorizontalFoldNeedsEnoughHeightInBothPanes(
        double listHeight, double detailHeight, double textScale, bool expected)
    {
        var contentHeight = listHeight + 20 + detailHeight;
        var layout = DetailLayout.ForWindow(800, 1200, new Rect(0, listHeight, 800, 20),
            textScale, contentWidth: 800, contentHeight: contentHeight);
        Assert.Equal(expected, layout.IsSplit);
        Assert.Equal(expected, layout.IsStacked);
    }

    [Theory]
    [InlineData(1.82, true)]
    [InlineData(1.9, false)]
    public void HorizontalFoldUsesMeasuredPaneHeightsForLargeText(double textScale, bool expected)
    {
        var layout = DetailLayout.ForWindow(669, 951, new Rect(0, 373.5, 669, 40),
            textScale, contentWidth: 669, contentHeight: 869);

        Assert.Equal(expected, layout.IsSplit);
        Assert.Equal(expected, layout.IsStacked);
    }

    [Fact]
    public void FoldOutsideWindowDoesNotReserveASpuriousColumn()
    {
        var layout = DetailLayout.ForWindow(669, 951, new Rect(800, 0, 20, 951));
        Assert.True(layout.IsSplit);
        Assert.Equal(new GridLength(0.42, GridUnitType.Star), layout.ListLength);
        Assert.Equal(new GridLength(0), layout.HingeLength);
    }

    [Fact]
    public void VerticalHingeUsesContentCoordinatesWhenTheWindowHasAnOffset()
    {
        var hinge = DetailLayout.HingeRelativeToContent(new Rect(355, 30, 20, 800),
            [new Rect(0, 0, 315, 700), new Rect(335, 0, 300, 700)]);

        Assert.Equal(new Rect(315, 0, 20, 700), hinge);
        var layout = DetailLayout.ForWindow(800, 900, hinge, contentWidth: 635, contentHeight: 700);
        Assert.Equal(new GridLength(315), layout.ListLength);
        Assert.Equal(new GridLength(20), layout.HingeLength);
    }

    [Fact]
    public void HorizontalHingeUsesContentCoordinatesWhenTheWindowHasAnOffset()
    {
        var hinge = DetailLayout.HingeRelativeToContent(new Rect(20, 405, 700, 20),
            [new Rect(0, 0, 650, 350), new Rect(0, 370, 650, 350)]);

        Assert.Equal(new Rect(0, 350, 650, 20), hinge);
        var layout = DetailLayout.ForWindow(800, 900, hinge, contentWidth: 650, contentHeight: 720);
        Assert.True(layout.IsSplit);
        Assert.True(layout.IsStacked);
        Assert.Equal(new GridLength(350), layout.ListLength);
        Assert.Equal(new GridLength(20), layout.HingeLength);
    }

    [Theory]
    [InlineData(0, 0, 669, 40, 0, 88, 0, -88)]
    [InlineData(455.5, 0, 40, 669, 30, 58, 425.5, -58)]
    [InlineData(0, 455.5, 669, 40, 0, 82, 0, 373.5)]
    public void WindowHingeSubtractsTheContentOrigin(
        double x, double y, double width, double height, double originX, double originY,
        double expectedX, double expectedY)
    {
        var hinge = DetailLayout.HingeRelativeToContent(new Rect(x, y, width, height),
            new Point(originX, originY));

        Assert.Equal(new Rect(expectedX, expectedY, width, height), hinge);
    }

    [Fact]
    public void MissingWindowHingeStaysEmptyAfterSubtractingContentOrigin()
    {
        Assert.Equal(Rect.Zero, DetailLayout.HingeRelativeToContent(Rect.Zero, new Point(24, 88)));
    }

    [Fact]
    public void InconsistentSpanningBoundsDoNotInventAContentHinge()
    {
        var hinge = new Rect(410, 0, 20, 700);
        var contentHinge = DetailLayout.HingeRelativeToContent(hinge,
            [new Rect(0, 0, 400, 700), new Rect(0, 720, 400, 700)]);

        Assert.Equal(hinge, contentHinge);
    }

    [Fact]
    public void HingeOutsideContentDoesNotReserveAColumn()
    {
        var layout = DetailLayout.ForWindow(1000, 900, new Rect(720, 0, 20, 800),
            contentWidth: 700, contentHeight: 800);

        Assert.True(layout.IsSplit);
        Assert.Equal(new GridLength(0), layout.HingeLength);
        Assert.Equal(new GridLength(0.42, GridUnitType.Star), layout.ListLength);
    }

    [Fact]
    public void UnloadedHostUsesCurrentWindowInsteadOfStaleContentGeometry()
    {
        var layout = DetailLayout.ForWindow(841, 701, new Rect(0, 200, 411, 40),
            contentWidth: 411, contentHeight: 597, contentIsCurrent: false);

        Assert.True(layout.IsSplit);
        Assert.False(layout.IsStacked);
        Assert.Equal(new GridLength(0.42, GridUnitType.Star), layout.ListLength);
        Assert.Equal(new GridLength(0), layout.HingeLength);
    }

    [Fact]
    public void LoadedHostUsesMeasuredContentGeometry()
    {
        var layout = DetailLayout.ForWindow(841, 701, Rect.Zero,
            contentWidth: 411, contentHeight: 597);

        Assert.False(layout.IsSplit);
    }

    [Theory]
    [InlineData(669, 951, 1, true)]
    [InlineData(669, 951, 1.5, true)]
    [InlineData(669, 951, 1.6, false)]
    [InlineData(669, 951, 1.82, false)]
    [InlineData(951, 669, 1.82, false)]
    [InlineData(951, 1092, 1.82, true)]
    [InlineData(951, 900, 1.5, true)]
    [InlineData(951, 899, 1.5, false)]
    public void TextScaleRequiresEnoughSpaceForPanes(double width, double height, double textScale, bool expected)
    {
        Assert.Equal(expected, DetailLayout.ForWindow(width, height, Rect.Zero, textScale).IsSplit);
    }

    [Theory]
    [InlineData(2.4, true)]
    [InlineData(2.5, false)]
    public void TextScaleUsesActualHingePaneWidth(double textScale, bool expected)
    {
        Assert.Equal(expected, DetailLayout.ForWindow(840, 1600, new Rect(410, 0, 20, 1600), textScale).IsSplit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidTextScaleIsRejected(double textScale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DetailLayout.ForWindow(669, 951, Rect.Zero, textScale));
        Assert.Throws<ArgumentOutOfRangeException>(() => DetailLayout.ShouldScrollListHeader(600, textScale, false));
    }
}
