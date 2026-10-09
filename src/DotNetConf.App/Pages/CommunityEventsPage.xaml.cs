using DotNetConf.App.ViewModels;
using DotNetConf.App.Controls;
using DotNetConf.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using Map = Microsoft.Maui.Controls.Maps.Map;

namespace DotNetConf.App.Pages;

public partial class CommunityEventsPage : ContentPage, IQueryAttributable
{
    private Map? map;
    private CommunityEvent? selectedEvent;
    private bool showMap;
    private readonly IFontManager fontManager;
    private readonly ILogger<CommunityEventsPage> logger;

    public ConferenceViewModel Conference { get; }
    public ImageSource? WatchPartyPin { get; private set; }
    public ImageSource? InPersonPin { get; private set; }
    public Command<CommunityEvent> SelectEventCommand { get; }
    public bool CanShowMap { get; private set; } = IsMapConfigured();
    public bool HasAvailableMap => CanShowMap && Conference.CommunityEvents.Count > 0;
    public bool ShowMap => showMap;
    public bool ShowList => !showMap;
    public string ExplorerTitle => ShowMap ? "Event map" : "Community events";
    public bool HasSelection => selectedEvent is not null;
    public double DetailHeight => Height * 0.35;
    public string ListHelp => !CanShowMap
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
            if (ShowList && value is not null && map is not null)
                map.MoveToRegion(MapSpan.FromCenterAndRadius(
                    new Location(value.Latitude, value.Longitude), Distance.FromKilometers(2)));
        }
    }

    public CommunityEventsPage(ConferenceViewModel conference, IFontManager fontManager, ILogger<CommunityEventsPage> logger)
    {
        this.fontManager = fontManager;
        this.logger = logger;
        Conference = conference;
        SelectEventCommand = new Command<CommunityEvent>(communityEvent => SelectedEvent = communityEvent);
        showMap = CanShowMap;
        InitializeComponent();
        BindingContext = this;
        SizeChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(DetailHeight));
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
        OnPropertyChanged(nameof(HasAvailableMap));
        if (Conference.CommunityEvents.Count == 0)
        {
            SetMode(false);
            return;
        }
        if (map is not null)
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
        if (!CanShowMap)
            return;
        map = new Map
        {
            IsShowingUser = false,
            IsClusteringEnabled = true,
            AutomationId = "community-map"
        };
        SemanticProperties.SetHint(map, "Tap a pin for a preview. Tap a number to zoom.");
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
            };
            map.Pins.Add(pin);
        }
        MapHost.Add(map);
        ShowAll();
    }

    private void OnShowAllClicked(object? sender, EventArgs e) => ShowAll();

    private void ShowAll()
    {
        if (map is null || Conference.CommunityEvents.Count == 0)
            return;
        map.MoveToRegion(MapSpan.FromLocations(
            Conference.CommunityEvents.Select(e => new Location(e.Latitude, e.Longitude))).WithZoom(0.8));
    }

    private void OnDismissClicked(object? sender, EventArgs e) => SelectedEvent = null;
    private void OnMapClicked(object? sender, EventArgs e) => SetMode(true);
    private void OnListClicked(object? sender, EventArgs e) => SetMode(false);

    private void SetMode(bool value)
    {
        showMap = value && CanShowMap && Conference.CommunityEvents.Count > 0;
        OnPropertyChanged(nameof(ShowMap));
        OnPropertyChanged(nameof(ShowList));
        OnPropertyChanged(nameof(ListHelp));
        OnPropertyChanged(nameof(ExplorerTitle));
    }

    internal static bool IsMapConfigured()
    {
#if ANDROID
        var context = Android.App.Application.Context;
        var application = context.PackageManager!.GetApplicationInfo(
            context.PackageName!, Android.Content.PM.PackageInfoFlags.MetaData);
        var key = application?.MetaData?.GetString("com.google.android.geo.API_KEY");
        return !string.IsNullOrWhiteSpace(key) && key != "NOT_CONFIGURED";
#else
        return true;
#endif
    }
}
