#if DEBUG
using DotNetConf.Core;

namespace DotNetConf.Core.Tests;

public class DemoCommunityEventsTests
{
    [Fact]
    public void ContainsExactlyOneHundredDistinctSyntheticLocations()
    {
        var events = DemoCommunityEvents.Create();

        Assert.Equal(100, events.Count);
        Assert.Equal(100, events.Select(e => e.Id).Distinct().Count());
        Assert.Equal(100, events.Select(e => (e.Latitude, e.Longitude)).Distinct().Count());
        Assert.Equal(50, events.Count(e => e.Kind == "watch-party"));
        Assert.Equal(50, events.Count(e => e.Kind == "in-person"));
        Assert.All(events, e =>
        {
            Assert.Contains("(demo)", e.Name);
            Assert.StartsWith("Example ", e.Venue);
            Assert.Equal("https://example.com/", e.WebsiteUrl);
            Assert.InRange(e.Latitude, -90, 90);
            Assert.InRange(e.Longitude, -180, 180);
        });
    }

    [Fact]
    public void CoversTheWorldWithNearbyPairsThatCanCluster()
    {
        var events = DemoCommunityEvents.Create();

        Assert.True(events.Min(e => e.Latitude) < -35);
        Assert.True(events.Max(e => e.Latitude) > 60);
        Assert.True(events.Min(e => e.Longitude) < -120);
        Assert.True(events.Max(e => e.Longitude) > 170);
        Assert.True(events.Select(e => e.Country).Distinct().Count() >= 30);
        Assert.All(events.GroupBy(e => e.City), pair =>
        {
            Assert.Equal(2, pair.Count());
            Assert.InRange(Math.Abs(pair.First().Latitude - pair.Last().Latitude), 0.01, 0.03);
            Assert.InRange(Math.Abs(pair.First().Longitude - pair.Last().Longitude), 0.01, 0.03);
        });
        Assert.Equal(events, DemoCommunityEvents.Create());
    }
}
#else
namespace DotNetConf.Core.Tests;

public class DemoCommunityEventsTests
{
    [Fact]
    public void ReleaseDoesNotContainTheSyntheticEventGenerator()
    {
        Assert.Null(typeof(DotNetConf.Core.CommunityEvent).Assembly.GetType("DotNetConf.Core.DemoCommunityEvents"));
    }
}
#endif
