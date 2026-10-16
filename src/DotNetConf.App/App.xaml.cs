using Microsoft.Extensions.Logging;
#if ANDROID
using AndroidApplication = Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application;
using AndroidSoftInputMode = Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust;
#endif

namespace DotNetConf.App;

public partial class App : Application
{
    private readonly IServiceProvider services;
    private readonly FirstRunPlannerState onboarding;
    private readonly ILogger<App> logger;

    public App(IServiceProvider services, IPreferences preferences, ILogger<App> logger)
    {
        InitializeComponent();
#if ANDROID
        AndroidApplication.SetWindowSoftInputModeAdjust(this, AndroidSoftInputMode.Resize);
#endif
        this.services = services;
        this.logger = logger;
        onboarding = new(preferences.Get(FirstRunPlannerState.PreferenceKey, false),
            () => preferences.Set(FirstRunPlannerState.PreferenceKey, true));
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
        shell.Loaded += (_, _) => shell.Dispatcher.Dispatch(() => ShowOnboardingAsync(shell));
        window.Activated += (_, _) =>
        {
            if (shell.IsLoaded)
            {
                shell.Dispatcher.Dispatch(() => ShowOnboardingAsync(shell));
                shell.Dispatcher.Dispatch(async () => await services.GetRequiredService<Services.SessionReminderService>().RefreshAsync());
            }
        };
        window.StatusBarTheme = RequestedTheme == AppTheme.Dark ? StatusBarTheme.Dark : StatusBarTheme.Light;
        window.Resumed += async (_, _) => await services.GetRequiredService<ViewModels.ConferenceViewModel>().ResumeAsync();
        window.Stopped += (_, _) => services.GetRequiredService<ViewModels.ConferenceViewModel>().PauseRefresh();
        return window;
    }

    private async void ShowOnboardingAsync(AppShell shell)
    {
        if (!onboarding.TryBegin())
            return;
        try
        {
            var page = services.GetRequiredService<Pages.OnboardingPage>();
            page.Configure(onboarding);
            await shell.Navigation.PushModalAsync(page);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            onboarding.RetryLaunch();
            logger.LogWarning(exception, "Could not open first-run planning. Browsing remains available.");
        }
    }

    private void ApplyStatusBarTheme()
    {
        foreach (var window in Windows)
            window.StatusBarTheme = RequestedTheme == AppTheme.Dark ? StatusBarTheme.Dark : StatusBarTheme.Light;
    }
}
