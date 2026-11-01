using Microsoft.Extensions.Logging;
#if ANDROID
using AndroidApplication = Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application;
using AndroidSoftInputMode = Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust;
#endif

namespace DotNetConf.App;

public partial class App : Application
{
    private readonly IServiceProvider services;

    public App(IServiceProvider services, IPreferences preferences, ILogger<App> logger)
    {
        InitializeComponent();
#if ANDROID
        AndroidApplication.SetWindowSoftInputModeAdjust(this, AndroidSoftInputMode.Resize);
#endif
        this.services = services;
        var savedTheme = preferences.Get("appearance", nameof(AppTheme.Unspecified));
        if (Enum.TryParse<AppTheme>(savedTheme, out var theme) && Enum.IsDefined(theme))
            UserAppTheme = theme;
        else
            logger.LogWarning("Ignoring invalid stored appearance preference {Appearance}.", savedTheme);
        RequestedThemeChanged += (_, _) => ApplyStatusBarTheme();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var shell = services.GetRequiredService<AppShell>();
        var window = new Window(shell);
        services.GetRequiredService<Services.DetailNavigationService>().Attach(shell, window);
        window.StatusBarTheme = RequestedTheme == AppTheme.Dark ? StatusBarTheme.Dark : StatusBarTheme.Light;
        window.Resumed += async (_, _) => await services.GetRequiredService<ViewModels.ConferenceViewModel>().ResumeAsync();
        window.Stopped += (_, _) => services.GetRequiredService<ViewModels.ConferenceViewModel>().PauseRefresh();
        return window;
    }

    private void ApplyStatusBarTheme()
    {
        foreach (var window in Windows)
            window.StatusBarTheme = RequestedTheme == AppTheme.Dark ? StatusBarTheme.Dark : StatusBarTheme.Light;
    }
}
