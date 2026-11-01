using Microsoft.Maui.Foldable;
using Microsoft.Maui.Graphics;

namespace DotNetConf.App.Tests;

public sealed class FoldableRegionTests
{
    [Fact]
    public void SelectsTheFirstActiveRegionThatDividesTheView()
    {
        var bounds = new Rect(0, 0, 669, 869);
        var expected = new Rect(320, 0, 29, 869);
        FoldableRegion[] regions =
        [
            new(new Rect(300, 0, 20, 869), false),
            new(new Rect(800, 0, 20, 869), true),
            new(expected, true)
        ];

        Assert.Equal(expected, FoldableRegionHelper.GetActiveDivisionRegion(regions, bounds));
    }

    [Theory]
    [InlineData(double.NaN, 0, 20, 869)]
    [InlineData(320, 0, double.PositiveInfinity, 869)]
    [InlineData(320, 0, 0, 869)]
    [InlineData(0, 0, 669, 20)]
    [InlineData(800, 0, 20, 869)]
    public void RejectsInvalidOrNonDividingRegions(double x, double y, double width, double height)
    {
        var regions = new[] { new FoldableRegion(new Rect(x, y, width, height), true) };
        Assert.Equal(Rect.Zero, FoldableRegionHelper.GetActiveDivisionRegion(regions, new Rect(0, 0, 669, 869)));
    }

    [Fact]
    public void SelectsAHorizontalDivision()
    {
        var region = new Rect(0, 420, 951, 30);
        Assert.Equal(region, FoldableRegionHelper.GetActiveDivisionRegion(
            [new FoldableRegion(region, true)], new Rect(0, 0, 951, 869)));
    }

    [Fact]
    public void MonitorIsDisposedOnlyAfterTheLastObserverDisconnects()
    {
        var disposed = new List<object>();
        var registry = new FoldableMonitorRegistry<object, object>(disposed.Add);
        var key = new object();
        var monitor = registry.GetOrAdd(key, () => new object());

        Assert.Same(monitor, registry.GetOrAdd(key, () => new object()));
        Assert.True(registry.Remove(key));
        Assert.Empty(disposed);
        Assert.True(registry.Remove(key));
        Assert.Equal(new[] { monitor }, disposed);
        Assert.False(registry.Remove(key));
    }

    [Fact]
    public void ReplacingAndDisposingMonitorsReleasesEachInteraction()
    {
        var disposed = new List<object>();
        var registry = new FoldableMonitorRegistry<object, object>(disposed.Add);
        var key = new object();
        var old = registry.GetOrAdd(key, () => new object());
        var replacement = registry.GetOrAdd(key, () => new object(), _ => true);

        Assert.Equal(new[] { old }, disposed);
        registry.Dispose();
        Assert.Equal(new[] { old, replacement }, disposed);
        Assert.Equal(0, registry.Count);
    }
}
