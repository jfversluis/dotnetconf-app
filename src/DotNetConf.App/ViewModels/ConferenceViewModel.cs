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
    IConferenceRepository repository, ScheduleService schedule, DetailNavigationService navigation,
    IBrowser browser, IConnectivity connectivity, SpeakerImageCache images,
    ILogger<ConferenceViewModel> logger) : ObservableObject
{
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private ConferenceDocument? document;
    private readonly List<SessionViewModel> allSessions = [];
    private readonly List<SpeakerViewModel> allSpeakers = [];
    private TimeZoneInfo timeZone = TimeZoneInfo.Local;
    private bool refreshingDays;
    private bool clearingTopics;
    private bool startedRefresh;
    private ConferenceSnapshot? snapshot;
    private string? refreshError;
    private string? scheduleError;
    private CancellationTokenSource? refreshCancellation;
    private bool isPaused;
    private bool refreshOnResume;

    public ObservableCollection<SessionViewModel> Sessions { get; } = [];
    public ObservableCollection<SessionViewModel> SavedSessions { get; } = [];
    public ObservableCollection<SpeakerViewModel> Speakers { get; } = [];
    [ObservableProperty] public partial IReadOnlyList<SessionViewModel> AllSessions { get; set; } = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommunityEventSummary))]
    [NotifyPropertyChangedFor(nameof(HasCommunityEvents))]
    public partial IReadOnlyList<CommunityEvent> CommunityEvents { get; set; } = [];
    public bool HasCommunityEvents => CommunityEvents.Count > 0;
    public ObservableCollection<DayFilter> Days { get; } = [new("All days", null)];
    public ObservableCollection<TopicFilterViewModel> Topics { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefresh))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial bool IsBusy { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyPropertyChangedFor(nameof(CanRefresh))]
    public partial bool IsRefreshing { get; set; }
    public bool CanRefresh => !IsBusy && !IsRefreshing;
    public bool HasAgenda => document?.Sessions.Count > 0;
    public bool IsInitialAgendaLoading => !HasAgenda && (IsBusy || IsRefreshing);
    public string AgendaEmptyTitle => IsInitialAgendaLoading ? "Loading the agenda"
        : "Download the agenda";
    public string AgendaEmptyMessage => IsInitialAgendaLoading
        ? "Getting the latest sessions and speakers."
        : "Connect to the internet, then pull down to refresh and download sessions and speakers. Once downloaded, they are available offline.";
    public string SessionsEmptyTitle => HasAgenda ? "No sessions to show" : AgendaEmptyTitle;
    public string SessionsEmptyMessage => HasAgenda
        ? "Try a different search, day, or topic." : AgendaEmptyMessage;
    public string SpeakersEmptyTitle => !HasAgenda ? AgendaEmptyTitle
        : document!.Speakers.Count == 0 ? "Speakers to be announced" : "No speakers to show";
    public string SpeakersEmptyMessage => !HasAgenda ? AgendaEmptyMessage
        : document!.Speakers.Count == 0 ? "Speaker details are not available yet. Pull down to refresh and check for updates."
        : "Try a different search.";
    public string ScheduleEmptyTitle => !HasAgenda ? AgendaEmptyTitle
        : SavedCount > 0 ? "Waiting for your saved sessions" : "Make it your conference";
    public string ScheduleEmptyMessage => !HasAgenda
        ? AgendaEmptyMessage + " Any previously saved choices are kept."
        : SavedCount > 0 ? "Your saved choices are kept. Pull down to refresh and download their session details."
        : "Save sessions from the agenda to build your personal schedule.";
    [ObservableProperty] public partial bool IsPullRefreshing { get; set; }
    [ObservableProperty] public partial string RefreshMessage { get; set; } = "";
    [ObservableProperty] public partial bool HasRefreshError { get; set; }
    [ObservableProperty]
    public partial bool IsMock { get; set; } = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommunityEventSummary))]
    public partial bool CommunityEventsAreMock { get; set; } = true;
    [ObservableProperty] public partial string AgendaNotice { get; set; } = "";
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
    [NotifyPropertyChangedFor(nameof(ScheduleEmptyTitle))]
    [NotifyPropertyChangedFor(nameof(ScheduleEmptyMessage))]
    public partial int SavedCount { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleSummary))]
    public partial bool IsScheduleAvailable { get; set; }

    public string ScheduleSummary => !IsScheduleAvailable ? "Saved schedule unavailable"
        : SavedCount == 1 ? "1 saved session" : $"{SavedCount} saved sessions";
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
        : CommunityEventsAreMock
            ? $"Explore {CommunityEvents.Count} fictional gatherings across {CommunityEvents.Select(e => e.City).Distinct().Count()} cities. These are examples, not events you can attend."
            : "Explore watch parties and in-person talks, with venue details and links to the organizers.";
    public event EventHandler? ScheduleChanged;
    public event EventHandler? AgendaChanged;

    public async Task InitializeAsync()
    {
        string? error;
        while ((error = await LoadAsync()) is not null)
        {
            if (!await Shell.Current.Dispatcher.DispatchAsync(() =>
                Shell.Current.DisplayAlertAsync("Couldn't load the agenda", error, "Retry", "Not now")))
                return;
        }
        if (!startedRefresh)
        {
            startedRefresh = true;
            connectivity.ConnectivityChanged += OnConnectivityChanged;
            RefreshCommand.Execute(null);
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
            var loaded = await repository.LoadAsync();
            await InitializeScheduleAsync();
            ApplySnapshot(loaded);
            if (HasAgenda)
                await Task.Run(() => images.Trim(loaded.Document.Speakers.Select(speaker => speaker.ProfilePicture)));
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
    private void RequestRefresh()
    {
        if (!IsRefreshing && RefreshCommand.CanExecute(null))
            RefreshCommand.Execute(null);
        IsPullRefreshing = IsRefreshing;
    }

    [RelayCommand(CanExecute = nameof(CanRefresh), IncludeCancelCommand = true)]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (document is null || IsRefreshing || isPaused)
            return;

        IsRefreshing = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        refreshCancellation = cancellation;
        var acceptingProgress = true;
        try
        {
            await InitializeScheduleAsync(cancellation.Token);
            if (connectivity.NetworkAccess == NetworkAccess.None)
            {
                refreshError = "Connect to the internet, then try again.";
                return;
            }
            var refreshed = await repository.RefreshAsync(cancellation.Token,
                new Progress<ConferenceSnapshot>(value =>
                {
                    if (acceptingProgress)
                        ApplySnapshot(value);
                }));
            acceptingProgress = false;
            ApplySnapshot(refreshed);
            await RefreshPicturesAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            logger.LogDebug("Agenda refresh canceled; keeping the last available agenda.");
            acceptingProgress = false;
            var retained = await repository.LoadAsync();
            ApplySnapshot(retained);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
            UnauthorizedAccessException or JsonException or OperationCanceledException)
        {
            logger.LogError(exception, "Could not refresh the conference agenda.");
            refreshError = "Couldn't check for updates. Try again when your connection is ready.";
        }
        finally
        {
            acceptingProgress = false;
            refreshCancellation = null;
            IsRefreshing = false;
            IsPullRefreshing = false;
            UpdateRefreshStatus();
            if (refreshOnResume && !isPaused)
            {
                refreshOnResume = false;
                await RefreshAsync();
            }
        }
    }

    private void ApplySnapshot(ConferenceSnapshot value)
    {
        snapshot = value;
        foreach (var issue in value.Issues)
            logger.LogWarning(issue.Exception, "Conference data issue: {Source}.", issue.Source);
        var sourceIssues = value.Issues.Where(issue => issue.Source != ConferenceRepository.CacheSource).ToArray();
        var metadataIssues = sourceIssues.Where(issue => issue.Source == ConferenceRepository.MetadataSource).ToArray();
        var invalidSources = sourceIssues.Where(issue => issue.Source != ConferenceRepository.MetadataSource &&
                issue.Exception is InvalidDataException or JsonException)
            .Select(issue => issue.Source).Distinct().ToArray();
        var failedSources = sourceIssues.Where(issue => issue.Source != ConferenceRepository.MetadataSource &&
                issue.Exception is not (InvalidDataException or JsonException))
            .Select(issue => issue.Source).Distinct().ToArray();
        var sourceErrors = new List<string>();
        if (metadataIssues.Length > 0)
            sourceErrors.Add("Couldn't apply the conference date metadata; the cached agenda is unchanged.");
        if (invalidSources.Length > 0)
            sourceErrors.Add($"Couldn't use the {string.Join(" and ", invalidSources)} agenda update because its data was invalid. Retry later.");
        if (failedSources.Length > 0)
            sourceErrors.Add($"Couldn't download {string.Join(" and ", failedSources)} sessions. Retry when your connection is ready.");
        refreshError = value.Issues.Any(issue => issue.Source == ConferenceRepository.CacheSource)
            ? "Couldn't read or save the offline agenda. Check storage and retry."
            : sourceErrors.Count > 0 ? string.Join(" ", sourceErrors) : null;
        var loaded = value.Document;
        document = loaded;
        NotifyAgendaState();
        ConferenceName = loaded.Name;
        ConferenceDescription = loaded.Description;
        IsMock = loaded.IsMock;
        CommunityEventsAreMock = loaded.CommunityEventsAreMock;
        AgendaNotice = loaded.AgendaNotice;
        CommunityEvents = loaded.CommunityEvents;

        var speakerIds = loaded.Speakers.Select(speaker => speaker.Id).ToHashSet(StringComparer.Ordinal);
        var savedWithdrawnSpeakerIds = loaded.WithdrawnSessions.Where(session => schedule.IsSaved(session.Id))
            .SelectMany(session => session.SpeakerIds).ToHashSet(StringComparer.Ordinal);
        foreach (var model in loaded.Speakers.Concat(loaded.WithdrawnSpeakers.Where(speaker => savedWithdrawnSpeakerIds.Contains(speaker.Id))))
        {
            var speaker = FindSpeaker(model.Id);
            if (speaker is null)
                allSpeakers.Add(new(model, this));
            else
                speaker.Update(model);
        }
        var sessionIds = loaded.Sessions.Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
        var models = loaded.Sessions.Concat(loaded.WithdrawnSessions.Where(session => schedule.IsSaved(session.Id))).ToArray();
        var retainedIds = models.Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
        allSessions.RemoveAll(session => !retainedIds.Contains(session.Id));
        foreach (var model in models)
        {
            var session = FindSession(model.Id);
            if (session is null)
            {
                session = new(model, allSpeakers, schedule.IsSaved(model.Id), this);
                allSessions.Add(session);
            }
            else
                session.Update(model, allSpeakers);
            session.IsSaved = schedule.IsSaved(model.Id);
            session.IsWithdrawn = !sessionIds.Contains(model.Id);
        }
        allSessions.Sort((left, right) => left.Model.StartsAt.CompareTo(right.Model.StartsAt));
        var retainedSpeakerIds = allSessions.SelectMany(session => session.Model.SpeakerIds).ToHashSet(StringComparer.Ordinal);
        allSpeakers.RemoveAll(speaker => !speakerIds.Contains(speaker.Id) && !retainedSpeakerIds.Contains(speaker.Id));
        foreach (var speaker in allSpeakers)
            speaker.UpdateSessions(SessionsForSpeaker(speaker.Id));
        var selectedTopics = Topics.Where(topic => topic.IsSelected).Select(topic => topic.Name);
        var topics = loaded.Sessions.SelectMany(session => session.Topics).Concat(selectedTopics).Distinct().Order()
            .Select(name => Topics.FirstOrDefault(topic => topic.Name == name) ?? new TopicFilterViewModel(name, OnTopicSelectionChanged))
            .ToArray();
        CollectionUpdates.Apply(Topics, topics);
        RefreshLocalTimes();
        FilterSpeakers();
        RefreshSchedule();
        AllSessions = allSessions.Where(session => !session.IsWithdrawn).ToArray();
        UpdateRefreshStatus();
        AgendaChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RefreshPicturesAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var speakers = allSpeakers.ToArray();
        try
        {
            await Parallel.ForEachAsync(speakers, new ParallelOptions
            {
                MaxDegreeOfParallelism = 3,
                CancellationToken = timeout.Token
            }, async (speaker, token) =>
            {
                var url = speaker.Model.ProfilePicture;
                var path = await images.GetAsync(url, token);
                if (path is not null)
                    await MainThread.InvokeOnMainThreadAsync(() => speaker.SetProfilePicture(url, path));
            });
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Speaker photo refresh timed out; existing photos remain available.");
        }
        finally
        {
            if (HasAgenda)
                await Task.Run(() => images.Trim(speakers.Select(speaker => speaker.Model.ProfilePicture)));
        }
    }

    private void UpdateRefreshStatus()
    {
        var offline = connectivity.NetworkAccess == NetworkAccess.None;
        RefreshMessage = scheduleError ?? (offline
            ? HasAgenda ? "You're offline. Your downloaded agenda is still available."
                : "You're offline. Connect to download the agenda."
            : refreshError ?? "");
        HasRefreshError = RefreshMessage.Length > 0;
    }

    private void NotifyAgendaState()
    {
        foreach (var name in new[]
        {
            nameof(HasAgenda), nameof(IsInitialAgendaLoading), nameof(AgendaEmptyTitle), nameof(AgendaEmptyMessage),
            nameof(SessionsEmptyTitle), nameof(SessionsEmptyMessage), nameof(SpeakersEmptyTitle),
            nameof(SpeakersEmptyMessage), nameof(ScheduleEmptyTitle), nameof(ScheduleEmptyMessage)
        })
            OnPropertyChanged(name);
    }

    partial void OnIsBusyChanged(bool value) => NotifyAgendaState();
    partial void OnIsRefreshingChanged(bool value) => NotifyAgendaState();

    public async Task ResumeAsync()
    {
        isPaused = false;
        if (IsRefreshing)
        {
            refreshOnResume = refreshCancellation?.IsCancellationRequested == true;
            return;
        }
        var restartCanceledRefresh = refreshOnResume;
        refreshOnResume = false;
        var now = DateTimeOffset.UtcNow;
        if (document is not null && !IsRefreshing &&
            (restartCanceledRefresh || HasRefreshError || snapshot?.LastCheckedAt is not { } last || now < last || now - last > TimeSpan.FromMinutes(5)))
            await RefreshAsync();
    }

    internal string? FindCachedPhoto(string url) => images.FindCached(url);
    internal void ReportPhotoFailure(string id, Exception exception) =>
        logger.LogWarning(exception, "Could not display speaker portrait for {SpeakerId}; using the fallback.", id);
    public void PauseRefresh()
    {
        isPaused = true;
        refreshOnResume |= IsRefreshing;
        refreshCancellation?.Cancel();
    }

    private async Task InitializeScheduleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await schedule.InitializeAsync(cancellationToken);
            var recovered = !IsScheduleAvailable;
            IsScheduleAvailable = true;
            scheduleError = null;
            if (recovered && snapshot is { } retained)
                ApplySnapshot(retained);
            else
            {
                foreach (var session in allSessions)
                    session.IsSaved = schedule.IsSaved(session.Id);
                RefreshSchedule();
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError(exception, "Could not read the saved schedule; agenda browsing remains available.");
            IsScheduleAvailable = false;
            scheduleError = "Your saved file couldn't be read and hasn't been changed. Agenda browsing still works.";
        }
        foreach (var session in allSessions)
            session.ToggleSaveCommand.NotifyCanExecuteChanged();
    }

    private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs args)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            UpdateRefreshStatus();
            if (args.NetworkAccess == NetworkAccess.Internet && !isPaused)
                await ResumeAsync();
        });
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
            if (!await browser.OpenAsync(communityEvent.WebsiteUrl, BrowserLaunchMode.SystemPreferred))
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
            if (!await browser.OpenAsync(document?.WebsiteUrl ?? "https://www.dotnetconf.net/", BrowserLaunchMode.SystemPreferred))
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
            session.IsSaved = await schedule.SetSavedAsync(session.Id, !session.IsSaved);
            // Also shows sessions saved in another browser tab.
            foreach (var other in allSessions)
                other.IsSaved = schedule.IsSaved(other.Id);
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
            await Shell.Current.Dispatcher.DispatchAsync(() =>
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
        Shell.Current.Dispatcher.DispatchAsync(() => Shell.Current.DisplayAlertAsync(title, message, "OK"));

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
            var days = new[] { new DayFilter("All days", null) }.Concat(
                allSessions.Where(session => !session.IsWithdrawn)
                    .Select(session => Agenda.LocalDay(session.Model.StartsAt, timeZone)).Distinct().Order()
                    .Select(day => new DayFilter($"{day:ddd}, {Agenda.MonthDay(day)}", day)));
            CollectionUpdates.Apply(Days, days);
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
        CollectionUpdates.Apply(SavedSessions, saved);
        SavedCount = schedule.SavedCount;
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
        {
            CollectionUpdates.Apply(Sessions, results);
            OnPropertyChanged(nameof(ShowFilteredSessionsLabel));
        }
    }

    private void FilterSpeakers()
    {
        var currentSpeakerIds = document?.Speakers.Select(speaker => speaker.Id)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        var results = allSpeakers
            .Where(speaker => currentSpeakerIds.Contains(speaker.Id) &&
                $"{speaker.Name} {speaker.Tagline}".Contains(SpeakerQuery.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(speaker => speaker.Name).ToArray();
        if (!Speakers.SequenceEqual(results))
            CollectionUpdates.Apply(Speakers, results);
    }

    [RelayCommand]
    private void SearchSessions() => FilterSessions();

    [RelayCommand]
    private void SearchSpeakers() => FilterSpeakers();
    partial void OnSelectedDayChanged(DayFilter? value) => FilterSessions();

}
