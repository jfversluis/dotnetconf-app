using DotNetConf.App.Pages;
using Microsoft.Extensions.Logging;

namespace DotNetConf.App.Services;

public sealed class DetailNavigationService(
    ILogger<DetailNavigationService>? logger = null, IFoldableRegionSource? foldableRegionSource = null)
{
    private readonly SemaphoreSlim navigationGate = new(1, 1);
    private Shell? shell;
    private bool layoutQueued;
    private double textScale = 1;
    private readonly List<AdaptiveConferencePage> hosts = [];
    internal IFoldableRegionSource? FoldableRegionSource => foldableRegionSource;

    public void RegisterHost(AdaptiveConferencePage host) => hosts.Add(host);

    private AdaptiveConferencePage? CurrentHost =>
        hosts.FirstOrDefault(host => host.Parent == CurrentShell.CurrentItem?.CurrentItem?.CurrentItem);

    public Task OpenSessionAsync(string id) =>
        NavigateAsync(new(DetailKind.Session, id));

    public Task OpenSpeakerAsync(string id) =>
        NavigateAsync(new(DetailKind.Speaker, id));

    public void Attach(Shell owner, Window window)
    {
        shell = owner;
        window.SizeChanged += (_, _) => RequestLayoutSync();
        window.Activated += (_, _) => RequestLayoutSync();
        owner.Navigated += (_, _) => RequestLayoutSync();
        RequestLayoutSync();
    }

    public void RequestLayoutSync()
    {
        if (shell is null || layoutQueued)
            return;
        layoutQueued = true;
        shell.Dispatcher.Dispatch(async () =>
        {
            layoutQueued = false;
            try
            {
                await navigationGate.WaitAsync();
                try { await SynchronizeLayoutAsync(); }
                finally { navigationGate.Release(); }
            }
            catch (Exception exception)
            {
                logger?.LogError(exception, "Could not adapt detail navigation to the window size");
                await shell.DisplayAlertAsync("Unable to adjust details",
                    "The layout could not be updated. Return to the list and choose the item again.", "OK");
            }
        });
    }

    private Shell CurrentShell => shell ?? Shell.Current;

    private async Task NavigateAsync(DetailDestination destination)
    {
        await navigationGate.WaitAsync();
        try
        {
            await SynchronizeLayoutAsync();
            var owner = CurrentShell;
            var stack = owner.Navigation.NavigationStack;
            if (CurrentHost is { } host && stack.Skip(1).All(page => GetDestination(page) is not null))
            {
                host.Selection.Select(destination);
                host.RenderDetail();
                if (!host.IsInline && GetDestination(stack.LastOrDefault()) != destination)
                {
                    var prefix = stack.Count > 1
                        ? string.Join("/", Enumerable.Repeat("..", stack.Count - 1)) + "/"
                        : "";
                    await owner.GoToAsync($"{prefix}{destination.Route}?id={Uri.EscapeDataString(destination.Id)}");
                }
                return;
            }

            for (var index = stack.Count - 1; index >= 0; index--)
            {
                if (GetDestination(stack[index]) != destination)
                    continue;

                var stepsBack = stack.Count - 1 - index;
                if (stepsBack > 0)
                    await owner.GoToAsync(string.Join("/", Enumerable.Repeat("..", stepsBack)));
                return;
            }

            await owner.GoToAsync($"{destination.Route}?id={Uri.EscapeDataString(destination.Id)}");
        }
        finally
        {
            navigationGate.Release();
        }
    }

    private async Task SynchronizeLayoutAsync()
    {
        var owner = CurrentShell;
        var stack = owner.Navigation.NavigationStack;
        var measuredScale = stack.LastOrDefault() switch
        {
            SessionDetailPage page => page.TextScale,
            SpeakerDetailPage page => page.TextScale,
            _ => CurrentHost?.TextScale
        };
        if (measuredScale is { } scale)
            textScale = scale;
        var currentHost = CurrentHost;
        var foregroundLayout = stack.LastOrDefault() switch
        {
            SessionDetailPage page => page.CurrentLayout(owner.Window.Width, owner.Window.Height, textScale),
            SpeakerDetailPage page => page.CurrentLayout(owner.Window.Width, owner.Window.Height, textScale),
            _ => null
        };
        foreach (var registeredHost in hosts)
            registeredHost.UpdateLayoutPolicy(owner.Window.Width, owner.Window.Height, textScale,
                ReferenceEquals(registeredHost, currentHost) ? foregroundLayout : null);
        if (stack.Count == 0 || currentHost is not { } host)
            return;
        // Leave filters, settings, and other non-detail routes in place until the user returns.
        if (stack.Skip(1).Any(page => GetDestination(page) is null))
            return;

        if (GetDestination(stack.LastOrDefault()) is { } current)
            host.Selection.Select(current);
        if (host.CanShowInline)
        {
            host.IsInline = true;
            if (stack.Count > 1)
                await owner.GoToAsync(string.Join("/", Enumerable.Repeat("..", stack.Count - 1)), false);
            host.RenderDetail();
        }
        else if (host.IsInline)
        {
            host.IsInline = false;
            host.RenderDetail();
            if (host.Selection.Current is { } destination)
                await owner.GoToAsync($"{destination.Route}?id={Uri.EscapeDataString(destination.Id)}", false);
        }
    }

    private static DetailDestination? GetDestination(Page? page) => page switch
    {
        SessionDetailPage { SessionId: { } id } => new(DetailKind.Session, id),
        SpeakerDetailPage { SpeakerId: { } id } => new(DetailKind.Speaker, id),
        _ => null
    };
}
