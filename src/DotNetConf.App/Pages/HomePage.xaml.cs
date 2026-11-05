using DotNetConf.App.ViewModels;
using DotNetConf.App.Controls;
using Syncfusion.Maui.Toolkit.BottomSheet;
using Microsoft.Extensions.Logging;

namespace DotNetConf.App.Pages;

public partial class HomePage : ConferencePage
{
    private readonly AskViewModel ask;
    private readonly ConferenceViewModel conference;
    private readonly ILogger<HomePage> logger;
    private CancellationTokenSource? readiness;
    private bool navigating;
    public HomePage(ConferenceViewModel conference, AskViewModel ask, ILogger<HomePage> logger) : base(conference)
    {
        InitializeComponent();
        this.ask = ask;
        this.conference = conference;
        this.logger = logger;
        AskContent.BindingContext = ask;
        ask.BeforeNavigation += DismissForNavigationAsync;
        ask.Announce += AnnounceOutcome;
        AskSheet.SizeChanged += (_, _) =>
        {
            AskSheet.ContentWidthMode = BottomSheetContentWidthMode.Custom;
            AskSheet.BottomSheetContentWidth = Math.Min(720, Math.Max(0, AskSheet.Width));
        };
    }

    protected override async void OnAppearing()
    {
        navigating = false;
        ask.Close();
        base.OnAppearing();
        conference.AgendaChanged += AgendaChanged;
        if (Window is { } window)
        {
            window.Activated += WindowActivated;
            window.Stopped += WindowStopped;
        }
        await CheckReadinessAsync();
    }

    protected override void OnDisappearing()
    {
        conference.AgendaChanged -= AgendaChanged;
        if (Window is { } window)
        {
            window.Activated -= WindowActivated;
            window.Stopped -= WindowStopped;
        }
        readiness?.Cancel();
        if (!navigating)
            CloseAsk();
        base.OnDisappearing();
    }

    private async Task CheckReadinessAsync()
    {
        readiness?.Cancel();
        readiness?.Dispose();
        readiness = new CancellationTokenSource();
        try
        {
            await ask.CheckAvailabilityAsync(readiness.Token);
            AskLauncher.IsVisible = ask.IsAvailable;
            ExploreSessions.IsVisible = !ask.IsAvailable;
        }
        catch (OperationCanceledException) { }
    }

    private async void AgendaChanged(object? sender, EventArgs e) => await CheckReadinessAsync();
    private async void WindowActivated(object? sender, EventArgs e) => await CheckReadinessAsync();
    private void WindowStopped(object? sender, EventArgs e) => CloseAsk();
    private void OpenAskClicked(object? sender, EventArgs e)
    {
        ask.Open();
        AskContent.RestoreMotion();
        AskSheet.AnimationDuration = AskGlow.ReduceMotion ? 0 : 200;
        AskSheet.State = BottomSheetState.FullExpanded;
        AskSheet.Show();
        AskContent.Dispatcher.Dispatch(() => AskContent.FocusInput());
    }

    private void CloseAsk()
    {
        AskContent.Deactivate();
        ask.Close();
        AskSheet.Close();
        AskLauncherButton.SetSemanticFocus();
    }

    private void CloseAskRequested(object? sender, EventArgs e) => CloseAsk();
    private void AskStateChanged(object? sender, StateChangedEventArgs e)
    {
        if (navigating)
            return;
        if (e.NewState == BottomSheetState.Collapsed)
        {
            CloseAsk();
            return;
        }
        if (e.NewState == BottomSheetState.Hidden)
        {
            AskContent.Deactivate();
            ask.Close();
            AskLauncherButton.SetSemanticFocus();
        }
    }

    private async Task DismissForNavigationAsync()
    {
        navigating = true;
        AskContent.Deactivate();
        AskSheet.Close();
        await Task.Delay(AskGlow.ReduceMotion ? 0 : 220);
    }

    private async void AskScheduleRequested(object? sender, EventArgs e)
    {
        CloseAsk();
        try { await Shell.Current.GoToAsync("//schedule"); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(exception, "Could not open My Schedule from Ask.");
            await DisplayAlertAsync("Couldn't open My Schedule", "Your saved choices are kept. Try the My Schedule tab.", "OK");
        }
    }

    protected override bool OnBackButtonPressed()
    {
        if (AskSheet.IsOpen)
        {
            CloseAsk();
            return true;
        }
        return base.OnBackButtonPressed();
    }

    private void AnnounceOutcome(string message)
    {
        if (AskSheet.IsOpen && message.Length > 0)
            SemanticScreenReader.Default.Announce(message);
    }
}
