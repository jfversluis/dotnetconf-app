using DotNetConf.Core;
using Microsoft.Maui.Maps;
using Map = Microsoft.Maui.Controls.Maps.Map;

namespace DotNetConf.App.Controls;

internal static class CommunityMapViewport
{
    public static void ShowAll(Map map, IReadOnlyList<CommunityEvent> events)
    {
        if (events.Count == 0)
            return;

#if IOS
        if (map.Handler?.PlatformView is MapKit.MKMapView nativeMap)
        {
            var points = events.Select(e => MapKit.MKMapPoint.FromCoordinate(
                new CoreLocation.CLLocationCoordinate2D(e.Latitude, e.Longitude))).ToArray();
            var left = points.Min(p => p.X);
            var top = points.Min(p => p.Y);
            // Geographic spans are normalized across the dateline by MAUI. Fit the actual
            // projected points instead so a worldwide collection does not focus the Pacific.
            var bounds = new MapKit.MKMapRect(left, top,
                Math.Max(1000, points.Max(p => p.X) - left),
                Math.Max(1000, points.Max(p => p.Y) - top));
            nativeMap.SetVisibleMapRect(bounds, new UIKit.UIEdgeInsets(72, 32, 32, 32), false);
            return;
        }
#elif ANDROID
        if (map.Handler?.PlatformView is Android.Gms.Maps.MapView nativeMap)
        {
            nativeMap.GetMapAsync(new FitEventsCallback(events));
            return;
        }
#endif
        var south = events.Min(e => e.Latitude);
        var north = events.Max(e => e.Latitude);
        var west = events.Min(e => e.Longitude);
        var east = events.Max(e => e.Longitude);
        map.MoveToRegion(new MapSpan(new Location((south + north) / 2, (west + east) / 2),
            Math.Max(0.05, north - south), Math.Max(0.05, east - west)));
    }

#if ANDROID
    private sealed class FitEventsCallback(IReadOnlyList<CommunityEvent> events)
        : Java.Lang.Object, Android.Gms.Maps.IOnMapReadyCallback
    {
        public void OnMapReady(Android.Gms.Maps.GoogleMap googleMap)
        {
            using var bounds = new Android.Gms.Maps.Model.LatLngBounds.Builder();
            foreach (var e in events)
                bounds.Include(new Android.Gms.Maps.Model.LatLng(e.Latitude, e.Longitude));
            using var region = bounds.Build();
            using var update = Android.Gms.Maps.CameraUpdateFactory.NewLatLngBounds(region, 72);
            googleMap.MoveCamera(update);
        }
    }
#endif
}
