using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetConf.App.Services;
using DotNetConf.Core;
using Microsoft.Extensions.Logging;

namespace DotNetConf.App.ViewModels;

public partial class ProposedSessionViewModel(Session session, string speakerNames, TimeZoneInfo zone, Action changed) : ObservableObject
{
    public string Id => session.Id;
    public string AutomationId => $"planner-session-{Id}";
    public string Title => session.Title;
    public string SpeakerNames => speakerNames;
    public string Time => $"{TimeZoneInfo.ConvertTime(session.StartsAt, zone):ddd, MMM d HH:mm} - " +
        $"{TimeZoneInfo.ConvertTime(session.EndsAt, zone):HH:mm}";
    [ObservableProperty] public partial bool IsSelected { get; set; } = true;
    partial void OnIsSelectedChanged(bool value) => changed();
}

public partial class PlannerViewModel(
    IConferenceDataSource dataSource, ScheduleService schedule, OnDevicePlanner planner,
    ConferenceViewModel conference, ILogger<PlannerViewModel> logger) : ObservableObject
{
    private CancellationTokenSource? operation;
    private ConferenceDocument? document;
    private ScheduleProposal? proposal;
    private TimeZoneInfo timeZone = TimeZoneInfo.Local;
    private bool initialized;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    public partial string Request { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsBusy { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsReady { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "Checking the on-device model...";
    [ObservableProperty] public partial string Constraints { get; set; } = "";
    [ObservableProperty] public partial string Validation { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    public partial bool HasValidProposal { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    public partial bool ConstraintsConfirmed { get; set; }
    [ObservableProperty] public partial bool HasProposal { get; set; }
    public ObservableCollection<ProposedSessionViewModel> Sessions { get; } = [];
    public bool CanGenerate => IsReady && !IsBusy && !string.IsNullOrWhiteSpace(Request);
    public bool CanEdit => IsReady && !IsBusy;
    public bool CanApply => HasValidProposal && ConstraintsConfirmed && !IsBusy;
    public string TimeZoneLabel => $"Use dates and times in {TimeZoneInfo.Local.Id}, or explicitly name another time zone.";

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
        if (OnDevicePlanner.UnsupportedReason is { } unsupported)
        {
            Status = unsupported;
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
            Status = "Apple Intelligence is ready. Your request stays on this device.";
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
        }
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (!CanGenerate)
            return;
        ClearProposal();
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
                (proposal.AllSessionsMatchingQuery is { } query ? $"Include all sessions matching: {query}.\n" : "") +
                $"Availability: {(proposal.Availability.Length == 0 ? "Unrestricted" : string.Join("; ", proposal.Availability.Select(w => $"{w.Start} to {w.End} ({(w.Zone == "local" ? timeZone.Id : w.Zone)})")))}";
            foreach (var session in SchedulePlanner.Review(document, proposal, timeZone, SavedIds()).Sessions)
                Sessions.Add(new(session, string.Join(", ", document.Speakers
                    .Where(s => session.SpeakerIds.Contains(s.Id)).Select(s => s.Name)), timeZone, ReviewSelection));
            HasProposal = true;
            ReviewSelection();
            Status = Sessions.Count == 0
                ? "No schedule was proposed. Check the explanation below; your likes have not changed."
                : "Review the interpreted constraints and sessions below. AI can misunderstand your request. Nothing has been saved.";
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
        SessionIds = Sessions.Where(s => s.IsSelected).Select(s => s.Id).ToArray()
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
            Status = "Selected proposals added to My Schedule. Your previous likes were kept.";
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
    public void Cancel() => operation?.Cancel();

    [RelayCommand]
    private async Task SkipAsync()
    {
        Cancel();
        await Shell.Current.GoToAsync("..");
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
