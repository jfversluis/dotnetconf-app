using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using DotNetConf.Core;
using DotNetConf.App.Services;
using Microsoft.Extensions.Logging;

namespace DotNetConf.App.ViewModels;

public sealed record DayFilter(string Label, DateOnly? Day)
{
    public override string ToString() => Label;
}

public partial class ConferenceViewModel(
    IConferenceDataSource dataSource, ScheduleService schedule, DetailNavigationService navigation,
    ILogger<ConferenceViewModel> logger) : ObservableObject
{
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private ConferenceDocument? document;
    private readonly List<SessionViewModel> allSessions = [];
    private readonly List<SpeakerViewModel> allSpeakers = [];
    private TimeZoneInfo timeZone = TimeZoneInfo.Local;
    private bool refreshingDays;
    private bool clearingTopics;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFilteredSessionsLabel))]
    public partial IReadOnlyList<SessionViewModel> Sessions { get; set; } = [];
    public ObservableCollection<SessionViewModel> SavedSessions { get; } = [];
    [ObservableProperty] public partial IReadOnlyList<SpeakerViewModel> Speakers { get; set; } = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommunityEventSummary))]
    public partial IReadOnlyList<CommunityEvent> CommunityEvents { get; set; } = [];
    public ObservableCollection<DayFilter> Days { get; } = [new("All days", null)];
    public ObservableCollection<TopicFilterViewModel> Topics { get; } = [];

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommunityEventSummary))]
    public partial bool IsMock { get; set; } = true;
    [ObservableProperty] public partial string ConferenceName { get; set; } = ".NET Conf 2026";
    [ObservableProperty] public partial string ConferenceDescription { get; set; } =
        "The .NET community comes together to celebrate, learn, and build what is next.";
    [ObservableProperty]
    public partial string SessionQuery { get; set; } = "";
    [ObservableProperty] public partial string SpeakerQuery { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDayIndex))]
    [NotifyPropertyChangedFor(nameof(FilterResultsContext))]
    public partial DayFilter? SelectedDay { get; set; }
    [ObservableProperty] public partial string TimeZoneLabel { get; set; } = $"Times shown in {TimeZoneInfo.Local.DisplayName}";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleSummary))]
    public partial int SavedCount { get; set; }

    public string ScheduleSummary => SavedCount == 1 ? "1 saved session" : $"{SavedCount} saved sessions";
    public int SelectedDayIndex
    {
        get => SelectedDay is null ? 0 : Math.Max(0, Days.IndexOf(SelectedDay));
        set
        {
            if (!refreshingDays && value >= 0 && value < Days.Count)
                SelectedDay = Days[value];
        }
    }
    public int AppliedFilterCount => Topics.Count(topic => topic.IsSelected);
    public bool HasTopicFilters => AppliedFilterCount > 0;
    public string? FilterBadgeText => AppliedFilterCount == 0 ? null : AppliedFilterCount.ToString();
    public string FilterToolbarLabel => AppliedFilterCount == 0
        ? "Filter topics" : $"Filter topics, {AppliedFilterCount} selected";
    public string FilterSummary => AppliedFilterCount switch
    {
        0 => "All topics",
        <= 2 => string.Join(" · ", Topics.Where(topic => topic.IsSelected).Select(topic => topic.Name)),
        _ => $"{AppliedFilterCount} topics selected"
    };
    public string FilterResultsContext => $"Results for {SelectedDay?.Label ?? Days[0].Label}. Your search still applies.";
    public string ShowFilteredSessionsLabel => Sessions.Count == 1
        ? "Show 1 session" : $"Show {Sessions.Count} sessions";
    public string CommunityEventSummary => CommunityEvents.Count == 0
        ? "Community events will appear here when they're available."
        : IsMock
            ? $"Explore {CommunityEvents.Count} fictional gatherings across {CommunityEvents.Select(e => e.City).Distinct().Count()} cities. These are examples, not events you can attend."
            : "Explore watch parties and in-person talks, with venue details and links to the organizers.";
    public event EventHandler? ScheduleChanged;

    public async Task InitializeAsync()
    {
        string? error;
        while ((error = await LoadAsync()) is not null)
        {
            if (!await MainThread.InvokeOnMainThreadAsync(() =>
                Shell.Current.DisplayAlertAsync("Couldn't load the agenda", error, "Retry", "Not now")))
                return;
        }
    }

    private async Task<string?> LoadAsync()
    {
        await loadGate.WaitAsync();
        try
        {
            if (document is not null)
            {
                RefreshLocalTimes();
                return null;
            }
            IsBusy = true;
            var loaded = await dataSource.LoadAsync();
            await schedule.InitializeAsync();
            document = loaded;
            ConferenceName = loaded.Name;
            ConferenceDescription = loaded.Description;
            IsMock = loaded.IsMock;
            CommunityEvents = loaded.CommunityEvents;
            allSpeakers.AddRange(loaded.Speakers.Select(speaker => new SpeakerViewModel(speaker, this)));
            allSessions.AddRange(loaded.Sessions.OrderBy(session => session.StartsAt)
                .Select(session => new SessionViewModel(session, allSpeakers, schedule.IsSaved(session.Id), this)));
            foreach (var topic in loaded.Sessions.SelectMany(session => session.Topics).Distinct().Order())
                Topics.Add(new(topic, OnTopicSelectionChanged));
            RefreshLocalTimes();
            FilterSpeakers();
            RefreshSchedule();
            return null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError(exception, "Could not load conference data or the saved schedule.");
            return "We couldn't load the agenda or saved schedule. Your saved file has not been replaced.";
        }
        finally
        {
            IsBusy = false;
            loadGate.Release();
        }
    }

    [RelayCommand]
    private void ClearTopicFilters()
    {
        clearingTopics = true;
        try
        {
            foreach (var topic in Topics)
                topic.IsSelected = false;
        }
        finally
        {
            clearingTopics = false;
        }
        OnTopicSelectionChanged();
    }

    private void OnTopicSelectionChanged()
    {
        if (clearingTopics)
            return;
        OnPropertyChanged(nameof(AppliedFilterCount));
        OnPropertyChanged(nameof(HasTopicFilters));
        OnPropertyChanged(nameof(FilterBadgeText));
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(FilterToolbarLabel));
        FilterSessions();
    }

    [RelayCommand]
    private Task BrowseSessionFiltersAsync() => Shell.Current.GoToAsync("session-filters");

    [RelayCommand]
    private Task BrowseSessionsAsync() => Shell.Current.GoToAsync("//sessions");

    [RelayCommand]
    private Task BrowseSpeakersAsync() => Shell.Current.GoToAsync("//speakers");

    [RelayCommand]
    private Task BrowseScheduleAsync() => Shell.Current.GoToAsync("//schedule");

    [RelayCommand]
    private Task BrowseCommunityEventsAsync() => Shell.Current.GoToAsync("//community");

    [RelayCommand]
    private Task ExploreCommunityMapAsync() => Shell.Current.GoToAsync("community-map");

    [RelayCommand]
    private Task BrowseCommunityListAsync() => Shell.Current.GoToAsync("community-map?mode=list");

    [RelayCommand]
    private Task OpenCommunityEventAsync(CommunityEvent? communityEvent) => communityEvent is null
        ? Task.CompletedTask
        : Shell.Current.GoToAsync($"community-event?id={Uri.EscapeDataString(communityEvent.Id)}");

    [RelayCommand]
    private Task BrowseAboutAsync() => Shell.Current.GoToAsync("about");

    [RelayCommand]
    private Task BrowseSettingsAsync() => Shell.Current.GoToAsync("settings");

    [RelayCommand]
    private async Task OpenCommunityWebsiteAsync(CommunityEvent? communityEvent)
    {
        if (communityEvent is null)
            return;
        try
        {
            if (!await Browser.OpenAsync(communityEvent.WebsiteUrl, BrowserLaunchMode.SystemPreferred))
                await ShowErrorAsync("Couldn't open the event website", $"Please visit {communityEvent.WebsiteUrl} in your browser.");
        }
        catch (Exception exception) when (exception is FeatureNotSupportedException or UriFormatException)
        {
            logger.LogError(exception, "Could not open community event website for {EventId}.", communityEvent.Id);
            await ShowErrorAsync("Couldn't open the event website", $"Please visit {communityEvent.WebsiteUrl} in your browser.");
        }
    }

    [RelayCommand]
    private async Task OpenWebsiteAsync()
    {
        try
        {
            if (!await Browser.OpenAsync(document?.WebsiteUrl ?? "https://www.dotnetconf.net/", BrowserLaunchMode.SystemPreferred))
                await ShowErrorAsync("Couldn't open the website",
                    "Please visit https://www.dotnetconf.net/ in your browser.");
        }
        catch (Exception exception) when (exception is FeatureNotSupportedException or UriFormatException)
        {
            logger.LogError(exception, "Could not open the conference website.");
            await ShowErrorAsync("Couldn't open the website",
                "Please visit https://www.dotnetconf.net/ in your browser.");
        }
    }

    public SessionViewModel? FindSession(string? id) => allSessions.Find(session => session.Id == id);
    public SpeakerViewModel? FindSpeaker(string? id) => allSpeakers.Find(speaker => speaker.Id == id);
    public Task OpenSessionAsync(string id) => navigation.OpenSessionAsync(id);
    public Task OpenSpeakerAsync(string id) => navigation.OpenSpeakerAsync(id);
    public IEnumerable<SessionViewModel> SessionsForSpeaker(string id) =>
        allSessions.Where(session => session.Model.SpeakerIds.Contains(id));

    public async Task ToggleSavedAsync(SessionViewModel session)
    {
        try
        {
            session.IsSaved = await schedule.ToggleAsync(session.Id);
            RefreshSchedule();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError(exception, "Could not save schedule changes.");
            await ShowErrorAsync("Couldn't update your schedule",
                "Your schedule has not changed. Check available storage and try the heart action again.");
            return;
        }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                Toast.Make(session.IsSaved ? "Added to My Schedule" : "Removed from My Schedule",
                    ToastDuration.Short).Show());
        }
        catch (Exception exception) when (exception is InvalidOperationException or FeatureNotSupportedException)
        {
            logger.LogError(exception, "Schedule updated, but the confirmation toast could not be shown.");
            await ShowErrorAsync("Schedule updated",
                "Your schedule was updated, but the confirmation couldn't be displayed.");
        }
    }

    private static Task ShowErrorAsync(string title, string message) =>
        MainThread.InvokeOnMainThreadAsync(() => Shell.Current.DisplayAlertAsync(title, message, "OK"));

    public void RefreshLocalTimes()
    {
        TimeZoneInfo.ClearCachedData();
        timeZone = TimeZoneInfo.Local;
        TimeZoneLabel = $"Times shown in {timeZone.DisplayName}";
        foreach (var session in allSessions)
            session.RefreshTime(timeZone);
        var selectedDay = SelectedDay?.Day;
        refreshingDays = true;
        try
        {
            Days.Clear();
            Days.Add(new("All days", null));
            foreach (var day in allSessions.Select(session => Agenda.LocalDay(session.Model.StartsAt, timeZone)).Distinct().Order())
                Days.Add(new(day.ToString("ddd, MMM d"), day));
            SelectedDay = Days.FirstOrDefault(day => day.Day == selectedDay) ?? Days[0];
        }
        finally
        {
            refreshingDays = false;
        }
        OnPropertyChanged(nameof(SelectedDayIndex));
        FilterSessions();
    }

    private void RefreshSchedule()
    {
        var saved = allSessions.Where(session => session.IsSaved).ToArray();
        foreach (var session in allSessions)
            session.HasConflict = session.IsSaved && saved.Any(other => Agenda.Overlaps(session.Model, other.Model));
        Replace(SavedSessions, saved);
        SavedCount = saved.Length;
        ScheduleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void FilterSessions()
    {
        if (document is null)
            return;
        var ids = Agenda.Search(document, SessionQuery, SelectedDay?.Day,
            Topics.Where(topic => topic.IsSelected).Select(topic => topic.Name).ToArray(),
            timeZone).Select(session => session.Id).ToHashSet();
        var results = allSessions.Where(session => ids.Contains(session.Id)).ToArray();
        if (!Sessions.SequenceEqual(results))
            Sessions = results;
    }

    private void FilterSpeakers()
    {
        var results = allSpeakers
            .Where(speaker => $"{speaker.Name} {speaker.Tagline}".Contains(SpeakerQuery.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(speaker => speaker.Name).ToArray();
        if (!Speakers.SequenceEqual(results))
            Speakers = results;
    }

    [RelayCommand]
    private void SearchSessions() => FilterSessions();

    [RelayCommand]
    private void SearchSpeakers() => FilterSpeakers();
    partial void OnSelectedDayChanged(DayFilter? value) => FilterSessions();

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        var snapshot = values.ToArray();
        target.Clear();
        foreach (var value in snapshot)
            target.Add(value);
    }
}
