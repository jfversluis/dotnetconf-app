using DotNetConf.App.Services;

namespace DotNetConf.App.Tests;

public sealed class ScheduleLayoutTests
{
    [Theory]
    [InlineData(339, true)]
    [InlineData(340, false)]
    [InlineData(219, true)]
    public void TimelineLabelsStackInNarrowScaledContent(double width, bool expected) =>
        Assert.Equal(expected, ScheduleLayout.ShouldStackTimelineLabels(width));

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
    }
}
