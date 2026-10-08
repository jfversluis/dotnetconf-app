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

    [ObservableProperty] public partial IReadOnlyList<SessionViewModel> Sessions { get; set; } = [];
    public ObservableCollection<SessionViewModel> SavedSessions { get; } = [];
    [ObservableProperty] public partial IReadOnlyList<SpeakerViewModel> Speakers { get; set; } = [];
    public ObservableCollection<DayFilter> Days { get; } = [new("All days", null)];
    public ObservableCollection<string> Topics { get; } = ["All topics"];

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsMock { get; set; } = true;
    [ObservableProperty] public partial string ConferenceName { get; set; } = ".NET Conf 2026";
    [ObservableProperty] public partial string ConferenceDescription { get; set; } =
        "The .NET community comes together to celebrate, learn, and build what is next.";
    [ObservableProperty] public partial string SessionQuery { get; set; } = "";
    [ObservableProperty] public partial string SpeakerQuery { get; set; } = "";
    [ObservableProperty] public partial DayFilter? SelectedDay { get; set; }
    [ObservableProperty] public partial string? SelectedTopic { get; set; }
    [ObservableProperty] public partial string TimeZoneLabel { get; set; } = $"Times shown in {TimeZoneInfo.Local.DisplayName}";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleSummary))]
    public partial int SavedCount { get; set; }

    public string ScheduleSummary => SavedCount == 1 ? "1 saved session" : $"{SavedCount} saved sessions";
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
            allSpeakers.AddRange(loaded.Speakers.Select(speaker => new SpeakerViewModel(speaker, this)));
            allSessions.AddRange(loaded.Sessions.OrderBy(session => session.StartsAt)
                .Select(session => new SessionViewModel(session, allSpeakers, schedule.IsSaved(session.Id), this)));
            foreach (var topic in loaded.Sessions.SelectMany(session => session.Topics).Distinct().Order())
                Topics.Add(topic);
            SelectedTopic = Topics[0];
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
    private void ResetFilters()
    {
        SessionQuery = "";
        SelectedDay = Days[0];
        SelectedTopic = Topics[0];
        FilterSessions();
    }

    [RelayCommand]
    private Task BrowseSessionsAsync() => Shell.Current.GoToAsync("//sessions");

    [RelayCommand]
    private Task BrowseSpeakersAsync() => Shell.Current.GoToAsync("//speakers");

    [RelayCommand]
    private Task BrowseScheduleAsync() => Shell.Current.GoToAsync("//schedule");

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
        Days.Clear();
        Days.Add(new("All days", null));
        foreach (var day in allSessions.Select(session => Agenda.LocalDay(session.Model.StartsAt, timeZone)).Distinct().Order())
            Days.Add(new(day.ToString("ddd, MMM d"), day));
        SelectedDay = Days.FirstOrDefault(day => day.Day == selectedDay) ?? Days[0];
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
            SelectedTopic == "All topics" ? null : SelectedTopic, timeZone).Select(session => session.Id).ToHashSet();
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
    partial void OnSelectedTopicChanged(string? value) => FilterSessions();

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        var snapshot = values.ToArray();
        target.Clear();
        foreach (var value in snapshot)
            target.Add(value);
    }
}
