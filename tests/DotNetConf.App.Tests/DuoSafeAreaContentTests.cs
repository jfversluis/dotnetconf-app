using DotNetConf.App.Controls;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace DotNetConf.App.Tests;

public class DuoSafeAreaContentTests
{
    [Theory]
    [InlineData(0, 466, 0, 382, 0, 84)]
    [InlineData(0, 466, 84, 382, 84, 0)]
    [InlineData(0, 951, 0, 867, 0, 84)]
    [InlineData(100, 720, 0, 867, 0, 0)]
    [InlineData(0, 382, 0, 382, 0, 0)]
    [InlineData(0, 466, 24, 358, 24, 84)]
    [InlineData(0, 466, 0, 466, 0, 0)]
    [InlineData(0, 40, 84, 382, 40, 0)]
    [InlineData(450, 16, 0, 382, 0, 16)]
    [InlineData(0, 0, 84, 382, 0, 0)]
    public void OnlyTheOverlappingHorizontalRegionIsReserved(
        double x, double width, double safeX, double safeWidth, double left, double right)
    {
        var padding = DuoSafeAreaContent.CalculateHorizontalPadding(
            new Rect(x, -200, width, 800), new Rect(safeX, 58, safeWidth, 620));

        Assert.Equal(new Thickness(left, 0, right, 0), padding);
    }

    [Fact]
    public void StandardContentStartsWithoutAdditionalInsets()
    {
        var content = new DuoSafeAreaContent();

        Assert.Equal(Thickness.Zero, content.Padding);
        Assert.Equal(SafeAreaEdges.None, content.SafeAreaEdges);
    }
}
