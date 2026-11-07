using DotNetConf.App.Services;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace DotNetConf.App.Tests;

public sealed class ScheduleLayoutTests
{
    [Theory]
    [InlineData(680, 1, true)]
    [InlineData(679, 1, false)]
    [InlineData(1000, 1.6, false)]
    [InlineData(1088, 1.6, true)]
    public void FlatWideScheduleUsesEqualUsablePanes(double width, double scale, bool expected)
    {
        var candidate = new DetailLayout(true, false, new(0.42, GridUnitType.Star), new(0),
            new(0.58, GridUnitType.Star));
        var layout = ScheduleLayout.ForDetails(candidate, width, scale);
        Assert.Equal(expected, layout.IsSplit);
        Assert.False(layout.IsStacked);
        if (expected)
            Assert.Equal(layout.ListLength, layout.DetailLength);
    }

    [Theory]
    [InlineData(413.5, 40, 867, 1, true)]
    [InlineData(433.5, 0, 867, 1, true)]
    [InlineData(339, 40, 867, 1, false)]
    [InlineData(528, 40, 867, 1, false)]
    [InlineData(413.5, 40, 867, 1.6, false)]
    public void VerticalDivisionIsPreservedOnlyWhenBothPanesFit(
        double left, double gap, double width, double scale, bool expected)
    {
        var candidate = new DetailLayout(true, false, new(left), new(gap), GridLength.Star);
        var layout = ScheduleLayout.ForDetails(candidate, width, scale);
        Assert.Equal(expected, layout.IsSplit);
        if (expected)
            Assert.Equal(candidate, layout);
    }

    [Fact]
    public void HorizontalDivisionsAndCompactLayoutsNeverStackScheduleDetails()
    {
        var horizontal = new DetailLayout(true, true, new(400), new(40), GridLength.Star);
        var compact = new DetailLayout(false, false, GridLength.Star, new(0), new(0));
        Assert.Equal(compact, ScheduleLayout.ForDetails(horizontal, 1000));
        Assert.Equal(compact, ScheduleLayout.ForDetails(compact, 1000));
    }

    [Theory]
    [InlineData(339, true)]
    [InlineData(340, false)]
    [InlineData(219, true)]
    public void TimelineLabelsStackInNarrowScaledContent(double width, bool expected) =>
        Assert.Equal(expected, ScheduleLayout.ShouldStackTimelineLabels(width));

    [Theory]
    [InlineData(132, 84, 0, true)]
    [InlineData(131, 84, 0, false)]
    [InlineData(160, 84, 28, true)]
    [InlineData(159, 84, 28, false)]
    [InlineData(500, 60, 34, true)]
    public void DayHeaderPinsOnlyWhenAReadableCardFitsAboveTheBottomInset(
        double viewportHeight, double headerHeight, double bottomInset, bool expected) =>
        Assert.Equal(expected, ScheduleLayout.ShouldPinTimelineDayHeader(viewportHeight, headerHeight, bottomInset));

    [Theory]
    [InlineData(259, 900, 1, true)]
    [InlineData(260, 600, 1, false)]
    [InlineData(402, 874, 2, true)]
    [InlineData(520, 1200, 2, false)]
    [InlineData(852, 393, 1, true)]
    public void ScheduleHeaderYieldsSpaceForLargeTextOrShortWindows(
        double width, double height, double scale, bool expected) =>
        Assert.Equal(expected, ScheduleLayout.ShouldUseCompactHeader(width, height, scale));

    [Theory]
    [InlineData(120, 320)]
    [InlineData(320, 320)]
    [InlineData(390, 390)]
    public void TimelineCanScrollHorizontallyWhenLargeTextNarrowsTheViewport(double viewport, double expected) =>
        Assert.Equal(expected, ScheduleLayout.TimelineContentWidth(viewport));

    [Theory]
    [InlineData(867, 449, 1, false)]
    [InlineData(720, 600, 1, true)]
    [InlineData(719, 600, 1, false)]
    [InlineData(720, 599, 1, false)]
    [InlineData(1152, 960, 1.6, true)]
    [InlineData(1152, 959, 1.6, false)]
    [InlineData(1151, 960, 1.6, false)]
    [InlineData(402, 650, 1, false)]
    public void TopicsWrapOnlyWithEnoughScaledSpace(double width, double height, double scale, bool expected) =>
        Assert.Equal(expected, ScheduleLayout.ShouldWrapTopics(width, height, scale));

    [Theory]
    [InlineData(600, 1, true)]
    [InlineData(599, 1, false)]
    [InlineData(650, 3, false)]
    [InlineData(960, 1.6, true)]
    [InlineData(959, 1.6, false)]
    [InlineData(600, 0.8, true)]
    public void HeadingYieldsSpaceOnShortOrLargeTextScreens(double height, double scale, bool expected) =>
        Assert.Equal(expected, ScheduleLayout.ShouldShowHeading(height, scale));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidTextScaleIsRejected(double scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScheduleLayout.ShouldWrapTopics(720, 600, scale));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScheduleLayout.ShouldShowHeading(600, scale));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScheduleLayout.ShouldUseCompactHeader(400, 800, scale));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScheduleLayout.ForDetails(default, 1000, scale));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScheduleLayout.TimelineContentWidth(scale));
    }
}
