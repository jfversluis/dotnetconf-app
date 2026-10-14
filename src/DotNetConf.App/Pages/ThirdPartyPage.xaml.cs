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
#if AVALONIA
        new("Mapsui", "Community map on Linux and in the browser · 5.1.0 · MIT", "LICENSE-Mapsui.txt"),
        new("BruTile", "Map tiles for Mapsui · 6.0.0 · Apache 2.0", "LICENSE-BruTile.txt"),
        new("NetTopologySuite", "Map geometry for Mapsui · 2.6.0 · BSD 3-Clause", "LICENSE-NetTopologySuite.txt"),
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
