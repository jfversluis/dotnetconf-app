using DotNetConf.App.Pages;

namespace DotNetConf.App.Services;

public sealed class DetailNavigationService
{
    private readonly SemaphoreSlim navigationGate = new(1, 1);

    public Task OpenSessionAsync(string id) =>
        NavigateAsync("session-detail", id, page => page is SessionDetailPage detail && detail.SessionId == id);

    public Task OpenSpeakerAsync(string id) =>
        NavigateAsync("speaker-detail", id, page => page is SpeakerDetailPage detail && detail.SpeakerId == id);

    private async Task NavigateAsync(string route, string id, Func<Page, bool> matches)
    {
        await navigationGate.WaitAsync();
        try
        {
            var shell = Shell.Current;
            var stack = shell.Navigation.NavigationStack;
            for (var index = stack.Count - 1; index >= 0; index--)
            {
                if (!matches(stack[index]))
                    continue;

                var stepsBack = stack.Count - 1 - index;
                if (stepsBack > 0)
                    await shell.GoToAsync(string.Join("/", Enumerable.Repeat("..", stepsBack)));
                return;
            }

            await shell.GoToAsync($"{route}?id={Uri.EscapeDataString(id)}");
        }
        finally
        {
            navigationGate.Release();
        }
    }
}
