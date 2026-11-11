using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using DotNetConf.Core;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace DotNetConf.App.ViewModels;

public partial class ConferenceViewModel
{
    public IReadOnlyList<ConferenceLinkViewModel> SocialChannels =>
        socialChannels ??= ConferenceLinks.SocialChannels.Select(link => new ConferenceLinkViewModel(link, this)).ToArray();
    private IReadOnlyList<ConferenceLinkViewModel>? socialChannels;
    public string ShareConferenceLabel => sharing.CanShare ? "Share .NET Conf" : "Copy conference invitation";
    public string ShareSessionLabel => sharing.CanShare ? "Share session" : "Copy session details";
    public string ShareConferenceHint => sharing.CanShare
        ? "Opens your device's share sheet" : "Copies the invitation to your clipboard";
    public string ShareSessionHint => sharing.CanShare
        ? "Opens your device's share sheet" : "Copies session details to your clipboard";
    public bool HasConferenceDates => document?.StartDate is not null;
    public string ConferenceDates
    {
        get
        {
            if (document?.StartDate is not { } start)
                return "";
            var first = start.ToString("MMMM d", CultureInfo.InvariantCulture);
            if (document.EndDate is not { } end || end == start)
                return first;
            if (start.Year != end.Year)
                return $"{first}, {start.Year}–{end.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)}";
            return start.Month == end.Month
                ? $"{first}–{end.Day}"
                : $"{first}–{end.ToString("MMMM d", CultureInfo.InvariantCulture)}";
        }
    }
    public string ConferenceDateContext => HasConferenceDates ? $"{ConferenceDates} · Online" : "";
    public string ConferenceShareText => $"Join me at {ConferenceName}" +
        (HasConferenceDates ? $", {ConferenceDates}." : ".") +
        $"\nA free, virtual celebration of .NET.\n\n{ConferenceLinks.Website}\n{ConferenceLinks.Hashtag}";

    [RelayCommand]
    private Task OpenRegistrationAsync() => OpenLinkAsync(new("Register for .NET Conf", ConferenceLinks.Registration));

    [RelayCommand]
    private Task ShareConferenceAsync() => ShareTextAsync(".NET Conf", ConferenceShareText);

    public async Task<SessionViewModel?> ResolveLinkedSessionAsync(string id)
    {
        await InitializeAsync();
        if (FindSession(id) is not { IsWithdrawn: false })
        {
            if (RefreshCommand.ExecutionTask is { IsCompleted: false } refresh)
                await refresh;
            else if (RefreshCommand.CanExecute(null))
                await RefreshCommand.ExecuteAsync(null);
        }
        return FindSession(id);
    }

    internal async Task ShowSessionActionsAsync(SessionViewModel session)
    {
        var actions = session.GetQuickActions();
        if (actions.Count == 0)
        {
            await ShowErrorAsync("Actions unavailable", "Try again after the agenda and local session choices finish loading.");
            return;
        }
        string? choice;
        try
        {
            var shell = Shell.Current ?? throw new InvalidOperationException("The session window is unavailable.");
            choice = await shell.DisplayActionSheetAsync(session.Title, "Cancel", null,
                actions.Select(action => action.Label).ToArray());
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(exception, "Could not display session actions.");
            await ShowErrorAsync("Actions unavailable", "Open the session details to save, change watched status, or share.");
            return;
        }
        if (choice is null or "Cancel")
            return;
        var selected = session.GetQuickActions().FirstOrDefault(action => action.Label == choice);
        if (selected.Command is not null && selected.Command.CanExecute(null))
            await selected.Command.ExecuteAsync(null);
        else
            await ShowErrorAsync("Session changed", "That action is no longer available. Open More actions to see the current choices.");
    }

    internal Task OpenLinkAsync(ConferenceLink link) => OpenLinkAsync(link, isSessionVideo: false);

    internal Task OpenSessionVideoAsync(string url) =>
        OpenLinkAsync(new("Watch session", url), isSessionVideo: true);

    private async Task OpenLinkAsync(ConferenceLink link, bool isSessionVideo)
    {
        try
        {
            if (!ConferenceLinks.IsSafeUrl(link.Url))
                throw new UriFormatException("The destination is not a valid HTTPS URL.");
            var uri = new Uri(link.Url);
            var opened = isSessionVideo
                ? await TryOpenSessionVideoAsync(uri)
                : await browser.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
            if (!opened)
            {
                logger.LogWarning("No app could open conference link {Url}.", link.Url);
                await ShowLinkErrorAsync(link.Url);
            }
        }
        catch (Exception exception) when (IsLinkOpenFailure(exception))
        {
            logger.LogError(exception, "Could not open conference link {Url}.", link.Url);
            await ShowLinkErrorAsync(link.Url);
        }
    }

    internal async Task<bool> TryOpenSessionVideoAsync(Uri uri)
    {
        if (!ConferenceLinks.IsYouTubeUrl(uri.AbsoluteUri))
            throw new UriFormatException("The session video is not a valid YouTube URL.");

        try
        {
            if (await videoLauncher.OpenAsync(uri))
                return true;
            logger.LogWarning("The device could not open session video {Url}; trying the browser.", uri);
        }
        catch (Exception exception) when (IsLinkOpenFailure(exception))
        {
            logger.LogWarning(exception, "The device could not open session video {Url}; trying the browser.", uri);
        }

        return await browser.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
    }

    private static bool IsLinkOpenFailure(Exception exception) =>
        exception is NotSupportedException or UriFormatException or InvalidOperationException or UnauthorizedAccessException
#if ANDROID
            or Android.Content.ActivityNotFoundException or Java.Lang.SecurityException
#endif
            ;

    private async Task ShowLinkErrorAsync(string url)
    {
        var copy = await Shell.Current.Dispatcher.DispatchAsync(() => Shell.Current.DisplayAlertAsync(
            "Couldn't open the link", $"Open this address in your browser:\n{url}", "Copy link", "Close"));
        if (copy)
            await CopyTextAsync(url, "Link copied");
    }

    internal async Task ShareTextAsync(string title, string text)
    {
        try
        {
            await sharing.ShareAsync(title, text);
            if (!sharing.CanShare)
                await ShowLinkFeedbackAsync("Details copied");
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Could not share conference content.");
            var copy = await Shell.Current.Dispatcher.DispatchAsync(() => Shell.Current.DisplayAlertAsync(
                "Couldn't share", "Copy the details instead. Your saved schedule hasn't changed.", "Copy details", "Close"));
            if (copy)
                await CopyTextAsync(text, "Details copied");
        }
    }

    internal async Task CopyTextAsync(string text, string confirmation)
    {
        try
        {
            await sharing.CopyAsync(text);
            await ShowLinkFeedbackAsync(confirmation);
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Could not copy conference content.");
            await ShowErrorAsync("Couldn't copy", $"Copy this text manually:\n{text}");
        }
    }

    private static Task ShowLinkFeedbackAsync(string message)
    {
#if AVALONIA
        return ShowErrorAsync(message, "Ready to paste into the app of your choice.");
#else
        return Toast.Make(message, ToastDuration.Short).Show();
#endif
    }
}
