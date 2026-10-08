using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace DotNetConf.App;

// Avalonia.Controls.Maui.Essentials does not implement IBrowser yet; Avalonia's launcher covers desktop and the browser.
public sealed class AvaloniaBrowser : IBrowser
{
    public async Task<bool> OpenAsync(Uri uri, BrowserLaunchOptions options)
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime switch
        {
            IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
            ISingleViewApplicationLifetime singleView => TopLevel.GetTopLevel(singleView.MainView),
            _ => null,
        };
        return topLevel is not null && await topLevel.Launcher.LaunchUriAsync(uri);
    }
}
