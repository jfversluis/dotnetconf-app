namespace DotNetConf.App;

public partial class App : Application
{
    private readonly IServiceProvider services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        this.services = services;
        RequestedThemeChanged += (_, _) => ApplyStatusBarTheme();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(services.GetRequiredService<AppShell>());
        window.StatusBarTheme = RequestedTheme == AppTheme.Dark ? StatusBarTheme.Dark : StatusBarTheme.Light;
        return window;
    }

    private void ApplyStatusBarTheme()
    {
        foreach (var window in Windows)
            window.StatusBarTheme = RequestedTheme == AppTheme.Dark ? StatusBarTheme.Dark : StatusBarTheme.Light;
    }
}
