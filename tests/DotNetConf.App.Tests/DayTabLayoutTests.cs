using DotNetConf.App.Services;

namespace DotNetConf.App.Tests;

public sealed class DayTabLayoutTests
{
    [Theory]
    [InlineData(233, 6, 44, true)]
    [InlineData(233, 3, 44, false)]
    [InlineData(384, 6, 44, false)]
    [InlineData(383, 6, 44, true)]
    [InlineData(620, 6, 80, false)]
    [InlineData(560, 6, 80, true)]
    public void ScrollsOnlyWhenHeadersCannotFit(
        double width, int count, double widestLabel, bool expected)
    {
        Assert.Equal(expected, DayTabLayout.ShouldScroll(width, count, widestLabel));
    }

    [Fact]
    public void InvalidMeasurementsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DayTabLayout.ShouldScroll(0, 3, 44));
        Assert.Throws<ArgumentOutOfRangeException>(() => DayTabLayout.ShouldScroll(200, 0, 44));
        Assert.Throws<ArgumentOutOfRangeException>(() => DayTabLayout.ShouldScroll(200, 3, double.NaN));
    }
}
