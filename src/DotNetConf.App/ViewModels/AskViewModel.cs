using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetConf.App.Services;
using DotNetConf.Core;
using Microsoft.Extensions.Logging;

namespace DotNetConf.App.ViewModels;

public partial class AskViewModel(IOnDeviceAsk ask, IAskApplication application, ScheduleService schedule,
    ILogger<AskViewModel> logger) : ObservableObject
{
    private CancellationTokenSource? active;
    private AskResolution? pending;
    private ConferenceDocument? reviewedDocument;
    private string[] reviewedSavedIds = [];
    private string reviewedZone = "";
    private DateTimeOffset expires;
    private int generation;
    private bool opened;
    public event Func<Task>? BeforeNavigation;
    public event Action<string>? Announce;

    [ObservableProperty] public partial bool IsAvailable { get; set; }
    [ObservableProperty] public partial string Request { get; set; } = "";
    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    public partial bool IsBusy { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    public partial bool HasAgenda { get; set; }
    [ObservableProperty] public partial AskChoice[] Choices { get; set; } = [];
    [ObservableProperty] public partial bool NeedsChoice { get; set; }
    [ObservableProperty] public partial bool NeedsApproval { get; set; }
    [ObservableProperty] public partial bool CanViewSchedule { get; set; }
    public bool CanSubmit => !IsBusy && HasAgenda;
#if DEBUG
    [ObservableProperty] public partial bool ShowTrace { get; set; }
    [ObservableProperty] public partial string TraceText { get; set; } = "";
#endif

    public async Task CheckAvailabilityAsync(CancellationToken token)
    {
        HasAgenda = application.AskDocument?.Sessions.Count > 0;
        try { IsAvailable = await ask.IsAvailableAsync(token); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not check on-device Ask readiness.");
            IsAvailable = false;
            if (opened)
                Message = "Couldn't check the local model. Close Ask and try again. Normal search is still available.";
        }
    }

    public void Open()
    {
        opened = true;
        ClearPending();
        HasAgenda = application.AskDocument?.Sessions.Count > 0;
        Message = HasAgenda ? "Find a talk, open a speaker, or update your saved schedule. One request at a time."
            : "Download the agenda first. Close Ask and pull down on Home to retry.";
    }

    public void Close()
    {
        opened = false;
        Stop();
        Request = "";
        Message = "";
#if DEBUG
        TraceText = "";
        ShowTrace = false;
#endif
    }

    [RelayCommand]
    private void Stop()
    {
        generation++;
        active?.Cancel();
        ClearPending();
        if (opened)
            Message = "Stopped. Any completed save is kept; no pending action will run.";
    }

    partial void OnRequestChanged(string value) => ClearPending();

    private void ClearPending()
    {
        pending = null;
        reviewedDocument = null;
        Choices = [];
        NeedsApproval = false;
        NeedsChoice = false;
        CanViewSchedule = false;
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync()
    {
        if (IsBusy || !opened)
            return;
        ClearPending();
        var document = application.AskDocument;
        if (document is null || document.Sessions.Count == 0)
        {
            Message = "Download the agenda first. Nothing changed.";
            return;
        }
        active = new CancellationTokenSource();
        var token = active.Token;
        var visit = ++generation;
        IsBusy = true;
        Message = "Finding the right app action on your device...";
        try
        {
            await schedule.ReloadAsync(token);
            var zone = application.AskTimeZone;
            var savedSnapshot = schedule.SavedIds;
            var result = await ask.InterpretAsync(document, zone, Request.Trim(), savedSnapshot, token);
            token.ThrowIfCancellationRequested();
            if (visit != generation || !opened)
                return;
            if (!ReferenceEquals(document, application.AskDocument) || zone.Id != application.AskTimeZone.Id)
                throw new InvalidDataException("The agenda or timezone changed. Try your request again.");
            reviewedDocument = document;
            reviewedSavedIds = savedSnapshot;
            reviewedZone = zone.Id;
            expires = DateTimeOffset.UtcNow.AddMinutes(5);
            pending = result;
            if (result is null)
                Message = "I can find or open conference sessions and speakers, favorite matching talks, or remove saved sessions. Try one of those actions.";
            else
                await PresentAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (opened)
                Message = "Stopped. No pending action will run.";
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Ask request did not complete.");
            ClearPending();
            Message = exception is InvalidDataException or NotSupportedException or SchedulePresentationException ? exception.Message
                : "Couldn't complete this request. Check the local model and try again. No pending action will run.";
        }
        finally
        {
#if DEBUG
            TraceText = opened ? string.Join("\n", ask.Trace) : "";
            if (opened && CanViewSchedule)
                TraceText += "\nApplication | saved state confirmed; shared UI refreshed";
#endif
            active.Dispose();
            active = null;
            IsBusy = false;
            if (opened)
                Announce?.Invoke(Message);
        }
    }

    private async Task PresentAsync(CancellationToken token)
    {
        var result = pending ?? throw new InvalidOperationException("No pending action.");
        Message = result.Message;
        if (result.Choices.Length == 0)
            return;
        Choices = result.Choices;
        NeedsChoice = result.ChooseDay || (Choices.Length > 1 &&
            (result.Intent.Action is AskAction.OpenSession or AskAction.OpenSpeaker ||
             result.Intent.Action == AskAction.SaveSession && !result.Intent.AllMatches));
        NeedsApproval = !result.ChooseDay && result.Intent.Action == AskAction.RemoveSessions;
        if (NeedsApproval)
            Message = $"Remove {Choices.Length} saved {(Choices.Length == 1 ? "session" : "sessions")}? Times are device-local. The agenda itself will not be deleted.";
        if (!NeedsChoice && !NeedsApproval)
            await ExecuteAsync(result.Choices[0].Id, token);
    }

    [RelayCommand]
    private async Task ChooseAsync(AskChoice? choice)
    {
        if (choice is null || IsBusy || pending is null || !NeedsChoice ||
            !pending.Choices.Contains(choice))
            return;
        await RunPendingAsync(async token =>
        {
            RequireFresh();
            NeedsChoice = false;
            if (pending!.ChooseDay)
            {
                var intent = pending.Intent with { Day = choice.Id };
                pending = AskRequest.Resolve(reviewedDocument!, intent, application.AskTimeZone, reviewedSavedIds);
                await PresentAsync(token);
            }
            else
                await ExecuteAsync(choice.Id, token);
        });
    }

    [RelayCommand]
    private async Task ConfirmRemovalAsync()
    {
        if (IsBusy || !NeedsApproval || pending?.Intent.Action != AskAction.RemoveSessions)
            return;
        await RunPendingAsync(async token =>
        {
            RequireFresh();
            var result = pending!;
            var document = reviewedDocument!;
            var ids = result.Choices.Select(c => c.Id).ToArray();
            NeedsApproval = false;
            pending = null; // Consume approval before awaiting any persistence.
            var outcome = await application.RemoveAskSessionsAsync(ids, reviewedSavedIds, document, token);
            Choices = [];
            Message = outcome is null ? "Your agenda or saved schedule changed. Ask again to review a fresh removal preview."
                : $"Removed {ids.Length} saved {(ids.Length == 1 ? "session" : "sessions")}." + (outcome == false ? " Reminders need attention in Settings." : "");
            CanViewSchedule = outcome is not null;
#if DEBUG
            TraceText += "\nUser confirmation | " + (outcome is null ? "stale preview rejected" : $"removed {ids.Length} reviewed IDs");
#endif
        });
    }

    [RelayCommand]
    private void Decline()
    {
        ClearPending();
        Message = "Nothing removed. Your saved schedule is unchanged.";
#if DEBUG
        TraceText += "\nUser declined | no changes";
#endif
        Announce?.Invoke(Message);
    }

    private void RequireFresh()
    {
        if (!opened || !ReferenceEquals(reviewedDocument, application.AskDocument) ||
            reviewedZone != application.AskTimeZone.Id || DateTimeOffset.UtcNow > expires)
        {
            ClearPending();
            throw new InvalidDataException("This preview expired or the agenda changed. Ask again to review the current agenda.");
        }
    }

    private async Task RunPendingAsync(Func<CancellationToken, Task> action)
    {
        active = new CancellationTokenSource();
        IsBusy = true;
        try { await action(active.Token); }
        catch (OperationCanceledException) when (active.IsCancellationRequested)
        {
            ClearPending();
            if (opened) Message = "Stopped. No pending action will run.";
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not apply the reviewed Ask action.");
            ClearPending();
            Message = exception is InvalidDataException or SchedulePresentationException ? exception.Message
                : "Couldn't complete the action. Check your saved schedule before trying again.";
        }
        finally
        {
            active.Dispose();
            active = null;
            IsBusy = false;
            if (opened) Announce?.Invoke(Message);
        }
    }

    private async Task ExecuteAsync(string id, CancellationToken token)
    {
        RequireFresh();
        token.ThrowIfCancellationRequested();
        var result = pending!;
        var intent = result.Intent;
        pending = null;
        NeedsChoice = false;
        Choices = [];
        if (intent.Action == AskAction.SaveSession)
        {
            var ids = intent.AllMatches ? result.Choices.Select(choice => choice.Id).ToArray() : [id];
            var reminders = await application.SaveAskSessionsAsync(ids, token);
            token.ThrowIfCancellationRequested();
            Message = $"Saved {ids.Length} {(ids.Length == 1 ? "session" : "sessions")}."
                + (reminders ? "" : " Reminders need attention in Settings.");
            CanViewSchedule = true;
            if (BeforeNavigation is { } dismissSave)
                await dismissSave();
            token.ThrowIfCancellationRequested();
            await application.OpenAskScheduleAsync(reminders);
            Close();
            return;
        }
        if (BeforeNavigation is { } dismiss)
            await dismiss();
        token.ThrowIfCancellationRequested();
        switch (intent.Action)
        {
            case AskAction.FindSessions: await application.OpenAskSearchAsync(intent); break;
            case AskAction.OpenSession: await application.OpenSessionAsync(id); break;
            case AskAction.OpenSpeaker: await application.OpenSpeakerAsync(id); break;
            default: throw new InvalidOperationException("This action requires confirmation.");
        }
        Close();
    }
}
