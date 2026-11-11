using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Pages;

public partial class PlannerPage : ContentPage, IQueryAttributable
{
    private readonly PlannerViewModel viewModel;
    private bool leaving;
    private bool confirmingDeparture;
    private bool navigationApproved;
    private Shell? observedShell;

    public PlannerPage(PlannerViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("gaps", out var value) && value is string text && Uri.UnescapeDataString(text) == "true")
        {
            viewModel.EnableGapMode();
            Title = "Fill schedule gaps";
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Planner.IsActive = true;
        if (observedShell is null && Shell.Current is { } shell)
        {
            observedShell = shell;
            observedShell.Navigating += OnShellNavigating;
        }
        await viewModel.InitializeAsync();
    }

    protected override void OnDisappearing()
    {
        Planner.IsActive = false;
        if (observedShell is not null)
        {
            observedShell.Navigating -= OnShellNavigating;
            observedShell = null;
        }
        viewModel.Cancel();
        base.OnDisappearing();
    }

    private async void OnShellNavigating(object? sender, ShellNavigatingEventArgs args)
    {
        if (navigationApproved || !args.CanCancel)
            return;
        if (viewModel.IsSaving)
        {
            args.Cancel();
            await ShowSavingMessageAsync();
            return;
        }
        if (viewModel.SelectedCount == 0)
            return;
        if (confirmingDeparture)
        {
            args.Cancel();
            return;
        }
        var deferral = args.GetDeferral();
        try
        {
            if (!await ConfirmDepartureAsync())
                args.Cancel();
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task<bool> ConfirmDepartureAsync()
    {
        if (viewModel.IsSaving)
        {
            await ShowSavingMessageAsync();
            return false;
        }
        var confirmation = UnsavedSuggestionsConfirmation.For(viewModel.SelectedCount > 0);
        if (confirmation is null)
            return true;
        confirmingDeparture = true;
        try
        {
            return await DisplayAlertAsync(confirmation.Title, confirmation.Message,
                confirmation.Accept, UnsavedSuggestionsConfirmation.Cancel);
        }
        finally
        {
            confirmingDeparture = false;
        }
    }

    private async Task ShowSavingMessageAsync()
    {
        if (confirmingDeparture)
            return;
        confirmingDeparture = true;
        try
        {
            await DisplayAlertAsync("Adding sessions", "Please wait until your sessions have been added before leaving.", "OK");
        }
        finally
        {
            confirmingDeparture = false;
        }
    }

    private async void ViewScheduleClicked(object? sender, EventArgs args)
    {
        if (leaving)
            return;
        leaving = true;
        try
        {
            if (!await ConfirmDepartureAsync())
                return;
            navigationApproved = true;
            viewModel.Cancel();
            await Shell.Current.GoToAsync("..", false);
            await Shell.Current.GoToAsync("//schedule");
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            await DisplayAlertAsync("Couldn't open your schedule", "Please try again, or return using the back button.", "OK");
        }
        finally
        {
            navigationApproved = false;
            leaving = false;
        }
    }
}
