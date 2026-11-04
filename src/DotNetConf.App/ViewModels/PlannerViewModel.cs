using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetConf.App.Services;
using DotNetConf.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetConf.App.ViewModels;

public partial class ProposedSessionViewModel(Session session, string speakerNames, TimeZoneInfo zone,
    bool selected, string warning, Action changed) : ObservableObject
{
    public string Id => session.Id;
    public string AutomationId => $"planner-session-{Id}";
    public string WarningAutomationId => $"planner-warning-{Id}";
    public string SelectionAutomationId => $"planner-select-{Id}";
    public string Title => session.Title;
    public string SpeakerNames => speakerNames;
    public string Time => $"{TimeZoneInfo.ConvertTime(session.StartsAt, zone):ddd, MMM d HH:mm} - " +
        $"{TimeZoneInfo.ConvertTime(session.EndsAt, zone):ddd, MMM d HH:mm}";
    public string Topics => string.Join(" · ", session.Topics);
    public DateTimeOffset StartsAt => session.StartsAt;
    public DateTimeOffset EndsAt => session.EndsAt;
    public string Warning => string.Join(" ", new[] { warning, Conflict }.Where(value => value.Length > 0));
    public bool HasWarning => Warning.Length > 0;
    public bool CanSelect => warning.Length == 0 && !IsBusy;
    public string ChoiceLabel => warning.StartsWith("Already saved", StringComparison.Ordinal) ? "Already saved"
        : warning.Length > 0 ? "Can't fit"
        : IsSelected ? "Selected to add" : "Alternative - not selected";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning))]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    [NotifyPropertyChangedFor(nameof(SelectionDescription))]
    public partial string Conflict { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelect))]
    public partial bool IsBusy { get; set; }
    public string SelectionDescription => $"{Title}. {SpeakerNames}. {Time}." +
        (HasWarning ? $" {Warning}" : "");
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChoiceLabel))]
    public partial bool IsSelected { get; set; } = selected && warning.Length == 0;
    partial void OnIsSelectedChanged(bool value) => changed();
}

public partial class PlannerViewModel(
    [FromKeyedServices("agenda")] IConferenceDataSource dataSource, ScheduleService schedule, IOnDevicePlanner planner,
    IPlannerScheduleUpdates scheduleUpdates, ILogger<PlannerViewModel> logger) : ObservableObject
{
    private CancellationTokenSource? operation;
    private ConferenceDocument? document;
    private ScheduleProposal? proposal;
    private TimeZoneInfo timeZone = TimeZoneInfo.Local;
    private bool initialized;
    private CancellationTokenSource? startingPointsOperation;
    private CancellationTokenSource? modelOperation;
    private bool stopped;
    public bool IsGapMode { get; private set; }
    public string Heading => IsGapMode ? "Make room\nfor discovery." : "Your conference,\nyour way.";
    public string Introduction => IsGapMode
        ? "Find talks in the free time on your saved conference days. Your existing schedule stays exactly as it is."
        : IsAiMode
            ? "Describe your interests with on-device AI. Choose which suggested talks to add to your schedule."
            : IsCheckingModel || !HasCheckedModel
                ? "Checking on-device AI availability to prepare your session suggestions."
                : "Explore a varied mix of topics from the downloaded agenda. Choose what to add to your schedule.";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiscoverySource))]
    [NotifyPropertyChangedFor(nameof(Introduction))]
    public partial bool IsAiMode { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExplore))]
    [NotifyPropertyChangedFor(nameof(Introduction))]
    [NotifyPropertyChangedFor(nameof(CanRetryModel))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    public partial bool IsCheckingModel { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExplore))]
    [NotifyPropertyChangedFor(nameof(Introduction))]
    public partial bool HasCheckedModel { get; set; }
    [ObservableProperty]
    public partial string ModelStatus { get; set; } = "Checking whether on-device AI is available.";
    public bool CanExplore => HasCheckedModel && !IsReady && !IsCheckingModel;
    public string DiscoverySource => IsAiMode
        ? "On-device AI interpreted your request. Review its matches before adding."
        : "A varied random mix from the downloaded agenda. Not personalized; no AI is used.";
    public int SelectedCount => Sessions.Count(session => session.IsSelected);
    public string SelectionSummary => $"{SelectedCount} selected. Checked sessions are not saved until you choose Add.";
    public string ApplyLabel => $"Add {SelectedCount} to My Schedule";
    public bool CanRelaxConstraints => proposal is not null &&
        (proposal.RequiredSpeakers.Length > 0 || proposal.TopicFilter?.IncludeAll == true);
    public string RequestLabel => IsGapMode ? "What would fit your day?" : "What interests you?";
    public string GenerateLabel => IsGapMode ? "Find sessions for my gaps" : "Find sessions for me";
    public string AgendaDisclosure => document?.IsMock == true
        ? "Fictional demo agenda. Talks and participation are not announced or endorsed."
        : "Suggestions use the downloaded Microsoft agenda. Review session times before saving.";

    public void EnableGapMode()
    {
        IsGapMode = true;
        Request = "I see gaps in my schedule. What sessions fit in here?";
        foreach (var property in new[] { nameof(Heading), nameof(Introduction), nameof(RequestLabel), nameof(GenerateLabel) })
            OnPropertyChanged(property);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    public partial string Request { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanRetryModel))]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    public partial bool IsBusy { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    public partial bool IsSaving { get; set; }
    public bool CanCancel => IsBusy && !IsSaving;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanRetryModel))]
    [NotifyPropertyChangedFor(nameof(CanExplore))]
    public partial bool IsReady { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; set; } = "";
    [ObservableProperty] public partial string Constraints { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    public partial string Validation { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(HasValidationErrors))]
    public partial bool HasValidProposal { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    public partial bool ConstraintsConfirmed { get; set; }
    [ObservableProperty] public partial bool HasProposal { get; set; }
    [ObservableProperty] public partial bool HasAddedSessions { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchingDetailsAction))]
    public partial bool ShowMatchingDetails { get; set; }
    public ObservableCollection<ProposedSessionViewModel> Sessions { get; } = [];
    [ObservableProperty]
    public partial IReadOnlyList<PlannerStartingPoint> StartingPoints { get; set; } =
    [
        new("Feed my AI + mobile curiosity", "I'm interested in AI and mobile development."),
        new("Web, hold the boring bits", "I'd like to learn about web development and Blazor."),
        new("Down the .NET rabbit hole", "I'm new to .NET. Suggest a varied introduction to the conference.")
    ];
    [ObservableProperty]
    public partial bool IsLoadingStartingPoints { get; set; } = true;
    public bool CanGenerate => IsReady && CanEdit && !string.IsNullOrWhiteSpace(Request);
    public bool CanEdit => !IsBusy && !IsCheckingModel;
    public bool CanRetryModel => !IsReady && !IsCheckingModel && !IsBusy && planner.UnsupportedReason is null;
    public bool CanApply => HasValidProposal && CanEdit;
    public bool CanConfirm => HasValidProposal && CanEdit;
    public event EventHandler<string>? OutcomeAvailable;
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public bool HasValidationErrors => !HasValidProposal && !string.IsNullOrWhiteSpace(Validation);
    public string MatchingDetailsAction => ShowMatchingDetails ? "Hide matching details" : "Show matching details";
    public string TimeZoneLabel => $"Use dates and times in {TimeZoneInfo.Local.Id}, or explicitly name another time zone.";

    [RelayCommand]
    private void ToggleMatchingDetails() => ShowMatchingDetails = !ShowMatchingDetails;

    [RelayCommand]
    private void UseExample(string example)
    {
        if (CanEdit)
            Request = example;
    }

    public async Task InitializeAsync()
    {
        if (initialized)
            return;
        initialized = true;
        await CheckModelAsync();
    }

    [RelayCommand]
    private async Task ExploreAsync()
    {
        if (IsBusy || !CanExplore)
            return;
        startingPointsOperation?.Cancel();
        IsAiMode = false;
        ClearProposal();
        IsBusy = true;
        Status = "Finding a mix of sessions from the downloaded agenda...";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        operation = cancellation;
        try
        {
            document = await dataSource.LoadAsync(cancellation.Token);
            await schedule.ReloadAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            timeZone = TimeZoneInfo.Local;
            OnPropertyChanged(nameof(AgendaDisclosure));
            if (document.Sessions.Count == 0)
            {
                Status = "Download the agenda first. Connect to the internet and refresh Sessions, then choose Explore a mix again.";
                return;
            }
            proposal = SchedulePlanner.Discover(document, timeZone, SavedIds(), IsGapMode);
            PopulateSessions();
            Status = Sessions.Count == 0
                ? "No new sessions fit around your saved talks. Your schedule hasn't changed."
                : "";
        }
        catch (OperationCanceledException)
        {
            Status = "Discovery cancelled or timed out. Your saved sessions haven't changed.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            logger.LogWarning(exception, "Could not discover agenda sessions.");
            Status = "Couldn't load suggestions. Check the agenda download and try Explore a mix again. Your saved sessions are kept.";
        }
        finally
        {
            operation = null;
            IsBusy = false;
            NotifySelection();
            AnnounceOutcome();
        }
    }

    [RelayCommand]
    private async Task CheckModelAsync()
    {
        if (IsCheckingModel || IsBusy)
            return;
        IsReady = false;
        IsLoadingStartingPoints = true;
        IsCheckingModel = true;
        ModelStatus = "Checking whether on-device AI is available.";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        modelOperation = cancellation;
        try
        {
            if (planner.UnsupportedReason is not null)
                ModelStatus = "On-device AI isn't supported on this device. You can still explore the agenda or build My Schedule with hearts.";
            else
            {
                await planner.CheckReadinessAsync(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                IsReady = true;
                ModelStatus = "On-device AI is available. No account or cloud fallback.";
            }
        }
        catch (OperationCanceledException)
        {
            ModelStatus = "Model check cancelled or timed out. Explore a mix, or retry AI.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            if (cancellation.IsCancellationRequested)
                ModelStatus = "Model check cancelled or timed out. Explore a mix, or retry AI.";
            else
            {
                logger.LogWarning(exception, "On-device planner readiness check failed.");
                ModelStatus = "On-device AI isn't ready on this device. Explore a mix from the downloaded agenda, or check your device's AI settings and retry. No model download starts automatically.";
            }
        }
        finally
        {
            modelOperation = null;
            HasCheckedModel = true;
            IsCheckingModel = false;
            OnPropertyChanged(nameof(CanRetryModel));
            if (!IsReady)
                IsLoadingStartingPoints = false;
        }
        if (stopped)
            return;
        if (IsReady)
        {
            IsAiMode = true;
            ClearProposal();
            Status = "";
            _ = RefreshStartingPointsAsync();
        }
        else
        {
            if (IsAiMode)
                ClearProposal();
            IsAiMode = false;
            if (!HasProposal)
                await ExploreAsync();
        }
    }

    private async Task RefreshStartingPointsAsync()
    {
        startingPointsOperation?.Cancel();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        startingPointsOperation = cancellation;
        IsLoadingStartingPoints = true;
        try
        {
            var agenda = await dataSource.LoadAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (agenda.Sessions.Count == 0)
                throw new InvalidDataException("Download the agenda before requesting suggestions.");
            cancellation.CancelAfter(TimeSpan.FromSeconds(20));
            var suggestions = await planner.SuggestStartingPointsAsync(agenda, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (suggestions.Count == 0)
                throw new InvalidDataException("The local model returned no starting points.");
            if (ReferenceEquals(startingPointsOperation, cancellation))
                StartingPoints = suggestions;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Local planner starting point refresh cancelled or timed out.");
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            logger.LogWarning(exception, "Could not refresh local planner starting points.");
        }
        finally
        {
            if (ReferenceEquals(startingPointsOperation, cancellation))
            {
                startingPointsOperation = null;
                IsLoadingStartingPoints = false;
            }
        }
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (!CanGenerate)
            return;
        IsAiMode = true;
        ClearProposal();
        startingPointsOperation?.Cancel();
        IsBusy = true;
        Status = "Searching the real agenda with the on-device model...";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        operation = cancellation;
        try
        {
            document = await dataSource.LoadAsync(cancellation.Token);
            if (document.Sessions.Count == 0)
            {
                Status = "Download the agenda before planning. Connect to the internet and refresh Sessions, then try again. Your saved sessions are kept.";
                return;
            }
            await schedule.ReloadAsync(cancellation.Token);
            timeZone = TimeZoneInfo.Local;
            OnPropertyChanged(nameof(AgendaDisclosure));
            var result = await planner.ProposeAsync(document, Request, timeZone, SavedIds(), cancellation.Token, IsGapMode);
            cancellation.Token.ThrowIfCancellationRequested();
            proposal = result.Proposal;
            if (proposal is null)
            {
                Status = string.IsNullOrWhiteSpace(result.Message)
                    ? "The model did not submit a proposal. Try a shorter, more specific request."
                    : result.Message[..Math.Min(result.Message.Length, 1600)];
                return;
            }
            Constraints = (IsGapMode ? "Only free time on days with saved sessions, including before and after them. Saved sessions stay fixed.\n" : "") +
                $"Required speakers: {(proposal.RequiredSpeakers.Length == 0 ? "None" : string.Join(", ", proposal.RequiredSpeakers))}\n" +
                (proposal.AllSessionsByRequiredSpeakers ? "Include all sessions by these speakers.\n" : "") +
                (proposal.TopicFilter is { } filter
                    ? $"Topics: {string.Join(filter.MatchAll ? " AND " : " OR ", filter.Topics)}" +
                        (filter.RestrictToRequiredSpeakers ? " (only these speakers)" : " (plus independent speaker requests)") + ".\n" +
                        (filter.IncludeAll ? "Include all matching topic sessions within availability.\n" : "")
                    : "") +
                $"Availability: {(proposal.Availability.Length == 0 ? "Unrestricted" : string.Join("; ", proposal.Availability.Select(w => $"{w.Start} to {w.End} ({(w.Zone == "local" ? timeZone.Id : w.Zone)})")))}";
            PopulateSessions();
            Status = Sessions.Count == 0
                ? "No schedule was proposed. Check the explanation below; your likes have not changed."
                : "";
        }
        catch (OperationCanceledException)
        {
            Status = "Planning cancelled or timed out. Your liked sessions have not changed.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            if (cancellation.IsCancellationRequested)
                Status = "Planning cancelled or timed out. Your liked sessions have not changed.";
            else
            {
                logger.LogWarning(exception, "On-device schedule proposal failed.");
                Status = "Couldn't create a local proposal. The model may be unavailable or the request too complex. Try fewer constraints, or skip. Your likes have not changed.";
            }
        }
        finally
        {
            operation = null;
            IsBusy = false;
            AnnounceOutcome();
        }
    }

    private string[] SavedIds() => schedule.SavedIds;

    private void PopulateSessions()
    {
        if (document is null || proposal is null)
            return;
        var saved = SavedIds();
        var matchedIds = proposal.MatchedSessionIds.Concat(proposal.SessionIds).ToHashSet(StringComparer.Ordinal);
        foreach (var session in document.Sessions.Where(s => matchedIds.Contains(s.Id))
            .OrderByDescending(s => proposal.SessionIds.Contains(s.Id)).ThenBy(s => s.StartsAt))
        {
            var warnings = SchedulePlanner.Review(document, new([session.Id], [], proposal.Availability)
                { GapAnchorIds = proposal.GapAnchorIds }, timeZone, saved).Errors;
            var warning = saved.Contains(session.Id) ? "Already saved in My Schedule."
                : string.Join(" ", warnings);
            Sessions.Add(new(session, string.Join(", ", document.Speakers
                .Where(s => session.SpeakerIds.Contains(s.Id)).Select(s => s.Name)), timeZone,
                proposal.SessionIds.Contains(session.Id), warning, ReviewSelection));
        }
        HasProposal = Sessions.Count > 0;
        OnPropertyChanged(nameof(CanRelaxConstraints));
        ReviewSelection();
    }

    [RelayCommand]
    private void ChooseSubset()
    {
        if (proposal is null || IsBusy)
            return;
        proposal = proposal with { RequiredSpeakers = [], AllSessionsByRequiredSpeakers = false, TopicFilter = null };
        Constraints = "You chose to review a subset. The original speaker and 'all sessions' requirements are no longer enforced. Availability and saved-session conflicts still apply.";
        OnPropertyChanged(nameof(CanRelaxConstraints));
        ReviewSelection();
    }

    private void NotifySelection()
    {
        foreach (var property in new[] { nameof(SelectedCount), nameof(SelectionSummary), nameof(ApplyLabel) })
            OnPropertyChanged(property);
    }

    private ScheduleProposal SelectedProposal() => (proposal ?? throw new InvalidOperationException("No proposal.")) with
    {
        SessionIds = Sessions.Where(s => s.IsSelected && s.CanSelect).Select(s => s.Id).ToArray()
    };

    private void ReviewSelection()
    {
        ConstraintsConfirmed = false;
        NotifySelection();
        if (document is null || proposal is null)
            return;
        foreach (var session in Sessions)
            session.Conflict = string.Join(" ", Sessions.Where(other => other.Id != session.Id
                    && other.IsSelected && other.CanSelect && session.StartsAt < other.EndsAt
                    && other.StartsAt < session.EndsAt)
                .Select(other => $"Overlaps selected talk: {other.Title}."));
        // Keep original ID errors visible; filtering a preview must not hide hallucinated IDs.
        var originalErrors = SchedulePlanner.Review(document, proposal, timeZone, SavedIds()).Issues
            .Where(issue => issue.Code is ProposalIssueCode.UnknownSession or ProposalIssueCode.DuplicateSession)
            .Select(issue => issue.Message).ToArray();
        var review = SchedulePlanner.Review(document, SelectedProposal(), timeZone, SavedIds());
        HasValidProposal = review.CanApply && originalErrors.Length == 0;
        Validation = HasValidProposal
            ? "Selected sessions satisfy the interpreted constraints. Existing likes will be kept."
            : string.Join("\n", originalErrors.Concat(review.Errors).Distinct());
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (!CanApply || document is null || proposal is null)
            return;
        if (TimeZoneInfo.Local.Id != timeZone.Id)
        {
            ClearProposal();
            Status = "Your device time zone changed. Generate a new proposal before saving.";
            AnnounceOutcome();
            return;
        }
        var selected = SelectedProposal();
        ReviewSelection();
        if (!HasValidProposal)
        {
            AnnounceOutcome();
            return;
        }
        IsSaving = true;
        IsBusy = true;
        Status = "Saving selected sessions...";
        try
        {
            var latest = await dataSource.LoadAsync();
            if (!SchedulePlanner.HasSameAgenda(document, latest))
            {
                ClearProposal();
                Status = "The agenda was refreshed. Generate a new proposal to review the latest session times.";
                IsBusy = false;
                AnnounceOutcome();
                return;
            }
            var reviewed = await schedule.AddProposalAsync(latest, selected, timeZone);
            if (reviewed is { CanApply: false })
            {
                HasValidProposal = false;
                Validation = string.Join("\n", reviewed.Errors);
                Status = "Your schedule changed. Nothing was added; generate a new proposal.";
                IsBusy = false;
                AnnounceOutcome();
                return;
            }
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            logger.LogError(exception, "Could not add planner proposals to the schedule.");
            Status = "Couldn't save the proposal. Your liked sessions have not changed. Check storage and retry.";
            IsBusy = false;
            AnnounceOutcome();
            return;
        }
        try
        {
            scheduleUpdates.RefreshSavedState();
            ClearProposal();
            HasAddedSessions = true;
            Status = $"{selected.SessionIds.Length} selected sessions added to My Schedule. Your previous likes were kept.";
            if (!await scheduleUpdates.RefreshRemindersAsync())
                Status += " Reminders couldn't be updated; check Settings.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            logger.LogError(exception, "Schedule saved, but the presentation or reminders could not be refreshed.");
            ClearProposal();
            HasAddedSessions = true;
            Status = "Your selected sessions were saved, but the display or reminders couldn't be refreshed. Reopen My Schedule and check Settings.";
        }
        finally
        {
            IsBusy = false;
            AnnounceOutcome();
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        stopped = true;
        operation?.Cancel();
        startingPointsOperation?.Cancel();
        modelOperation?.Cancel();
    }

    partial void OnRequestChanged(string value)
    {
        operation?.Cancel();
        ClearProposal();
    }

    partial void OnIsBusyChanged(bool value)
    {
        if (!value)
            IsSaving = false;
        foreach (var session in Sessions)
            session.IsBusy = value;
        NotifySelection();
    }

    partial void OnHasValidProposalChanged(bool value) => OnPropertyChanged(nameof(CanConfirm));

    private void AnnounceOutcome() => OutcomeAvailable?.Invoke(this,
        HasValidationErrors ? $"The shortlist cannot be saved. {Validation}"
        : HasStatus ? Status
        : $"{Sessions.Count} suggestions, {SelectedCount} selected. Nothing is saved until you choose Add.");

    private void ClearProposal()
    {
        proposal = null;
        Sessions.Clear();
        HasProposal = false;
        ShowMatchingDetails = false;
        HasValidProposal = false;
        ConstraintsConfirmed = false;
        Constraints = "";
        Validation = "";
        NotifySelection();
        OnPropertyChanged(nameof(CanRelaxConstraints));
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or NotSupportedException or ArgumentException
#if IOS
            or Foundation.NSErrorException
#endif
        ;
}
