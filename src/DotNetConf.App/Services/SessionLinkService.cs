using DotNetConf.App.ViewModels;
using DotNetConf.Core;
using Microsoft.Extensions.Logging;

namespace DotNetConf.App.Services;

public sealed class SessionLinkService(
    ConferenceViewModel conference, DetailNavigationService navigation, ILogger<SessionLinkService> logger)
{
    private Shell? shell;
    private readonly Queue<(string? Id, string? Error)> pending = new();
    private (string? Id, string? Error)? active;
    private bool opening;

    public void Attach(Shell owner)
    {
        shell = owner;
        owner.Loaded += (_, _) => Resume();
        owner.Navigated += (_, _) => Resume();
    }

    public void Receive(string link)
    {
        try { Enqueue((SessionAppLinks.Parse(link), null)); }
        catch (UriFormatException exception)
        {
            logger.LogWarning(exception, "Ignoring an invalid companion session link.");
            Enqueue((null, "This isn't a valid companion session link."));
        }
    }

    public void ReceiveReminder(string? data)
    {
        if (data?.StartsWith(SessionAppLinks.ReminderPrefix, StringComparison.Ordinal) == true)
            Receive($"{SessionAppLinks.Scheme}://session/{Uri.EscapeDataString(data[SessionAppLinks.ReminderPrefix.Length..])}");
    }

    private void Enqueue((string? Id, string? Error) request)
    {
        lock (pending)
        {
            // A launch can arrive through both the application and scene callbacks.
            if (active == request || pending.Contains(request))
                return;
            if (pending.Count >= 8)
            {
                pending.Dequeue();
                logger.LogWarning("Too many pending session links; retaining the most recent requests.");
            }
            pending.Enqueue(request);
        }
        Resume();
    }

    public void Resume()
    {
        shell?.Dispatcher.Dispatch(DrainAsync);
    }

    private async void DrainAsync()
    {
        var owner = shell;
        if (opening || owner is null || !owner.IsLoaded || owner.Navigation.ModalStack.Count > 0)
            return;
        opening = true;
        try
        {
            while (owner.Navigation.ModalStack.Count == 0)
            {
                (string? Id, string? Error) request;
                lock (pending)
                {
                    if (!pending.TryDequeue(out request))
                        break;
                    active = request;
                }
                if (request.Error is not null)
                {
                    await owner.DisplayAlertAsync("Couldn't open session", request.Error, "OK");
                    continue;
                }
                var page = owner.CurrentPage;
                var previousTitleView = Shell.GetTitleView(page);
                var resolution = conference.ResolveLinkedSessionAsync(request.Id!);
                HorizontalStackLayout? loading = null;
                if (!resolution.IsCompleted)
                {
                    loading = new HorizontalStackLayout { Spacing = 8, AutomationId = "session-link-loading" };
                    loading.Add(new ActivityIndicator { IsRunning = true });
                    loading.Add(new Label { Text = "Opening session...", VerticalOptions = LayoutOptions.Center });
                    Shell.SetTitleView(page, loading);
                }
                try
                {
                    try
                    {
                        var session = await resolution;
                        if (owner.Navigation.ModalStack.Count > 0)
                        {
                            lock (pending)
                            {
                                active = null;
                                if (pending.Count < 8)
                                    pending.Enqueue(request);
                                else
                                    logger.LogWarning("The deferred session link was superseded by newer pending requests.");
                            }
                            break;
                        }
                        if (session is not { IsWithdrawn: false })
                        {
                            logger.LogWarning("A session link did not match the downloaded public agenda.");
                            await owner.DisplayAlertAsync("Session unavailable",
                                "This session isn't in the downloaded published agenda. Refresh the agenda and try again.", "OK");
                            continue;
                        }
                    }
                    finally
                    {
                        if (loading is not null && ReferenceEquals(Shell.GetTitleView(page), loading))
                            Shell.SetTitleView(page, previousTitleView);
                    }
                    await navigation.OpenLinkedSessionAsync(request.Id!);
                    if (OperatingSystem.IsIOS() || OperatingSystem.IsAndroid())
                        SemanticScreenReader.Default.Announce("Session opened");
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
                {
                    logger.LogError(exception, "Could not open a linked session.");
                    await owner.DisplayAlertAsync("Couldn't open session",
                        "The session could not be opened. Browse Sessions or try the link again.", "OK");
                }
            }
        }
        finally
        {
            lock (pending) { active = null; }
            opening = false;
            BackNavigation.Update(owner);
        }
    }
}
