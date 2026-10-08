using Microsoft.Extensions.Logging;

namespace DotNetConf.App.Pages;

public partial class SettingsPage : ContentPage
{
    private static readonly AppTheme[] Themes = [AppTheme.Unspecified, AppTheme.Light, AppTheme.Dark];
    private readonly IPreferences preferences;
    private readonly ILogger<SettingsPage> logger;
    private bool initializing;

    public IReadOnlyList<string> AppearanceChoices { get; } = ["System", "Light", "Dark"];
    public string TimeZoneLabel => TimeZoneInfo.Local.DisplayName;

    public SettingsPage(IPreferences preferences, ILogger<SettingsPage> logger)
    {
        this.preferences = preferences;
        this.logger = logger;
        InitializeComponent();
        BindingContext = this;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        initializing = true;
        AppearancePicker.SelectedIndex = Array.IndexOf(Themes, Application.Current!.UserAppTheme);
        initializing = false;
        TimeZoneInfo.ClearCachedData();
        OnPropertyChanged(nameof(TimeZoneLabel));
    }

    private async void OnAppearanceChanged(object? sender, EventArgs e)
    {
        if (initializing || AppearancePicker.SelectedIndex < 0)
            return;

        var app = Application.Current!;
        var theme = Themes[AppearancePicker.SelectedIndex];
        if (theme == app.UserAppTheme)
            return;
        try
        {
            preferences.Set("appearance", theme.ToString());
            app.UserAppTheme = theme;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(exception, "Could not save appearance preference.");
            initializing = true;
            AppearancePicker.SelectedIndex = Array.IndexOf(Themes, app.UserAppTheme);
            initializing = false;
            await DisplayAlertAsync("Couldn't save appearance",
                "Your appearance has not changed. Try choosing it again.", "OK");
        }
    }

    private async void OnAboutClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("about");
}
