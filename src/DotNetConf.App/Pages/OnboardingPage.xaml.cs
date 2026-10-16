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
    private readonly ContentView scheduleStep;
    private PlannerView? plannerView;
    private Task? schedulePreparation;
    private bool pageVisible;
    public bool PreparingScheduleStep { get; private set; } = true;
    public IReadOnlyList<View> Slides { get; }
    public Thickness HeroPadding => new(24,
        32 + Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.GetSafeAreaInsets(this).Top, 24, 28);
    public bool CanGoBack => Carousel.Position > 0;
    public string ProgressText => $"{Carousel.Position + 1} of 3";
    public string NextText => Carousel.Position == 2 ? "Done" : "Next";
    public bool RequestingReminders => requestingReminders;
    public bool CanRequestReminders => !requestingReminders && !reminders.Enabled;
    public bool CanNavigate => !requestingReminders && !leaving;
    public string ReminderStatus => reminders.Status;
    public bool ShowReminderStatus => permissionAttempted || reminders.Enabled;
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
        var intro = (View)((DataTemplate)Resources["ScheduleIntroTemplate"]).CreateContent();
        intro.BindingContext = this;
        scheduleStep = new ContentView { Content = intro, SafeAreaEdges = SafeAreaEdges.None };
        Slides = [welcome, scheduleStep, notificationStep];
        BindingContext = this;
        reminders.PropertyChanged += RemindersChanged;
    }

    private async Task PrepareScheduleStepAsync()
    {
        try
        {
            await planner.InitializeAsync();
        }
        finally
        {
            PreparingScheduleStep = false;
            OnPropertyChanged(nameof(PreparingScheduleStep));
        }
    }

    private void ShowReadyPlanner()
    {
        if (!pageVisible || !planner.IsReady || plannerView is not null)
            return;
        plannerView = new PlannerView { BindingContext = planner };
        var skip = new Button
        {
            Text = "Skip setup",
            Style = (Style)Microsoft.Maui.Controls.Application.Current!.Resources["QuietButton"],
            BorderWidth = 0,
            LineBreakMode = LineBreakMode.WordWrap,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            AutomationId = "onboarding-skip"
        };
        skip.SetBinding(IsEnabledProperty, Binding.Create(static (OnboardingPage page) => page.CanNavigate, source: this));
        skip.Clicked += SkipClicked;
        plannerView.SetHeroAction(skip);
        plannerView.SetHeroPadding(HeroPadding);
        scheduleStep.Content = plannerView;
    }

    public void Configure(FirstRunPlannerState onboarding) => state = onboarding;

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SafeAreaInsetsProperty.PropertyName)
        {
            UpdateHeroPadding();
        }
    }

    private void UpdateHeroPadding()
    {
        OnPropertyChanged(nameof(HeroPadding));
        plannerView?.SetHeroPadding(HeroPadding);
    }

    private void RemindersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) =>
        UpdatePermissionState();

    private void PositionChanged(object? sender, PositionChangedEventArgs args)
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(NextText));
        OnPropertyChanged(nameof(ProgressText));
        if (args.PreviousPosition == 1 && !PreparingScheduleStep)
            planner.Cancel();
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
        OnPropertyChanged(nameof(ShowReminderStatus));
        OnPropertyChanged(nameof(ShowNotificationSettings));
        Carousel.IsSwipeEnabled = !requestingReminders;
    }

    private void OpenSettingsClicked(object? sender, EventArgs args) => AppInfo.ShowSettingsUI();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        pageVisible = true;
        UpdateHeroPadding();
        if (permissionAttempted)
        {
            await reminders.RefreshAsync();
            UpdatePermissionState();
        }
        await (schedulePreparation ??= PrepareScheduleStepAsync());
        ShowReadyPlanner();
    }

    protected override void OnDisappearing()
    {
        pageVisible = false;
        planner.Cancel();
        base.OnDisappearing();
    }
}
