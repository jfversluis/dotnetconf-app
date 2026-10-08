using System.Reflection;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Controls.Maui.Platform;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Microsoft.Maui.Hosting;

namespace DotNetConf.App.Browser;

internal static class Program
{
    private static Task Main()
    {
        // Fonts and bundled assets are resolved from the entry assembly, which is the MAUI app on the other targets.
        Assembly.SetEntryAssembly(typeof(MauiProgram).Assembly);
        return AppBuilder.Configure<BrowserApp>()
            .WithInterFont()
            // Browsers expose no system fonts, so text without a font family uses Inter.
            .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
            .StartBrowserAppAsync("out");
    }
}

internal sealed class BrowserApp : MauiAvaloniaApplication
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
