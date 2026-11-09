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
        var links = services.GetRequiredService<Services.SessionLinkService>();
        services.GetRequiredService<Plugin.LocalNotification.INotificationService>().NotificationActionTapped += args =>
        {
            if (args.IsTapped)
                links.ReceiveReminder(args.Request.ReturningData);
        };
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
        var links = services.GetRequiredService<Services.SessionLinkService>();
        ModalPopped += (_, _) => links.Resume();
        var window = new Window(shell);
        var conference = services.GetRequiredService<ViewModels.ConferenceViewModel>();
        var sessionClock = window.Dispatcher.CreateTimer();
        sessionClock.Interval = TimeSpan.FromSeconds(30);
        sessionClock.Tick += (_, _) => conference.RefreshSessionStatuses(DateTimeOffset.UtcNow);
        services.GetRequiredService<Services.DetailNavigationService>().Attach(shell, window);
        shell.Loaded += (_, _) => shell.Dispatcher.Dispatch(() => ShowOnboardingAsync(shell));
        links.Attach(shell);
        window.Activated += (_, _) =>
        {
            conference.RefreshSessionStatuses(DateTimeOffset.UtcNow);
            sessionClock.Start();
            if (shell.IsLoaded)
            {
                shell.Dispatcher.Dispatch(() => ShowOnboardingAsync(shell));
                shell.Dispatcher.Dispatch(async () => await services.GetRequiredService<Services.SessionReminderService>().RefreshAsync());
            }
            shell.Dispatcher.Dispatch(() => Services.BackNavigation.Update(shell));
            links.Resume();
        };
        window.StatusBarTheme = RequestedTheme == AppTheme.Dark ? StatusBarTheme.Dark : StatusBarTheme.Light;
        window.Resumed += async (_, _) => await conference.ResumeAsync();
        window.Stopped += (_, _) =>
        {
            sessionClock.Stop();
            conference.PauseRefresh();
        };
        window.Destroying += (_, _) => sessionClock.Stop();
        return window;
    }

    private async void ShowOnboardingAsync(AppShell shell)
    {
        if (shell.Navigation.ModalStack.Any(page => page is Pages.OnboardingPage))
            return;
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

    protected override void OnAppLinkRequestReceived(Uri uri) =>
        services.GetRequiredService<Services.SessionLinkService>().Receive(uri.AbsoluteUri);
}
