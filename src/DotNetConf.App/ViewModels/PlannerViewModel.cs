using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetConf.App.Services;
using DotNetConf.Core;
using Microsoft.Extensions.Logging;

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
    public bool CanSelect => !HasWarning;
    [ObservableProperty] public partial bool IsSelected { get; set; } = selected && warning.Length == 0;
    partial void OnIsSelectedChanged(bool value) => changed();
}

public partial class PlannerViewModel(
    IConferenceDataSource dataSource, ScheduleService schedule, OnDevicePlanner planner,
    ConferenceViewModel conference, ILogger<PlannerViewModel> logger, SessionReminderService reminders) : ObservableObject
{
    private CancellationTokenSource? operation;
    private ConferenceDocument? document;
    private ScheduleProposal? proposal;
    private TimeZoneInfo timeZone = TimeZoneInfo.Local;
    private bool initialized;
    private CancellationTokenSource? startingPointsOperation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    public partial string Request { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanRetryModel))]
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
        Status = "Checking Apple Intelligence with a short local inference...";
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
                Status = "The local model isn't available. Use a supported iPhone with Apple Intelligence enabled, a supported language, and the model downloaded. Retry or skip.";
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
            await schedule.InitializeAsync(cancellation.Token);
            if (document.Sessions.Count == 0)
                throw new InvalidDataException("The agenda has no sessions.");
            timeZone = TimeZoneInfo.Local;
            var result = await planner.ProposeAsync(document, Request, timeZone, SavedIds(), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            proposal = result.Proposal;
            if (proposal is null)
            {
                Status = string.IsNullOrWhiteSpace(result.Message)
                    ? "The model did not submit a proposal. Try a shorter, more specific request."
                    : result.Message[..Math.Min(result.Message.Length, 1600)];
                return;
            }
            Constraints = $"Required speakers: {(proposal.RequiredSpeakers.Length == 0 ? "None" : string.Join(", ", proposal.RequiredSpeakers))}\n" +
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
                var warnings = SchedulePlanner.Review(document, new([session.Id], [], proposal.Availability),
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
        }
    }

    private string[] SavedIds() => document?.Sessions.Where(s => schedule.IsSaved(s.Id)).Select(s => s.Id).ToArray() ?? [];

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
        var originalErrors = SchedulePlanner.Review(document, proposal, timeZone, SavedIds()).Errors
            .Where(e => e.StartsWith("Unknown session ID:", StringComparison.Ordinal)
                || e.StartsWith("Duplicate session:", StringComparison.Ordinal)).ToArray();
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
            return;
        }
        ReviewSelection();
        if (!HasValidProposal)
            return;
        IsBusy = true;
        try
        {
            await schedule.AddAsync(SelectedProposal().SessionIds);
            conference.RefreshSavedState();
            ClearProposal();
            HasAddedSessions = true;
            Status = "Selected proposals added to My Schedule. Your previous likes were kept.";
            if (!await reminders.RefreshAsync())
                Status += " Reminders couldn't be updated; check Settings.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            logger.LogError(exception, "Could not add planner proposals to the schedule.");
            Status = "Couldn't save the proposal. Your liked sessions have not changed. Check storage and retry.";
        }
        finally
        {
            IsBusy = false;
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
