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
    public string Warning => warning;
    public bool HasWarning => warning.Length > 0;
    public bool CanSelect => !HasWarning && !IsBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelect))]
    public partial bool IsBusy { get; set; }
    public string SelectionDescription => $"{Title}. {SpeakerNames}. {Time}." +
        (HasWarning ? $" {Warning}" : "");
    [ObservableProperty] public partial bool IsSelected { get; set; } = selected && warning.Length == 0;
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
    public bool IsGapMode { get; private set; }
    public string Heading => IsGapMode ? "Make room\nfor discovery." : "Your conference,\nyour way.";
    public string Introduction => IsGapMode
        ? "Find talks in the free time on your saved conference days. Your existing schedule stays exactly as it is."
        : "Tell us what you want to learn. Get a personal shortlist, then choose what to save.";
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
    public partial bool IsBusy { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanRetryModel))]
    public partial bool IsReady { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; set; } = "Checking the on-device model...";
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
        new("AI + mobile", "I'm interested in AI and mobile development."),
        new("Web", "I'd like to learn about web development and Blazor."),
        new("Explore .NET", "I'm new to .NET. Suggest a varied introduction to the conference.")
    ];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStartingPoints))]
    public partial bool IsLoadingStartingPoints { get; set; } = true;
    public bool ShowStartingPoints => !IsLoadingStartingPoints;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStartingPointsStatus))]
    public partial string StartingPointsStatus { get; set; } = "";
    public bool HasStartingPointsStatus => !string.IsNullOrWhiteSpace(StartingPointsStatus);
    public bool CanGenerate => IsReady && !IsBusy && !string.IsNullOrWhiteSpace(Request);
    public bool CanEdit => !IsBusy;
    public bool CanRetryModel => !IsReady && !IsBusy && planner.UnsupportedReason is null;
    public bool CanApply => HasValidProposal && ConstraintsConfirmed && !IsBusy;
    public bool CanConfirm => HasValidProposal && !IsBusy;
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
    private async Task CheckModelAsync()
    {
        if (IsBusy)
            return;
        ClearProposal();
        IsReady = false;
        IsLoadingStartingPoints = true;
        if (planner.UnsupportedReason is { } unsupported)
        {
            Status = unsupported;
            IsLoadingStartingPoints = false;
            return;
        }
        IsBusy = true;
        Status = "Checking the on-device model...";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        operation = cancellation;
        try
        {
            await planner.CheckReadinessAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            IsReady = true;
            Status = "";
            _ = RefreshStartingPointsAsync();
        }
        catch (OperationCanceledException)
        {
            Status = "Model check cancelled or timed out. Retry, or skip and browse normally.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            if (cancellation.IsCancellationRequested)
                Status = "Model check cancelled or timed out. Retry, or skip and browse normally.";
            else
            {
                logger.LogWarning(exception, "On-device planner readiness check failed.");
                Status = "On-device planning isn't ready on this device. Check that a supported local model is enabled and downloaded, then retry. " +
                    "You can still use hearts to build My Schedule. No model download starts automatically.";
            }
        }
        finally
        {
            operation = null;
            IsBusy = false;
            if (!IsReady)
                IsLoadingStartingPoints = false;
        }
    }

    private async Task RefreshStartingPointsAsync()
    {
        startingPointsOperation?.Cancel();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        startingPointsOperation = cancellation;
        IsLoadingStartingPoints = true;
        StartingPointsStatus = "";
        try
        {
            var agenda = await dataSource.LoadAsync(cancellation.Token);
            document = agenda;
            OnPropertyChanged(nameof(AgendaDisclosure));
            if (agenda.Sessions.Count == 0)
                throw new InvalidDataException("Download the agenda before requesting suggestions.");
            var suggestions = await planner.SuggestStartingPointsAsync(agenda, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            StartingPoints = suggestions;
            StartingPointsStatus = "";
        }
        catch (OperationCanceledException)
        {
            StartingPointsStatus = "Showing curated starting points.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            logger.LogWarning(exception, "Could not refresh local planner starting points.");
            StartingPointsStatus = "Couldn't refresh suggestions. Curated starting points are available.";
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
        ClearProposal();
        startingPointsOperation?.Cancel();
        IsBusy = true;
        Status = "Searching the real agenda with the on-device model...";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        operation = cancellation;
        try
        {
            document = await dataSource.LoadAsync(cancellation.Token);
            await schedule.ReloadAsync(cancellation.Token);
            if (document.Sessions.Count == 0)
                throw new InvalidDataException("The agenda has no sessions.");
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
            var matchedIds = proposal.MatchedSessionIds.Concat(proposal.SessionIds).ToHashSet(StringComparer.Ordinal);
            foreach (var session in document.Sessions.Where(s => matchedIds.Contains(s.Id)).OrderBy(s => s.StartsAt))
            {
                var warnings = SchedulePlanner.Review(document, new([session.Id], [], proposal.Availability)
                    { GapAnchorIds = proposal.GapAnchorIds },
                    timeZone, SavedIds()).Errors;
                Sessions.Add(new(session, string.Join(", ", document.Speakers
                    .Where(s => session.SpeakerIds.Contains(s.Id)).Select(s => s.Name)), timeZone,
                    proposal.SessionIds.Contains(session.Id),
                    warnings.Count > 0 ? "Not selected. " + string.Join(" ", warnings) : "", ReviewSelection));
            }
            HasProposal = true;
            ReviewSelection();
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

    private ScheduleProposal SelectedProposal() => (proposal ?? throw new InvalidOperationException("No proposal.")) with
    {
        SessionIds = Sessions.Where(s => s.IsSelected && s.CanSelect).Select(s => s.Id).ToArray()
    };

    private void ReviewSelection()
    {
        ConstraintsConfirmed = false;
        if (document is null || proposal is null)
            return;
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
        IsBusy = true;
        try
        {
            var latest = await dataSource.LoadAsync();
            if (!ReferenceEquals(latest, document))
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
            Status = "Selected proposals added to My Schedule. Your previous likes were kept.";
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
        operation?.Cancel();
        startingPointsOperation?.Cancel();
    }

    partial void OnRequestChanged(string value)
    {
        Cancel();
        ClearProposal();
    }

    partial void OnIsBusyChanged(bool value)
    {
        foreach (var session in Sessions)
            session.IsBusy = value;
    }

    partial void OnHasValidProposalChanged(bool value) => OnPropertyChanged(nameof(CanConfirm));

    private void AnnounceOutcome() => OutcomeAvailable?.Invoke(this,
        HasValidationErrors ? $"The shortlist cannot be saved. {Validation}"
        : HasStatus ? Status
        : $"{Sessions.Count} sessions in your shortlist. Review their times and confirm before saving.");

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
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or NotSupportedException or ArgumentException
#if IOS
            or Foundation.NSErrorException
#endif
        ;
}
