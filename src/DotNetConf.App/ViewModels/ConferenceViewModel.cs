using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
    IConferenceRepository repository, ScheduleService schedule, WatchedSessionService watched, DetailNavigationService navigation,
    IBrowser browser, ISessionVideoLauncher videoLauncher, IConnectivity connectivity,
    ILogger<ConferenceViewModel> logger, ISessionReminderUpdates reminders, IConferenceSharing sharing,
    IHapticFeedback? haptics = null) : ObservableObject, IPlannerScheduleUpdates, IAskApplication
{
    private const string AgendaPresentationLoadError = "We couldn't display the agenda. Your saved choices are kept. Try again.";
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private readonly object snapshotSync = new();
    private int scheduleMutations;
    private ConferenceSnapshot? deferredSnapshot;
    private TaskCompletionSource? deferredPublication;
    private ConferenceDocument? document;
    private readonly List<SessionViewModel> allSessions = [];
    private readonly List<SpeakerViewModel> allSpeakers = [];
    private readonly RepublishingCollection<DayFilter> dayFilters = [new("All days", null)];
    private readonly RepublishingCollection<SessionViewModel> sessionResults = [];
    private TimeZoneInfo timeZone = TimeZoneInfo.Local;
    private bool refreshingDays;
    private bool clearingTopics;
    private string? askSpeakerFilter;
    private string? askSearchQuery;
    private bool askExactTitle;
    private bool startedRefresh;
    private ConferenceSnapshot? snapshot;
    private string? refreshError;
    private string? scheduleError;
    private string? schedulePresentationError;
    private string? watchedError;
    private bool schedulePresentationDirty;
    private bool watchedPresentationDirty;
    private bool localTimesDirty;
    private bool hasPublishedSnapshot;
    private CancellationTokenSource? refreshCancellation;
    private bool isPaused;
    private bool refreshOnResume;

    public ObservableCollection<SessionViewModel> Sessions => sessionResults;
    public ObservableCollection<SessionViewModel> SavedSessions { get; } = [];
    public ObservableCollection<SpeakerViewModel> Speakers { get; } = [];
    [ObservableProperty] public partial IReadOnlyList<SessionViewModel> SavedTimelineSessions { get; set; } = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommunityEventSummary))]
    [NotifyPropertyChangedFor(nameof(HasCommunityEvents))]
    [NotifyPropertyChangedFor(nameof(IsCommunityEventsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasMockCommunityEvents))]
    public partial IReadOnlyList<CommunityEvent> CommunityEvents { get; set; } = [];
    public bool HasCommunityEvents => CommunityEvents.Count > 0;
    public bool HasMockCommunityEvents => HasCommunityEvents && CommunityEventsAreMock;
    public bool IsCommunityEventsEmpty => document is not null && !HasCommunityEvents;
    public bool IsCommunityEventsLoading => document is null && (IsBusy || IsRefreshing);
    public ObservableCollection<DayFilter> Days => dayFilters;
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
    private bool NoPublishedVideos => HasAgenda && RecordingsOnly &&
        !document!.Sessions.Any(session => session.RecordingUrl.Length > 0);
    public string SessionsEmptyTitle => !HasAgenda ? AgendaEmptyTitle
        : NoPublishedVideos ? "No session videos linked yet" : "No sessions to show";
    public string SessionsEmptyMessage => HasAgenda
        ? NoPublishedVideos ? "The agenda doesn't have any session video links yet. Check back later or clear the video filter to browse sessions."
            : RecordingsOnly && HideWatched ? "No unwatched videos match these filters. Try changing your filters, search, or day."
            : RecordingsOnly ? "No videos match these filters. Clear the video filter or try a different search, day, or topic."
            : HideWatched ? "No unwatched sessions match these filters. Try changing your filters, search, or day."
            : "Try a different search, day, or topic." : AgendaEmptyMessage;
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
    [NotifyPropertyChangedFor(nameof(HasMockCommunityEvents))]
    public partial bool CommunityEventsAreMock { get; set; } = true;
    [ObservableProperty] public partial string AgendaNotice { get; set; } = "";
    [ObservableProperty] public partial string ConferenceName { get; set; } = ".NET Conf 2026";
    [ObservableProperty] public partial string ConferenceDescription { get; set; } =
        "The .NET community comes together to celebrate, learn, and build what is next.";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveSessionCriteria))]
    public partial string SessionQuery { get; set; } = "";
    [ObservableProperty] public partial string SpeakerQuery { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterSummary))]
    [NotifyPropertyChangedFor(nameof(FilterBadgeText))]
    [NotifyPropertyChangedFor(nameof(FilterToolbarLabel))]
    [NotifyPropertyChangedFor(nameof(SessionsEmptyTitle))]
    [NotifyPropertyChangedFor(nameof(SessionsEmptyMessage))]
    [NotifyPropertyChangedFor(nameof(HasSessionFilters))]
    [NotifyPropertyChangedFor(nameof(HasActiveSessionCriteria))]
    public partial bool RecordingsOnly { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterSummary))]
    [NotifyPropertyChangedFor(nameof(FilterBadgeText))]
    [NotifyPropertyChangedFor(nameof(FilterToolbarLabel))]
    [NotifyPropertyChangedFor(nameof(SessionsEmptyMessage))]
    [NotifyPropertyChangedFor(nameof(HasSessionFilters))]
    [NotifyPropertyChangedFor(nameof(HasActiveSessionCriteria))]
    [NotifyPropertyChangedFor(nameof(CanUseWatchedFilter))]
    public partial bool HideWatched { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDayIndex))]
    [NotifyPropertyChangedFor(nameof(FilterResultsContext))]
    [NotifyPropertyChangedFor(nameof(HasActiveSessionCriteria))]
    public partial DayFilter? SelectedDay { get; set; }
    [ObservableProperty] public partial string TimeZoneLabel { get; set; } = $"Times shown in {TimeZoneInfo.Local.DisplayName}";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleSummary))]
    [NotifyPropertyChangedFor(nameof(SchedulePlannerAction))]
    [NotifyPropertyChangedFor(nameof(SchedulePlannerHint))]
    [NotifyPropertyChangedFor(nameof(ScheduleEmptyTitle))]
    [NotifyPropertyChangedFor(nameof(ScheduleEmptyMessage))]
    public partial int SavedCount { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleSummary))]
    public partial bool IsScheduleAvailable { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseWatchedFilter))]
    public partial bool IsWatchedAvailable { get; set; }
    public bool CanUseWatchedFilter => IsWatchedAvailable || HideWatched;

    public string ScheduleSummary => !IsScheduleAvailable ? "Saved schedule unavailable"
        : SavedCount == 1 ? "1 saved session" : $"{SavedCount} saved sessions";
    public string CompactTimeZoneLabel => $"Local: {timeZone.Id}";
    public string SchedulePlannerAction => SavedCount > 0 ? "Find talks for my gaps" : "Discover sessions for my schedule";
    public string SchedulePlannerHint => SavedCount > 0
        ? "Explore a mix of talks around your saved sessions, or use optional on-device AI. Review before adding."
        : "Explore a varied mix of sessions, or describe your interests with optional on-device AI. Review before adding.";
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
    public bool HasAskSpeakerFilter => askSpeakerFilter is not null;
    public bool HasSessionFilters => HasTopicFilters || RecordingsOnly || HideWatched;
    public bool HasActiveSessionCriteria => HasSessionFilters || HasAskSpeakerFilter ||
        !string.IsNullOrWhiteSpace(SessionQuery) || SelectedDay?.Day is not null;
    private SessionViewModel? nextSavedSession;
    public SessionViewModel? NextSavedSession
    {
        get => nextSavedSession;
        private set
        {
            if (SetProperty(ref nextSavedSession, value))
                OnPropertyChanged(nameof(HasNextSavedSession));
        }
    }
    public bool HasNextSavedSession => NextSavedSession is not null;
    [ObservableProperty] public partial string NextSavedStatus { get; set; } = "";
    public string? FilterBadgeText
    {
        get
        {
            var count = AppliedFilterCount + (RecordingsOnly ? 1 : 0) + (HideWatched ? 1 : 0);
            return count > 0 ? count.ToString() : null;
        }
    }
    public string FilterToolbarLabel => $"Filter sessions. {FilterSummary}";
    private string TopicFilterSummary => AppliedFilterCount switch
    {
        0 => "All topics",
        <= 2 => string.Join(" · ", Topics.Where(topic => topic.IsSelected).Select(topic => topic.Name)),
        _ => $"{AppliedFilterCount} topics selected"
    };
    public string FilterSummary => (askSpeakerFilter is null ? TopicFilterSummary
        : $"{TopicFilterSummary} · Speaker: {askSpeakerFilter}") + (RecordingsOnly ? " · Video available" : "") +
        (HideWatched ? " · Hide watched" : "");
    public string FilterResultsContext => $"Results for {SelectedDay?.Label ?? Days[0].Label}. Your search still applies.";
    public string ShowFilteredSessionsLabel => Sessions.Count == 1
        ? "Show 1 session" : $"Show {Sessions.Count} sessions";
    public string CommunityEventSummary => CommunityEvents.Count == 0
        ? "No community events are listed in the app."
        : CommunityEventsAreMock
            ? $"Explore {CommunityEvents.Count} fictional gatherings across {CommunityEvents.Select(e => e.City).Distinct().Count()} cities. These are examples, not events you can attend."
            : "Explore watch parties and in-person talks, with venue details and links to the organizers.";
    public event EventHandler? ScheduleChanged;
    public event EventHandler? AgendaChanged;
    public ConferenceDocument? AskDocument => document;
    public TimeZoneInfo AskTimeZone => timeZone;

    public async Task OpenAskSearchAsync(AskIntent intent)
    {
        ApplyAskSearch(intent);
        await Shell.Current.GoToAsync("//sessions");
    }

    public void ApplyAskSearch(AskIntent intent)
    {
        if (document is null)
            throw new InvalidDataException("Download the agenda before searching.");
        var day = AskRequest.ResolveDay(document, intent, timeZone);
        clearingTopics = true;
        try
        {
            foreach (var topic in Topics)
                topic.IsSelected = false;
            RecordingsOnly = false;
            HideWatched = false;
            SessionQuery = intent.Query;
            askSearchQuery = intent.Query;
            askExactTitle = intent.ExactTitle;
            SetAskSpeakerFilter(intent.Speaker.Length == 0 ? null : intent.Speaker);
            SelectedDay = Days.Single(d => d.Day == day);
        }
        finally { clearingTopics = false; }
        OnTopicSelectionChanged();
        FilterSessions();
    }

    private void SetAskSpeakerFilter(string? speaker)
    {
        askSpeakerFilter = speaker;
        OnPropertyChanged(nameof(HasAskSpeakerFilter));
        OnPropertyChanged(nameof(HasActiveSessionCriteria));
        OnPropertyChanged(nameof(FilterSummary));
    }

    [RelayCommand]
    private void ClearAskSpeakerFilter()
    {
        SetAskSpeakerFilter(null);
        FilterSessions();
    }

    public async Task OpenAskScheduleAsync(bool remindersUpdated)
    {
        await Shell.Current.GoToAsync("//schedule");
        if (!remindersUpdated)
            await ShowErrorAsync("Schedule saved", "Your saved sessions are kept. Reminders need attention in Settings.");
    }

    public Task<bool> SaveAskSessionAsync(string id, CancellationToken token) => SaveAskSessionsAsync([id], token);

    public async Task<bool> SaveAskSessionsAsync(string[] ids, CancellationToken token)
    {
        lock (snapshotSync)
        {
            if (ids.Length == 0 || document is null || ids.Any(id => !document.Sessions.Any(session => session.Id == id)))
                throw new InvalidDataException("These sessions aren't in the current agenda. Ask again.");
            scheduleMutations++;
        }
        await CompleteScheduleMutationAsync(async () =>
        {
            await schedule.AddAsync(ids, token);
            return true;
        }, _ => "Your sessions were saved.");
        return await RefreshRemindersAfterScheduleMutationAsync();
    }

    public async Task<PlannerSaveResult?> AddPlannerProposalAsync(ConferenceDocument agenda, ScheduleProposal proposal,
        TimeZoneInfo zone, CancellationToken cancellationToken = default)
    {
        lock (snapshotSync)
        {
            if (document is null || !SchedulePlanner.HasSameAgenda(document, agenda))
                return null;
            scheduleMutations++;
        }
        ProposalReview? review = null;
        var mutationFinished = false;
        try
        {
            try
            {
                review = await schedule.AddProposalAsync(agenda, proposal, zone, cancellationToken)
                    ?? throw new InvalidOperationException("The schedule proposal could not be reviewed.");
                mutationFinished = true;
                if (review.CanApply)
                    RefreshSavedState();
            }
            finally { FinishScheduleMutation(mutationFinished); }
        }
        catch (Exception exception) when (review is { CanApply: true } && IsNonFatalCallbackFailure(exception))
        {
            ReportSchedulePresentationFailure(exception);
            return new(review, false);
        }
        return new(review, true);
    }

    private async Task<bool> SetSavedWithUpdatesAsync(string id, bool saved, CancellationToken token)
    {
        BeginScheduleMutation();
        await CompleteScheduleMutationAsync(() => schedule.SetSavedAsync(id, saved, token),
            result => result ? "The session was saved." : "The session was removed.");
        ConfirmInteraction();
        return await RefreshRemindersAfterScheduleMutationAsync();
    }

    public async Task<bool?> RemoveAskSessionsAsync(string[] ids, string[] expectedSavedIds,
        ConferenceDocument expectedDocument, CancellationToken token)
    {
        BeginScheduleMutation();
        var removed = await CompleteScheduleMutationAsync(
            () => schedule.RemoveReviewedAsync(ids, expectedSavedIds,
                () => ReferenceEquals(document, expectedDocument), token),
            result => result ? "Your reviewed sessions were removed." : "No sessions were removed.");
        return removed ? await RefreshRemindersAfterScheduleMutationAsync() : null;
    }

    private async Task<T> CompleteScheduleMutationAsync<T>(Func<Task<T>> mutation, Func<T, string> outcome)
    {
        var completed = false;
        var message = "";
        try
        {
            try
            {
                var result = await mutation();
                completed = true;
                message = outcome(result);
                RefreshSavedState();
                return result;
            }
            finally { FinishScheduleMutation(completed); }
        }
        catch (Exception exception) when (completed && IsNonFatalCallbackFailure(exception))
        {
            ReportSchedulePresentationFailure(exception);
            var remindersUpdated = await RefreshRemindersAfterScheduleMutationAsync();
            throw new SchedulePresentationException(message +
                " The schedule display couldn't refresh. Reopen My Schedule or tap Retry." +
                (remindersUpdated ? "" : " Reminders need attention in Settings."), exception);
        }
    }

    private void BeginScheduleMutation()
    {
        lock (snapshotSync)
            scheduleMutations++;
    }

    private void FinishScheduleMutation(bool mutationFinished)
    {
        if (mutationFinished)
        {
            FinishScheduleMutation();
            return;
        }
        try { FinishScheduleMutation(); }
        catch (Exception exception) when (IsNonFatalCallbackFailure(exception))
        {
            ReportSchedulePresentationFailure(exception);
        }
    }

    private void FinishScheduleMutation()
    {
        TaskCompletionSource? completion;
        lock (snapshotSync)
        {
            scheduleMutations--;
            if (scheduleMutations != 0 || deferredSnapshot is not { } latest)
                return;
            completion = deferredPublication;
            deferredPublication = null;
            try
            {
                ApplySnapshot(latest);
                deferredSnapshot = null;
                if (schedulePresentationDirty)
                    RefreshSavedState();
            }
            catch (Exception exception)
            {
                deferredSnapshot = latest;
                schedulePresentationDirty = true;
                completion?.TrySetException(IsNonFatalCallbackFailure(exception)
                    ? new SnapshotPublicationException(exception) : exception);
                throw;
            }
        }
        completion?.TrySetResult();
    }

    private Task PublishSnapshotAsync(ConferenceSnapshot value, bool waitForPublication = true)
    {
        lock (snapshotSync)
        {
            // A confirmed removal and an agenda update must have a single, consistent ordering.
            if (scheduleMutations > 0)
            {
                deferredSnapshot = value;
                return waitForPublication
                    ? (deferredPublication ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task
                    : Task.CompletedTask;
            }
            if (deferredSnapshot is not null)
                deferredSnapshot = value;
            try
            {
                ApplySnapshot(value);
                deferredSnapshot = null;
                if (schedulePresentationDirty)
                    RefreshSavedState();
            }
            catch (Exception exception)
            {
                deferredSnapshot = value;
                schedulePresentationDirty = true;
                if (IsNonFatalCallbackFailure(exception))
                    throw new SnapshotPublicationException(exception);
                throw;
            }
            return Task.CompletedTask;
        }
    }

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
                try { RefreshLocalTimes(); }
                catch (Exception exception) when (IsNonFatalCallbackFailure(exception))
                {
                    ReportSchedulePresentationFailure(exception);
                    return hasPublishedSnapshot ? null : AgendaPresentationLoadError;
                }
                if (schedulePresentationDirty)
                {
                    TryRefreshSavedState();
                    if (!hasPublishedSnapshot && schedulePresentationError is not null)
                        return AgendaPresentationLoadError;
                }
                return null;
            }
            IsBusy = true;
            var loaded = await repository.LoadAsync();
            await InitializeScheduleAsync();
            await InitializeWatchedAsync();
            await PublishSnapshotAsync(loaded);
            return null;
        }
        catch (SnapshotPublicationException exception)
        {
            ReportSchedulePresentationFailure(exception);
            return AgendaPresentationLoadError;
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
            await InitializeWatchedAsync(cancellation.Token);
            if (connectivity.NetworkAccess == NetworkAccess.None)
            {
                refreshError = "Connect to the internet, then try again.";
                return;
            }
            var refreshed = await repository.RefreshAsync(cancellation.Token,
                new Progress<ConferenceSnapshot>(value =>
                {
                    if (acceptingProgress)
                    {
                        try { _ = PublishSnapshotAsync(value, waitForPublication: false); }
                        catch (SnapshotPublicationException exception) { ReportSchedulePresentationFailure(exception); }
                    }
                }));
            acceptingProgress = false;
            await PublishSnapshotAsync(refreshed);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            logger.LogDebug("Agenda refresh canceled; keeping the last available agenda.");
            acceptingProgress = false;
            var retained = await repository.LoadAsync();
            try { await PublishSnapshotAsync(retained); }
            catch (SnapshotPublicationException exception) { ReportSchedulePresentationFailure(exception); }
        }
        catch (SnapshotPublicationException exception)
        {
            ReportSchedulePresentationFailure(exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
            UnauthorizedAccessException or JsonException or OperationCanceledException)
        {
            logger.LogError(exception, "Could not refresh the conference agenda.");
            refreshError = "Couldn't check for updates. Try again when your connection is ready.";
        }
        catch (Exception exception) when (IsSchedulePresentationException(exception))
        {
            ReportSchedulePresentationFailure(exception);
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
        OnPropertyChanged(nameof(HasConferenceDates));
        OnPropertyChanged(nameof(ConferenceDates));
        OnPropertyChanged(nameof(ConferenceDateContext));
        OnPropertyChanged(nameof(ConferenceShareText));
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
            session.IsWatched = watched.IsWatched(model.Id);
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
        UpdateRefreshStatus();
        AgendaChanged?.Invoke(this, EventArgs.Empty);
        hasPublishedSnapshot = true;
    }

    private void UpdateRefreshStatus()
    {
        var offline = connectivity.NetworkAccess == NetworkAccess.None;
        RefreshMessage = scheduleError ?? schedulePresentationError ?? watchedError ?? (offline
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
            nameof(SpeakersEmptyMessage), nameof(ScheduleEmptyTitle), nameof(ScheduleEmptyMessage),
            nameof(IsCommunityEventsEmpty), nameof(IsCommunityEventsLoading)
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
            await RefreshCommand.ExecuteAsync(null);
    }

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
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError(exception, "Could not read the saved schedule; agenda browsing remains available.");
            IsScheduleAvailable = false;
            scheduleError = "Your saved file couldn't be read and hasn't been changed. Agenda browsing still works.";
            foreach (var session in allSessions)
                session.ToggleSaveCommand.NotifyCanExecuteChanged();
            return;
        }
        var recovered = !IsScheduleAvailable;
        IsScheduleAvailable = true;
        scheduleError = null;
        try
        {
            if (recovered && snapshot is { } retained)
                await PublishSnapshotAsync(retained);
            TryRefreshSavedState();
        }
        catch (Exception exception) when (IsNonFatalCallbackFailure(exception))
        {
            ReportSchedulePresentationFailure(exception);
        }
        foreach (var session in allSessions)
            session.ToggleSaveCommand.NotifyCanExecuteChanged();
    }

    private async Task InitializeWatchedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await watched.ReloadAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError(exception, "Could not read watched sessions; agenda browsing remains available.");
            IsWatchedAvailable = false;
            watchedError = "Your watched status couldn't be read and hasn't been changed. Check storage and tap Retry.";
            foreach (var session in allSessions)
                session.ToggleWatchedCommand.NotifyCanExecuteChanged();
            UpdateRefreshStatus();
            return;
        }

        IsWatchedAvailable = true;
        try
        {
            RefreshWatchedState();
            watchedError = null;
            UpdateRefreshStatus();
        }
        catch (Exception exception) when (IsNonFatalCallbackFailure(exception))
        {
            ReportWatchedPresentationFailure(exception);
        }
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
        OnPropertyChanged(nameof(HasSessionFilters));
        OnPropertyChanged(nameof(HasActiveSessionCriteria));
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
    private void ClearSessionFilters()
    {
        RecordingsOnly = false;
        HideWatched = false;
        ClearTopicFilters();
    }

    [RelayCommand]
    private void ClearAllSessionCriteria()
    {
        clearingTopics = true;
        try
        {
            foreach (var topic in Topics)
                topic.IsSelected = false;
            RecordingsOnly = false;
            HideWatched = false;
            SessionQuery = "";
            askSearchQuery = null;
            askExactTitle = false;
            SetAskSpeakerFilter(null);
            SelectedDay = Days[0];
        }
        finally
        {
            clearingTopics = false;
        }
        OnTopicSelectionChanged();
    }

    partial void OnRecordingsOnlyChanged(bool value)
    {
        if (!clearingTopics)
            FilterSessions();
    }

    partial void OnHideWatchedChanged(bool value)
    {
        if (!clearingTopics)
            FilterSessions();
    }

    [RelayCommand]
    private Task BrowseSpeakersAsync() => Shell.Current.GoToAsync("//speakers");

    [RelayCommand]
    private Task BrowseScheduleAsync() => Shell.Current.GoToAsync("//schedule");

    [RelayCommand]
    private Task BrowsePlannerAsync() => Shell.Current.GoToAsync("planner");

    [RelayCommand]
    private Task FillScheduleGapsAsync() => Shell.Current.GoToAsync(SavedCount > 0 ? "planner?gaps=true" : "planner");

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
        await OpenLinkAsync(new(".NET Conf", ConferenceLinks.Website));
    }

    public SessionViewModel? FindSession(string? id) => allSessions.Find(session => session.Id == id);
    public SpeakerViewModel? FindSpeaker(string? id) => allSpeakers.Find(speaker => speaker.Id == id);
    public Task OpenSessionAsync(string id) => navigation.OpenSessionAsync(id);
    public Task OpenSpeakerAsync(string id) => navigation.OpenSpeakerAsync(id);
    public IEnumerable<SessionViewModel> SessionsForSpeaker(string id) =>
        allSessions.Where(session => session.Model.SpeakerIds.Contains(id));

    public async Task ToggleSavedAsync(SessionViewModel session)
    {
        bool remindersUpdated;
        try
        {
            remindersUpdated = await SetSavedWithUpdatesAsync(session.Id, !session.IsSaved, CancellationToken.None);
        }
        catch (SchedulePresentationException exception)
        {
            await ShowErrorAsync("Schedule updated", exception.Message);
            return;
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
                Toast.Make(!remindersUpdated ? "Schedule saved. Reminders need attention in Settings."
                    : session.IsSaved ? "Added to My Schedule" : "Removed from My Schedule",
                    ToastDuration.Short).Show());
        }
        catch (Exception exception) when (exception is InvalidOperationException or FeatureNotSupportedException)
        {
            logger.LogError(exception, "Schedule updated, but the confirmation toast could not be shown.");
            await ShowErrorAsync("Schedule updated",
                "Your schedule was updated, but the confirmation couldn't be displayed.");
        }
    }

    public async Task ToggleWatchedAsync(SessionViewModel session)
    {
        if (!IsWatchedAvailable || !session.HasWatchActions)
            return;

        try
        {
            await watched.SetWatchedAsync(session.Id, !session.IsWatched);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError(exception, "Could not update watched status for {SessionId}.", session.Id);
            watchedError = "Couldn't update your watched status. Check storage and try the action again.";
            UpdateRefreshStatus();
            return;
        }

        try
        {
            RefreshWatchedState();
            watchedError = null;
            UpdateRefreshStatus();
            ConfirmInteraction();
        }
        catch (Exception exception) when (IsNonFatalCallbackFailure(exception))
        {
            ReportWatchedPresentationFailure(exception);
        }
    }

    private void ConfirmInteraction()
    {
        try
        {
            if (haptics?.IsSupported == true)
                haptics.Perform(HapticFeedbackType.Click);
        }
        catch (Exception exception) when (exception is FeatureNotSupportedException or PermissionException)
        {
            logger.LogWarning(exception, "The session action completed, but haptic feedback is unavailable.");
        }
    }

    private static Task ShowErrorAsync(string title, string message) =>
        Shell.Current.Dispatcher.DispatchAsync(() => Shell.Current.DisplayAlertAsync(title, message, "OK"));

    public void RefreshLocalTimes()
    {
        var republish = localTimesDirty;
        localTimesDirty = true;
        TimeZoneInfo.ClearCachedData();
        timeZone = TimeZoneInfo.Local;
        TimeZoneLabel = $"Times shown in {timeZone.DisplayName}";
        if (republish)
            OnPropertyChanged(nameof(TimeZoneLabel));
        OnPropertyChanged(nameof(CompactTimeZoneLabel));
        foreach (var session in allSessions)
            session.RefreshTime(timeZone, republish);
        var selectedDay = SelectedDay?.Day;
        refreshingDays = true;
        try
        {
            var days = new[] { new DayFilter("All days", null) }.Concat(
                allSessions.Where(session => !session.IsWithdrawn)
                    .Select(session => Agenda.LocalDay(session.Model.StartsAt, timeZone)).Distinct().Order()
                    .Select(day => new DayFilter($"{day:ddd}, {Agenda.MonthDay(day)}", day)));
            CollectionUpdates.Apply(Days, days);
            if (republish)
                dayFilters.Republish();
            SelectedDay = Days.FirstOrDefault(day => day.Day == selectedDay) ?? Days[0];
        }
        finally
        {
            refreshingDays = false;
        }
        if (republish)
        {
            OnPropertyChanged(nameof(SelectedDay));
            OnPropertyChanged(nameof(FilterResultsContext));
        }
        OnPropertyChanged(nameof(SelectedDayIndex));
        FilterSessions();
        RefreshSessionStatusesCore(DateTimeOffset.UtcNow);
        if (republish)
        {
            sessionResults.Republish();
            OnPropertyChanged(nameof(ShowFilteredSessionsLabel));
        }
        localTimesDirty = false;
    }

    private sealed class RepublishingCollection<T> : ObservableCollection<T>
    {
        public void Republish()
        {
            // A failed collection callback may have left some observers unaware of an applied change.
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    public void RefreshSavedState()
    {
        var republish = schedulePresentationDirty;
        schedulePresentationDirty = true;
        foreach (var session in allSessions)
            session.IsSaved = schedule.IsSaved(session.Id);
        RefreshSchedule();
        if (republish)
        {
            OnPropertyChanged(nameof(NextSavedSession));
            OnPropertyChanged(nameof(HasNextSavedSession));
            OnPropertyChanged(nameof(NextSavedStatus));
        }
        if (deferredSnapshot is null && !localTimesDirty)
        {
            schedulePresentationDirty = false;
            if (schedulePresentationError is not null)
            {
                schedulePresentationError = null;
                UpdateRefreshStatus();
            }
        }
    }

    private void RefreshWatchedState()
    {
        var republish = watchedPresentationDirty;
        watchedPresentationDirty = true;
        foreach (var session in allSessions)
        {
            session.IsWatched = watched.IsWatched(session.Id);
            session.ToggleWatchedCommand.NotifyCanExecuteChanged();
            if (republish)
                session.RepublishWatchedState();
        }
        FilterSessions();
        if (republish)
        {
            sessionResults.Republish();
            OnPropertyChanged(nameof(ShowFilteredSessionsLabel));
        }
        watchedPresentationDirty = false;
    }

    private void ReportWatchedPresentationFailure(Exception exception)
    {
        watchedPresentationDirty = true;
        watchedError = "Your watched status is kept, but the display couldn't refresh. Tap Retry.";
        logger.LogError(exception, "Could not refresh the watched-session presentation.");
        try { UpdateRefreshStatus(); }
        catch (Exception notificationException) when (IsNonFatalCallbackFailure(notificationException))
        {
            logger.LogError(notificationException, "Could not notify the watched-session presentation error.");
            try { HasRefreshError = true; }
            catch (Exception visibilityException) when (IsNonFatalCallbackFailure(visibilityException))
            {
                logger.LogError(visibilityException, "Could not notify the watched-session error visibility.");
            }
        }
    }

    private void TryRefreshSavedState()
    {
        try
        {
            lock (snapshotSync)
            {
                if (scheduleMutations > 0)
                    return;
                if (deferredSnapshot is { } latest)
                {
                    ApplySnapshot(latest);
                    deferredSnapshot = null;
                }
                if (localTimesDirty)
                    RefreshLocalTimes();
                RefreshSavedState();
            }
        }
        catch (Exception exception) when (IsNonFatalCallbackFailure(exception))
        {
            ReportSchedulePresentationFailure(exception);
        }
    }

    private void ReportSchedulePresentationFailure(Exception exception)
    {
        schedulePresentationDirty = true;
        schedulePresentationError = "Your saved choices are kept, but the schedule display couldn't refresh. Reopen My Schedule or tap Retry.";
        logger.LogError(exception, "Could not refresh the saved schedule presentation.");
        try { UpdateRefreshStatus(); }
        catch (Exception notificationException) when (IsNonFatalCallbackFailure(notificationException))
        {
            logger.LogError(notificationException, "Could not notify the schedule presentation error.");
            try { HasRefreshError = true; }
            catch (Exception visibilityException) when (IsNonFatalCallbackFailure(visibilityException))
            {
                logger.LogError(visibilityException, "Could not notify the schedule error visibility.");
            }
        }
    }

    private static bool IsSchedulePresentationException(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or NotSupportedException or ArgumentException or NullReferenceException
#if IOS
            or Foundation.NSErrorException
#endif
            ;

    private sealed class SnapshotPublicationException(Exception innerException)
        : Exception("Could not publish the schedule presentation.", innerException);

    // UI and reminder callbacks can throw arbitrary exceptions after the schedule is already durable.
    private static bool IsNonFatalCallbackFailure(Exception exception) =>
        exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    private async Task<bool> RefreshRemindersAfterScheduleMutationAsync()
    {
        try { return await reminders.RefreshAsync(); }
        catch (Exception exception) when (IsNonFatalCallbackFailure(exception))
        {
            logger.LogError(exception, "Could not refresh session reminders after a schedule change.");
            return false;
        }
    }

    public Task<bool> RefreshRemindersAsync() => RefreshRemindersAfterScheduleMutationAsync();

    private void RefreshSchedule()
    {
        var saved = allSessions.Where(session => session.IsSaved).ToArray();
        foreach (var session in allSessions)
            session.HasConflict = session.IsSaved && saved.Any(other => Agenda.Overlaps(session.Model, other.Model));
        CollectionUpdates.Apply(SavedSessions, saved);
        SavedTimelineSessions = saved;
        SavedCount = schedule.SavedCount;
        RefreshSessionStatusesCore(DateTimeOffset.UtcNow);
        ScheduleChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshSessionStatuses(DateTimeOffset now)
    {
        try { RefreshSessionStatusesCore(now); }
        catch (Exception exception) when (IsNonFatalCallbackFailure(exception))
        {
            ReportSchedulePresentationFailure(exception);
        }
    }

    private void RefreshSessionStatusesCore(DateTimeOffset now)
    {
        foreach (var session in allSessions)
            session.RefreshLiveStatus(now);
        var next = SavedSessions.FirstOrDefault(session => !session.IsWithdrawn && session.Model.EndsAt > now);
        NextSavedSession = next;
        NextSavedStatus = next is null ? "" : next.Model.StartsAt <= now ? "Happening now" : "Up next";
    }

    private void FilterSessions()
    {
        if (document is null || clearingTopics)
            return;
        var speakers = askSpeakerFilter is null ? null
            : AskRequest.FindSpeakers(document, askSpeakerFilter).Select(speaker => speaker.Id).ToHashSet(StringComparer.Ordinal);
        var topics = Topics.Where(topic => topic.IsSelected).Select(topic => topic.Name).ToArray();
        var usingAskSearch = askSearchQuery is not null && SessionQuery.Equals(askSearchQuery, StringComparison.Ordinal);
        var matches = usingAskSearch
            ? AskRequest.Search(document, SessionQuery, SelectedDay?.Day, topics, timeZone,
                includeSpeakerNames: askSpeakerFilter is null)
            : Agenda.Search(document, SessionQuery, SelectedDay?.Day, topics, timeZone);
        if (usingAskSearch && askExactTitle)
            matches = matches.Where(session => session.Title.Equals(SessionQuery, StringComparison.OrdinalIgnoreCase));
        var ids = matches.Where(session => (!RecordingsOnly || session.RecordingUrl.Length > 0) &&
                (!HideWatched || !watched.IsWatched(session.Id)) &&
                (speakers is null || session.SpeakerIds.Any(speakers.Contains)))
            .Select(session => session.Id).ToHashSet();
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
    partial void OnSessionQueryChanged(string value)
    {
        if (askSearchQuery is not null && !value.Equals(askSearchQuery, StringComparison.Ordinal))
        {
            askSearchQuery = null;
            askExactTitle = false;
        }
    }

    [RelayCommand]
    private void SearchSpeakers() => FilterSpeakers();
    partial void OnSelectedDayChanged(DayFilter? value) => FilterSessions();

}
