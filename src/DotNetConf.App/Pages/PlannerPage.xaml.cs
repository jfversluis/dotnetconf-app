using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Pages;

public partial class PlannerPage : ContentPage, IQueryAttributable
{
    private readonly PlannerViewModel viewModel;
    private FirstRunPlannerState? onboarding;
    private Shell? observedShell;
    private bool leaving;

    public PlannerPage(PlannerViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("firstRun", out var value) && value is FirstRunPlannerState state)
        {
            onboarding = state;
            Title = "Welcome to .NET Conf";
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (onboarding is not null && observedShell is null)
        {
            observedShell = Shell.Current;
            observedShell.Navigated += OnShellNavigated;
        }
        await viewModel.InitializeAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.Cancel();
        base.OnDisappearing();
        if (onboarding is not null && observedShell is { } shell)
            Dispatcher.Dispatch(() => CompleteIfRemoved(shell));
    }

    private void CompleteIfRemoved(Shell shell)
    {
        if (ReferenceEquals(shell.CurrentPage, this)
            || shell.Items.SelectMany(item => item.Items)
                .Any(section => section.Navigation.NavigationStack.Contains(this)
                    || section.Navigation.ModalStack.Contains(this)))
            return;
        CompleteOnboarding();
    }

    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs args)
    {
        if (observedShell is null || ReferenceEquals(observedShell.CurrentPage, this))
            return;
        CompleteOnboarding();
    }

    private void CompleteOnboarding()
    {
        if (observedShell is { } shell)
            shell.Navigated -= OnShellNavigated;
        observedShell = null;
        onboarding?.Complete();
    }

    private async void SkipClicked(object? sender, EventArgs args) =>
        await LeaveAsync(onboarding is null ? ".." : "//sessions");

    private async void ViewScheduleClicked(object? sender, EventArgs args) =>
        await LeaveAsync("//schedule");

    private void GenerateClicked(object? sender, EventArgs args) => RequestEditor.Unfocus();

    private async Task LeaveAsync(string route)
    {
        if (leaving)
            return;
        leaving = true;
        viewModel.Cancel();
        try
        {
            // Switching Shell tabs retains their stacks; remove this page from its owning tab first.
            if (route.StartsWith("//", StringComparison.Ordinal))
                await Shell.Current.GoToAsync("..", false);
            await Shell.Current.GoToAsync(route);
            CompleteOnboarding();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            await DisplayAlertAsync("Couldn't open that page", "Please try again, or use the native back button to return to browsing.", "OK");
        }
        finally
        {
            leaving = false;
        }
    }
}
