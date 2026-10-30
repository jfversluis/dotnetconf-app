namespace DotNetConf.App.Pages;

public sealed record DependencyNotice(string Name, string Details, string File)
{
    public string AccessibilityLabel => $"{Name}. {Details}.";
}

public partial class ThirdPartyPage : ContentPage
{
    public IReadOnlyList<DependencyNotice> Dependencies { get; } =
    [
        new(".NET MAUI", "Controls, Maps, Core, Essentials & Graphics · 11.0.0-rc.2.26505.5 · MIT", "LICENSE-DotNetFoundation.txt"),
        new("CommunityToolkit.Maui", "15.0.1 · MIT", "LICENSE-MauiToolkit.txt"),
        new("CommunityToolkit.Mvvm", "8.4.2 · MIT", "LICENSE-MvvmToolkit.txt"),
        new("Syncfusion.Maui.Toolkit", "1.0.11 · MIT", "LICENSE-SyncfusionToolkit.txt"),
        new("IconFont.Maui.FluentIcons", "1.1.1 · MIT", "LICENSE-FluentIcons.txt"),
        new("Fluent UI System Icons", "Regular and Filled icon fonts · MIT", "LICENSE-FluentSystemIcons.txt"),
        new("Open Sans", "Regular and Semibold · Apache 2.0", "LICENSE-OpenSans.txt"),
        new("Space Grotesk", "Bold · SIL Open Font License 1.1", "LICENSE-SpaceGrotesk.txt"),
#if ANDROID
        new("Google Maps SDK & Google Play services native components", "Maps 20.0.0 · Base/Basement 18.10.0 · Tasks 18.4.1 · upstream third-party licenses", "NOTICE-GooglePlayServices.txt"),
#endif
#if AVALONIA
        new("Avalonia", "12.1.2 · MIT", "LICENSE-Avalonia.txt"),
        new("Avalonia third-party components", "Upstream copyright and license notices", "NOTICE-Avalonia.txt"),
        new("Avalonia.Controls.Maui", "Controls, Essentials & Desktop packages · 11.0.0-rc.1.26252.343 · MIT", "LICENSE-AvaloniaMaui.txt"),
        new("Avalonia.Controls.Maui third-party components", "ASP.NET Core MIME mappings · Apache 2.0", "NOTICE-AvaloniaMaui.txt"),
        new("Avalonia.Controls.WebView", "12.0.0 · MIT", "LICENSE-Avalonia.txt"),
        new("Microsoft.Maui.Graphics.Skia", "11.0.0-rc.1.26451.6 · MIT", "LICENSE-DotNetFoundation.txt"),
        new("Avalonia ANGLE", "Windows native dependency of the desktop graph · 2.1.27548.20260419 · BSD 3-Clause", "LICENSE-AvaloniaAngle.txt"),
        new("Inter", "Avalonia fallback font · SIL Open Font License 1.1", "LICENSE-Inter.txt"),
        new("SkiaSharp & HarfBuzzSharp", "4.151.0 & 14.2.1.102 · MIT", "LICENSE-SkiaSharp.txt"),
        new("SkiaSharp & HarfBuzzSharp native components", "Upstream copyright and license notices", "NOTICE-SkiaSharp.txt"),
        new("MicroCom.Runtime", "0.11.6 · MIT", "LICENSE-MicroCom.txt"),
        new("Tmds.DBus.Protocol", "Linux desktop dependency · 0.94.1 · MIT", "LICENSE-TmdsDBus.txt"),
#endif
#if DEBUG
        new("MAUI DevFlow", "Development builds only · 0.1.0-preview.13.26512.5 · MIT", "LICENSE-DotNetFoundation.txt"),
        new("Microsoft.Extensions.Logging.Debug", "Development builds only · 11.0.0-rc.2.26504.105 · MIT", "LICENSE-DotNetFoundation.txt"),
#endif
    ];

    public Command<DependencyNotice> ReadLicenseCommand { get; }
    public Command ReadNoticesCommand { get; }

    public ThirdPartyPage()
    {
        ReadLicenseCommand = new Command<DependencyNotice>(async notice => await OpenAsync(notice));
        ReadNoticesCommand = new Command(async () => await OpenAsync(
            new DependencyNotice("Third-party notices", "", "THIRD-PARTY-NOTICES.txt")));
        InitializeComponent();
        BindingContext = this;
    }

    private static Task OpenAsync(DependencyNotice notice) =>
        Shell.Current.GoToAsync("license", new ShellNavigationQueryParameters { ["notice"] = notice });
}
