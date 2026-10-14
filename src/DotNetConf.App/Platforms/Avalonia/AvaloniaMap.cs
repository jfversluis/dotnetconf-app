using System.Collections.ObjectModel;
using System.Collections.Specialized;
using BruTile.Cache;
using DotNetConf.App.Controls;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Limiting;
using Mapsui.Manipulations;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.Widgets;
using Mapsui.Widgets.InfoWidgets;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using MapsuiBrush = Mapsui.Styles.Brush;
using MapsuiColor = Mapsui.Styles.Color;
using MapsuiFont = Mapsui.Styles.Font;

namespace DotNetConf.App;

/// <summary>
/// The community map on Linux and in the browser, where MAUI has no map handler, drawn by Mapsui over OpenStreetMap.
/// </summary>
internal sealed class AvaloniaMap : Mapsui.UI.Maui.MapControl
{
    // About the width of a pin.
    private const double ClusterPixels = 44;
    private const long MoveMilliseconds = 400;
    private const int MinZoomLevel = 2;
    private const int MaxZoomLevel = 19;
    // Where Web Mercator ends.
    private const double MaxLatitude = 85.05;
    private static readonly MRect World = new(-20037508.34, -20037508.34, 20037508.34, 20037508.34);
    private static readonly MapsuiColor White = MapsuiColor.White;
    private static readonly MapsuiColor Purple = MapsuiColor.FromString("#512BD4");

    private readonly MemoryLayer pinLayer = new() { Name = "Pins", Style = null };
    private double groupedResolution;

    static AvaloniaMap()
    {
        // The SkiaSharp views on Avalonia draw in software; there's no GPU context for Mapsui to use.
        UseGPU = false;
        LoggingWidget.ShowLoggingInMap = ActiveMode.No;
        // OpenStreetMap asks apps to cache tiles and refresh them now and then.
        OpenStreetMap.DefaultCache ??= new FileCache(Path.Combine(FileSystem.CacheDirectory, "map-tiles"), "png", TimeSpan.FromDays(7));
    }

    public AvaloniaMap()
    {
        Map.Layers.Add(OpenStreetMap.CreateTileLayer());
        Map.Layers.Add(pinLayer);
        Map.Navigator.OverrideZoomBounds = new MMinMax(Resolution(MaxZoomLevel), Resolution(MinZoomLevel));
        Map.Navigator.OverridePanBounds = World;
        Map.Navigator.Limiter = new ViewportLimiterKeepWithinExtent();
        Map.Navigator.ViewportChanged += OnViewportChanged;
        Map.Tapped += OnTapped;
        Pins.CollectionChanged += OnPinsChanged;
    }

    /// <summary>
    /// The pins on the map; a tap raises the pin's <see cref="Pin.MarkerClicked"/> event.
    /// </summary>
    public ObservableCollection<Pin> Pins { get; } = [];

    /// <summary>
    /// Whether pins close together merge into a numbered cluster that zooms in when tapped.
    /// </summary>
    public bool IsClusteringEnabled { get; init; }

    /// <summary>
    /// Always false: these targets never ask for the user's location.
    /// </summary>
    public bool IsShowingUser
    {
        get => false;
        init
        {
            if (value)
                throw new NotSupportedException("The map doesn't show the user's location on this platform.");
        }
    }

    /// <summary>
    /// Moves the map to show the whole region. Before the map is on screen it opens there; after that it glides.
    /// </summary>
    public void MoveToRegion(MapSpan region)
    {
        var southWest = ToWorld(region.Center.Latitude - region.LatitudeDegrees / 2, region.Center.Longitude - region.LongitudeDegrees / 2);
        var northEast = ToWorld(region.Center.Latitude + region.LatitudeDegrees / 2, region.Center.Longitude + region.LongitudeDegrees / 2);
        Map.Navigator.ZoomToBox(new MRect(southWest.X, southWest.Y, northEast.X, northEast.Y), MBoxFit.Fit,
            Map.Navigator.HasExecutedPostponedCalls ? MoveMilliseconds : 0);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        // The page is made fresh for each visit, so a map off screen for good is released.
        if (Handler is null)
            Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Map.Navigator.ViewportChanged -= OnViewportChanged;
            Map.Tapped -= OnTapped;
            Pins.CollectionChanged -= OnPinsChanged;
        }
        base.Dispose(disposing);
    }

    private void OnPinsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdatePins();

    private void OnViewportChanged(object? sender, ViewportChangedEventArgs e)
    {
        if (IsClusteringEnabled && Map.Navigator.Viewport.Resolution != groupedResolution)
            UpdatePins();
    }

    private void OnTapped(object? sender, MapEventArgs e)
    {
        if (e.GestureType != GestureType.SingleTap || e.GetMapInfo([pinLayer]).Feature is not { } feature)
            return;
        if (feature["pin"] is Pin pin)
            pin.SendMarkerClick();
        else if (feature["cluster"] is List<Pin> cluster)
            ZoomInto(cluster);
        e.Handled = true;
    }

    // The margin keeps the cluster's pins clear of the legend.
    private void ZoomInto(List<Pin> cluster)
    {
        var points = cluster.Select(pin => ToWorld(pin.Location.Latitude, pin.Location.Longitude)).ToList();
        var box = new MRect(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
        Map.Navigator.ZoomToBox(box.Grow(Math.Max(box.Width, box.Height) / 2 + ClusterPixels * Resolution(MaxZoomLevel)),
            MBoxFit.Fit, MoveMilliseconds);
    }

    private void UpdatePins()
    {
        groupedResolution = Map.Navigator.Viewport.Resolution;
        var reach = ClusterPixels * groupedResolution;
        var points = Pins.Select(pin => (Pin: pin, Point: ToWorld(pin.Location.Latitude, pin.Location.Longitude))).ToList();
        var grouped = new bool[points.Count];
        var features = new List<IFeature>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            if (grouped[i])
                continue;
            var group = new List<Pin> { points[i].Pin };
            for (var j = i + 1; IsClusteringEnabled && reach > 0 && j < points.Count; j++)
            {
                if (!grouped[j] && points[i].Point.Distance(points[j].Point) < reach)
                {
                    grouped[j] = true;
                    group.Add(points[j].Pin);
                }
            }
            features.Add(group.Count == 1 ? PinFeature(points[i].Pin, points[i].Point) : ClusterFeature(group));
        }
        pinLayer.Features = features;
        pinLayer.DataHasChanged();
    }

    private static PointFeature PinFeature(Pin pin, MPoint point)
    {
        var feature = new PointFeature(point) { ["pin"] = pin };
        if (pin.ImageSource is FileImageSource { File: { } path })
        {
            feature.Styles.Add(new ImageStyle
            {
                // A proper file URI: in the browser the cache directory is a relative path.
                Image = new Uri(Path.GetFullPath(path)).AbsoluteUri,
                // Pin images are drawn at the display's density, with the tip just above their bottom edge.
                SymbolScale = 1.0 / CommunityPinImages.Scale,
                RelativeOffset = new RelativeOffset(0, 0.45),
            });
        }
        return feature;
    }

    private static PointFeature ClusterFeature(List<Pin> cluster)
    {
        var points = cluster.Select(pin => ToWorld(pin.Location.Latitude, pin.Location.Longitude)).ToList();
        var feature = new PointFeature(points.Average(p => p.X), points.Average(p => p.Y)) { ["cluster"] = cluster };
        feature.Styles.Add(new SymbolStyle { Fill = new MapsuiBrush(Purple), Outline = new Pen(White, 2), SymbolScale = 1.1 });
        feature.Styles.Add(new LabelStyle
        {
            Text = cluster.Count.ToString(),
            ForeColor = White,
            BackColor = null,
            Font = new MapsuiFont { Size = 14, Bold = true },
        });
        return feature;
    }

    private static MPoint ToWorld(double latitude, double longitude)
    {
        var (x, y) = SphericalMercator.FromLonLat(longitude, Math.Clamp(latitude, -MaxLatitude, MaxLatitude));
        return new MPoint(x, y);
    }

    // The world is 256 pixels wide at zoom level 0.
    private static double Resolution(int zoomLevel) => World.Width / 256 / Math.Pow(2, zoomLevel);
}
