using Microsoft.Extensions.Logging;
using DotNetConf.App.Services;

namespace DotNetConf.App.Pages;

public partial class SettingsPage : ContentPage
{
    private static readonly AppTheme[] Themes = [AppTheme.Unspecified, AppTheme.Light, AppTheme.Dark];
    private readonly IPreferences preferences;
    private readonly ILogger<SettingsPage> logger;
    private bool initializing;
    private bool updatingReminders;
    public SessionReminderService Reminders { get; }

    public IReadOnlyList<string> AppearanceChoices { get; } = ["System", "Light", "Dark"];
    public string TimeZoneLabel => TimeZoneInfo.Local.DisplayName;

    public SettingsPage(IPreferences preferences, ILogger<SettingsPage> logger, SessionReminderService reminders)
    {
        this.preferences = preferences;
        this.logger = logger;
        Reminders = reminders;
        InitializeComponent();
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        initializing = true;
        AppearancePicker.SelectedIndex = Array.IndexOf(Themes, Application.Current!.UserAppTheme);
        initializing = false;
        TimeZoneInfo.ClearCachedData();
        OnPropertyChanged(nameof(TimeZoneLabel));
        await Reminders.RefreshAsync();
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

    private async void EnableRemindersClicked(object? sender, EventArgs e)
    {
        if (updatingReminders)
            return;
        updatingReminders = true;
        try
        {
            await Reminders.EnableAsync();
        }
        finally
        {
            updatingReminders = false;
        }
    }

    private async void DisableRemindersClicked(object? sender, EventArgs e)
    {
        if (updatingReminders)
            return;
        updatingReminders = true;
        try
        {
            await Reminders.DisableAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(exception, "Could not disable reminders.");
            await DisplayAlertAsync("Couldn't turn off reminders", "Try again or disable notifications in device settings.", "OK");
        }
        finally
        {
            updatingReminders = false;
        }
    }

    private void NotificationSettingsClicked(object? sender, EventArgs e) => AppInfo.ShowSettingsUI();
}
