using DotNetConf.App.ViewModels;
using DotNetConf.App.Controls;
using DotNetConf.App.Services;
using DotNetConf.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using Syncfusion.Maui.Toolkit.BottomSheet;
using Map = Microsoft.Maui.Controls.Maps.Map;

namespace DotNetConf.App.Pages;

public partial class CommunityEventsPage : ContentPage, IQueryAttributable
{
    private Map? map;
    private CommunityEvent? selectedEvent;
    private bool showMap;
    private bool isSplit;
    private double measuredTextScale = 1;
    private readonly FoldableViewport viewport;
    private readonly TextScaleMonitor textScale = new();
    private readonly IFontManager fontManager;
    private readonly ILogger<CommunityEventsPage> logger;
    private readonly Dictionary<int, ImageSource> clusterImages = [];

    public ConferenceViewModel Conference { get; }
    public ImageSource? WatchPartyPin { get; private set; }
    public ImageSource? InPersonPin { get; private set; }
    public Command<CommunityEvent> SelectEventCommand { get; }
    public bool CanShowMap { get; private set; } = IsMapConfigured();
    public bool HasAvailableMap => CanShowMap && Conference.CommunityEvents.Count > 0;
    public bool IsSplit => isSplit;
    public bool IsCompact => !isSplit;
    public bool ShowMap => HasAvailableMap && (isSplit || showMap);
    public bool ShowList => isSplit || !ShowMap;
    public bool ShowModeControls => HasAvailableMap && !isSplit;
    public string MapModeDescription => ShowMap ? "Map, selected" : "Map";
    public string ListModeDescription => ShowList ? "List, selected" : "List";
    public bool ShowExplorerHeadline => IsCompact && measuredTextScale <= 1.3 &&
        (Window?.Height ?? Height) >= 600;
    public string ExplorerTitle => "Local events";
    public string LocationSummary => $"{Conference.CommunityEvents.Count} locations · {Conference.CommunityEvents.Select(e => e.City).Distinct().Count()} cities";
    public string SelectedLocation => selectedEvent is null ? "" : $"{selectedEvent.City}, {selectedEvent.Country}";
    public ImageSource? SelectedPin => selectedEvent?.Kind == "watch-party" ? WatchPartyPin : InPersonPin;
    public bool HasSelection => selectedEvent is not null;
    public string ListHelp => Conference.CommunityEvents.Count == 0
        ? "Community listings need an official event source, which is not connected yet."
        : !CanShowMap
        ? "Map unavailable in this build. Explore the events below."
        : "Watch parties and in-person talks. Select an event to find out more.";

    public CommunityEvent? SelectedEvent
    {
        get => selectedEvent;
        set
        {
            if (selectedEvent == value)
                return;
            selectedEvent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedLocation));
            OnPropertyChanged(nameof(SelectedPin));
        }
    }

    private void SelectFromList(CommunityEvent communityEvent)
    {
        SelectedEvent = communityEvent;
        OpenEventSheet();
        if (map is not null)
            map.MoveToRegion(MapSpan.FromCenterAndRadius(
                new Location(communityEvent.Latitude, communityEvent.Longitude), Distance.FromKilometers(2)));
    }

    public CommunityEventsPage(ConferenceViewModel conference, IFontManager fontManager,
        ILogger<CommunityEventsPage> logger, IFoldableRegionSource? foldableRegionSource = null)
    {
        this.fontManager = fontManager;
        this.logger = logger;
        Conference = conference;
        SelectEventCommand = new Command<CommunityEvent>(SelectFromList);
        showMap = CanShowMap;
        InitializeComponent();
        BindingContext = this;
        Panes.Add(textScale);
        textScale.TextScaleChanged += (_, _) => UpdateLayout();
#if IOS
        viewport = new FoldableViewport(Panes, Panes, UpdateLayout, foldableRegionSource);
#else
        viewport = new FoldableViewport(Panes, this, UpdateLayout, foldableRegionSource);
#endif
        SizeChanged += (_, _) => UpdateLayout();
        HeaderControls.SizeChanged += (_, _) => UpdateLayout();
        EventSheet.SizeChanged += (_, _) =>
        {
            EventSheet.ContentWidthMode = BottomSheetContentWidthMode.Custom;
            EventSheet.BottomSheetContentWidth = Math.Min(720, Math.Max(0, EventSheet.Width));
        };
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("mode", out var value) && value is string mode && mode == "list")
            SetMode(false);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Conference.InitializeAsync();
        OnPropertyChanged(nameof(LocationSummary));
        OnPropertyChanged(nameof(HasAvailableMap));
        UpdateLayout();
        if (Conference.CommunityEvents.Count == 0)
        {
            SetMode(false);
            return;
        }
        if (map is not null || !CanShowMap)
            return;

        try
        {
            WatchPartyPin = CommunityPinImages.Create(true, fontManager);
            InPersonPin = CommunityPinImages.Create(false, fontManager);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Could not create community map pin images.");
            CanShowMap = false;
            OnPropertyChanged(nameof(CanShowMap));
            OnPropertyChanged(nameof(HasAvailableMap));
            SetMode(false);
            await DisplayAlertAsync("Couldn't load the map",
                "The event list is still available. Close and reopen the app to try again.", "OK");
            return;
        }
        OnPropertyChanged(nameof(WatchPartyPin));
        OnPropertyChanged(nameof(InPersonPin));
        map = new Map
        {
            IsShowingUser = false,
            IsClusteringEnabled = true,
            ClusterImageProvider = cluster =>
            {
                lock (clusterImages)
                {
                    if (!clusterImages.TryGetValue(cluster.Count, out var image))
                    {
                        image = CommunityPinImages.CreateCluster(cluster.Count);
                        clusterImages.Add(cluster.Count, image);
                    }
                    return image;
                }
            },
            AutomationId = "community-map"
        };
        SemanticProperties.SetHint(map, "Pan to explore worldwide locations. Tap a pin for a preview. Tap a number to zoom.");
        foreach (var communityEvent in Conference.CommunityEvents)
        {
            var pin = new Pin
            {
                Label = communityEvent.Name,
                Address = $"{communityEvent.KindLabel}. {communityEvent.LocationLabel}",
                Location = new Location(communityEvent.Latitude, communityEvent.Longitude),
                ClusteringIdentifier = "community-events",
                ImageSource = communityEvent.Kind == "watch-party" ? WatchPartyPin : InPersonPin
            };
            pin.MarkerClicked += (_, args) =>
            {
                args.HideInfoWindow = true;
                SelectedEvent = communityEvent;
                OpenEventSheet();
            };
            map.Pins.Add(pin);
        }
        map.SizeChanged += OnMapFirstLayout;
        MapHost.Add(map);
    }

    private void OnMapFirstLayout(object? sender, EventArgs e)
    {
        if (map is null || map.Width <= 0 || map.Height <= 0)
            return;
        map.SizeChanged -= OnMapFirstLayout;
        if (selectedEvent is not null)
            map.MoveToRegion(MapSpan.FromCenterAndRadius(
                new Location(selectedEvent.Latitude, selectedEvent.Longitude), Distance.FromKilometers(2)));
        else
            ShowAll();
    }

    private void OnShowAllClicked(object? sender, EventArgs e) => ShowAll();

    private void ShowAll()
    {
        if (map is null || Conference.CommunityEvents.Count == 0)
            return;
        CommunityMapViewport.ShowAll(map, Conference.CommunityEvents);
    }

    private void OpenEventSheet()
    {
        EventSheet.AnimationDuration = AskGlow.ReduceMotion ? 0 : 200;
        EventSheet.State = BottomSheetState.FullExpanded;
        EventSheet.Show();
    }

    private void OnDismissClicked(object? sender, EventArgs e) => EventSheet.Close();

    private void OnEventSheetStateChanged(object? sender, StateChangedEventArgs e)
    {
        if (e.NewState == BottomSheetState.Collapsed)
            EventSheet.Close();
    }

    private async void OnEventDetailsClicked(object? sender, EventArgs e)
    {
        var communityEvent = SelectedEvent;
        if (communityEvent is null)
            return;
        EventSheet.Close();
        await Task.Delay(AskGlow.ReduceMotion ? 0 : 220);
        await Conference.OpenCommunityEventCommand.ExecuteAsync(communityEvent);
    }

    protected override void OnDisappearing()
    {
        EventSheet.Close();
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (!EventSheet.IsOpen)
            return base.OnBackButtonPressed();
        EventSheet.Close();
        return true;
    }
    private void OnMapClicked(object? sender, EventArgs e) => SetMode(true);
    private void OnListClicked(object? sender, EventArgs e) => SetMode(false);

    private void SetMode(bool value)
    {
        showMap = value && CanShowMap && Conference.CommunityEvents.Count > 0;
        UpdateLayout();
    }

    internal static DetailLayout AdaptExplorerLayout(DetailLayout layout, bool mapAvailable,
        double contentHeight, double scale)
    {
        // A tabletop map needs room for native attribution, its legend, and a usable viewport.
        if (!mapAvailable || layout.IsStacked &&
            Math.Min(layout.ListLength.Value, contentHeight - layout.ListLength.Value - layout.HingeLength.Value) < 320 * Math.Max(1, scale))
            return new(false, false, GridLength.Star, new(0), new(0));
        return layout;
    }

    internal static bool ShouldStackModeControls(double width, double scale) =>
        width / Math.Max(1, scale) < 320;

    private void UpdateLayout()
    {
        if (viewport is null)
            return;
        var scale = textScale.TextScale ?? 1;
        measuredTextScale = scale;
        var layout = AdaptExplorerLayout(
            viewport.GetLayout(Window?.Width ?? Width, Window?.Height ?? Height, scale),
            HasAvailableMap, Panes.Height, scale);
        isSplit = layout.IsSplit;
        CommunityPaneLayout.Apply(Panes, ListPane, MapPane, layout);
        var stackModes = ShouldStackModeControls(HeaderControls.Width, scale);
        Grid.SetColumn(ModeSwitch, stackModes ? 0 : 1);
        Grid.SetRow(ModeSwitch, stackModes ? 1 : 0);
        Grid.SetColumnSpan(ModeSwitch, stackModes ? 2 : 1);
        Grid.SetColumnSpan(LocationCount, stackModes || !ShowModeControls ? 2 : 1);
        HeaderControls.RowDefinitions[1].Height = stackModes && ShowModeControls ? GridLength.Auto : new(0);
        HeaderControls.RowSpacing = stackModes && ShowModeControls ? 10 : 0;
        OnPropertyChanged(nameof(IsSplit));
        OnPropertyChanged(nameof(IsCompact));
        OnPropertyChanged(nameof(ShowMap));
        OnPropertyChanged(nameof(ShowList));
        OnPropertyChanged(nameof(ShowModeControls));
        OnPropertyChanged(nameof(MapModeDescription));
        OnPropertyChanged(nameof(ListModeDescription));
        OnPropertyChanged(nameof(ShowExplorerHeadline));
        OnPropertyChanged(nameof(ListHelp));
    }

    internal static bool IsMapConfigured()
    {
#if ANDROID
        var context = Android.App.Application.Context;
        var application = context.PackageManager!.GetApplicationInfo(
            context.PackageName!, Android.Content.PM.PackageInfoFlags.MetaData);
        var key = application?.MetaData?.GetString("com.google.android.geo.API_KEY");
        return !string.IsNullOrWhiteSpace(key) && key != "NOT_CONFIGURED";
#elif AVALONIA
        // Avalonia.Controls.Maui has no Map handler yet, so these targets use the event list.
        return false;
#else
        return true;
#endif
    }
}
