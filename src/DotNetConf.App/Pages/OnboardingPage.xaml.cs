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
    private bool confirmingSchedule;
    private bool requestingReminders;
    private bool permissionAttempted;
    private readonly ContentView scheduleStep;
    private PlannerView? plannerView;
    private readonly OnboardingPlannerPreparation schedulePreparation = new();
    private bool pageVisible;
    private bool observingReminders;
    public bool PreparingScheduleStep { get; private set; }
    public IReadOnlyList<View> Slides { get; }
    public bool CanGoBack => Carousel.Position > 0;
    public string ProgressText => $"{Carousel.Position + 1} of 3";
    public string NextText => Carousel.Position == 2 ? "Done" : Carousel.Position == 1 ? "Continue" : "Next";
    public bool RequestingReminders => requestingReminders;
    public bool CanRequestReminders => !requestingReminders && (reminders.CanEnable || reminders.CanRetry);
    public bool ShowEnableReminders => reminders.CanEnable;
    public bool ShowRetryReminders => reminders.CanRetry;
    public bool CanNavigate => !requestingReminders && !leaving && !confirmingSchedule && !planner.IsSaving;
    public string ReminderStatus => reminders.Status;
    public bool ShowReminderStatus => permissionAttempted || reminders.Enabled || reminders.CanRetry || reminders.PermissionBlocked;
    public bool ShowNotificationSettings => reminders.PermissionBlocked;

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
    }

    private async Task PrepareScheduleStepAsync()
    {
        PreparingScheduleStep = true;
        OnPropertyChanged(nameof(PreparingScheduleStep));
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
        if (!pageVisible || Carousel.Position != 1 || plannerView is not null)
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
        plannerView.UseOnboardingSafeArea();
        plannerView.IsActive = Carousel.Position == 1;
        scheduleStep.Content = plannerView;
    }

    public void Configure(FirstRunPlannerState onboarding) => state = onboarding;

    private void RemindersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) =>
        UpdatePermissionState();

    private void PlannerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PlannerViewModel.HasCheckedModel))
            ShowReadyPlanner();
        if (args.PropertyName == nameof(PlannerViewModel.IsSaving))
            OnPropertyChanged(nameof(CanNavigate));
    }

    private async void PositionChanged(object? sender, PositionChangedEventArgs args)
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(NextText));
        OnPropertyChanged(nameof(ProgressText));
        if (plannerView is not null)
            plannerView.IsActive = pageVisible && Carousel.Position == 1;
        if (args.PreviousPosition == 1)
        {
            schedulePreparation.Leave();
            planner.Cancel();
        }
        if (args.CurrentPosition == 1)
        {
            ShowReadyPlanner();
            await PrepareOrResumeScheduleStepAsync();
        }
    }

    private async Task PrepareOrResumeScheduleStepAsync()
    {
        if (await schedulePreparation.EnterAsync(PrepareScheduleStepAsync,
                () => planner.InitializeAsync(resumeWithPreview: true),
                () => pageVisible && Carousel.Position == 1, () => planner.CancellationRevision))
            ShowReadyPlanner();
    }

    private async void NextClicked(object? sender, EventArgs args)
    {
        if (!CanNavigate)
            return;
        if (Carousel.Position == 1 && !await ConfirmScheduleDepartureAsync(continuing: true))
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
        if (Carousel.Position != 2 && !await ConfirmScheduleDepartureAsync())
            return;
        leaving = true;
        OnPropertyChanged(nameof(CanNavigate));
        if (plannerView is not null)
            plannerView.IsActive = false;
        planner.Cancel();
        var host = Window?.Page;
        try
        {
            if (state is null)
                throw new InvalidOperationException("Onboarding was not configured.");
            await state.CompleteAfterDismissalAsync(async () => await Navigation.PopModalAsync());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            logger.LogError(exception, "Could not finish onboarding.");
            if (host is not null)
                await host.DisplayAlertAsync("Couldn't finish setup", "Please try again. Your saved sessions are kept.", "OK");
        }
        finally
        {
            leaving = false;
            OnPropertyChanged(nameof(CanNavigate));
            if (plannerView is not null)
                plannerView.IsActive = pageVisible && Carousel.Position == 1;
        }
    }

    private async Task<bool> ConfirmScheduleDepartureAsync(bool continuing = false)
    {
        var confirmation = UnsavedSuggestionsConfirmation.ForOnboarding(
            planner.SelectedCount > 0, planner.HasAddedSessions, continuing);
        if (confirmation is null)
            return true;

        confirmingSchedule = true;
        OnPropertyChanged(nameof(CanNavigate));
        try
        {
            var leave = await DisplayAlertAsync(confirmation.Title, confirmation.Message,
                confirmation.Accept, UnsavedSuggestionsConfirmation.Cancel);
            if (!leave && Carousel.Position != 1 && plannerView is not null)
                Carousel.ScrollTo(1, animate: false);
            return leave;
        }
        finally
        {
            confirmingSchedule = false;
            OnPropertyChanged(nameof(CanNavigate));
        }
    }

    private async void EnableRemindersClicked(object? sender, EventArgs args) =>
        await UpdateRemindersAsync(retry: false);

    private async void RetryRemindersClicked(object? sender, EventArgs args) =>
        await UpdateRemindersAsync(retry: true);

    private async Task UpdateRemindersAsync(bool retry)
    {
        if (!CanRequestReminders)
            return;
        requestingReminders = true;
        UpdatePermissionState();
        try
        {
            if (retry && !reminders.RetryEnabling)
                await reminders.RefreshAsync();
            else
                await reminders.EnableAsync();
            permissionAttempted = true;
        }
        finally
        {
            requestingReminders = false;
            UpdatePermissionState();
        }
    }

    private void UpdatePermissionState()
    {
        OnPropertyChanged(nameof(RequestingReminders));
        OnPropertyChanged(nameof(CanRequestReminders));
        OnPropertyChanged(nameof(ShowEnableReminders));
        OnPropertyChanged(nameof(ShowRetryReminders));
        OnPropertyChanged(nameof(CanNavigate));
        OnPropertyChanged(nameof(ReminderStatus));
        OnPropertyChanged(nameof(ShowReminderStatus));
        OnPropertyChanged(nameof(ShowNotificationSettings));
    }

    private void OpenSettingsClicked(object? sender, EventArgs args) => AppInfo.ShowSettingsUI();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        pageVisible = true;
        if (!observingReminders)
        {
            reminders.PropertyChanged += RemindersChanged;
            planner.PropertyChanged += PlannerChanged;
            observingReminders = true;
        }
        if (plannerView is not null)
            plannerView.IsActive = Carousel.Position == 1;
        if (permissionAttempted)
        {
            await reminders.RefreshAsync();
            UpdatePermissionState();
        }
        if (schedulePreparation.HasStarted && Carousel.Position == 1)
            await PrepareOrResumeScheduleStepAsync();
    }

    protected override void OnDisappearing()
    {
        pageVisible = false;
        schedulePreparation.Leave();
        if (plannerView is not null)
            plannerView.IsActive = false;
        if (observingReminders)
        {
            reminders.PropertyChanged -= RemindersChanged;
            planner.PropertyChanged -= PlannerChanged;
            observingReminders = false;
        }
        planner.Cancel();
        base.OnDisappearing();
    }
}
