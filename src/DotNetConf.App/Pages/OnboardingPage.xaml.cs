using DotNetConf.App.Controls;
using DotNetConf.App.Services;
using DotNetConf.App.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;

namespace DotNetConf.App.Pages;

public partial class OnboardingPage : ContentPage
{
    private readonly PlannerViewModel planner;
    private readonly SessionReminderService reminders;
    private readonly ILogger<OnboardingPage> logger;
    private FirstRunPlannerState? state;
    private bool leaving;
    private bool requestingReminders;
    private bool permissionAttempted;
    public IReadOnlyList<View> Slides { get; }
    public bool CanGoBack => Carousel.Position > 0;
    public string ProgressText => $"{Carousel.Position + 1} of 3";
    public string NextText => Carousel.Position switch
    {
        0 => "Make it mine",
        1 => planner.HasAddedSessions ? "Next: session reminders" : "Continue to reminders",
        _ => "Let's explore"
    };
    public bool RequestingReminders => requestingReminders;
    public bool CanRequestReminders => !requestingReminders && !reminders.Enabled;
    public bool CanNavigate => !requestingReminders && !leaving;
    public string ReminderStatus => reminders.Status;
    public bool ShowNotificationSettings => permissionAttempted && !reminders.Enabled;

    public OnboardingPage(PlannerViewModel planner, SessionReminderService reminders, ILogger<OnboardingPage> logger)
    {
        this.planner = planner;
        this.reminders = reminders;
        this.logger = logger;
        InitializeComponent();
        On<iOS>().SetModalPresentationStyle(UIModalPresentationStyle.FullScreen);
        var welcome = (View)((DataTemplate)Resources["WelcomeTemplate"]).CreateContent();
        welcome.BindingContext = this;
        var notificationStep = (View)((DataTemplate)Resources["RemindersTemplate"]).CreateContent();
        notificationStep.BindingContext = this;
        Slides = [welcome, new PlannerView { BindingContext = planner }, notificationStep];
        BindingContext = this;
        planner.PropertyChanged += PlannerChanged;
        reminders.PropertyChanged += RemindersChanged;
    }

    public void Configure(FirstRunPlannerState onboarding) => state = onboarding;

    private void PlannerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PlannerViewModel.HasAddedSessions))
            OnPropertyChanged(nameof(NextText));
    }

    private void RemindersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) =>
        UpdatePermissionState();

    private async void PositionChanged(object? sender, PositionChangedEventArgs args)
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(NextText));
        OnPropertyChanged(nameof(ProgressText));
        if (args.PreviousPosition == 1)
            planner.Cancel();
        if (args.CurrentPosition == 1)
            await planner.InitializeAsync();
    }

    private async void NextClicked(object? sender, EventArgs args)
    {
        if (!CanNavigate)
            return;
        if (Carousel.Position < 2)
            Carousel.ScrollTo(Carousel.Position + 1, animate: false);
        else
            await FinishAsync();
    }

    private void BackClicked(object? sender, EventArgs args)
    {
        if (CanNavigate && Carousel.Position > 0)
            Carousel.ScrollTo(Carousel.Position - 1, animate: false);
    }

    private async void SkipClicked(object? sender, EventArgs args) => await FinishAsync();

    protected override bool OnBackButtonPressed()
    {
        if (CanGoBack && CanNavigate)
            Carousel.ScrollTo(Carousel.Position - 1, animate: false);
        else if (CanNavigate)
            _ = FinishAsync();
        return true;
    }

    private async Task FinishAsync()
    {
        if (!CanNavigate)
            return;
        leaving = true;
        OnPropertyChanged(nameof(CanNavigate));
        planner.Cancel();
        try
        {
            state?.Complete();
            await Navigation.PopModalAsync();
            planner.PropertyChanged -= PlannerChanged;
            reminders.PropertyChanged -= RemindersChanged;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            logger.LogError(exception, "Could not finish onboarding.");
            await DisplayAlertAsync("Couldn't finish setup", "Please try again. Your saved sessions are kept.", "OK");
        }
        finally
        {
            leaving = false;
            OnPropertyChanged(nameof(CanNavigate));
        }
    }

    private async void EnableRemindersClicked(object? sender, EventArgs args)
    {
        if (!CanRequestReminders)
            return;
        requestingReminders = true;
        UpdatePermissionState();
        await reminders.EnableAsync();
        permissionAttempted = true;
        requestingReminders = false;
        UpdatePermissionState();
    }

    private void UpdatePermissionState()
    {
        OnPropertyChanged(nameof(RequestingReminders));
        OnPropertyChanged(nameof(CanRequestReminders));
        OnPropertyChanged(nameof(CanNavigate));
        OnPropertyChanged(nameof(ReminderStatus));
        OnPropertyChanged(nameof(ShowNotificationSettings));
        Carousel.IsSwipeEnabled = !requestingReminders;
    }

    private void OpenSettingsClicked(object? sender, EventArgs args) => AppInfo.ShowSettingsUI();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (permissionAttempted)
        {
            await reminders.RefreshAsync();
            UpdatePermissionState();
        }
    }

    protected override void OnDisappearing()
    {
        planner.Cancel();
        base.OnDisappearing();
    }
}
