using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Pages;

public partial class PlannerPage : ContentPage
{
    private readonly PlannerViewModel viewModel;
    private bool leaving;

    public PlannerPage(PlannerViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Planner.IsActive = true;
        await viewModel.InitializeAsync();
    }

    protected override void OnDisappearing()
    {
        Planner.IsActive = false;
        viewModel.Cancel();
        base.OnDisappearing();
    }

    private async void ViewScheduleClicked(object? sender, EventArgs args)
    {
        if (leaving)
            return;
        leaving = true;
        viewModel.Cancel();
        try
        {
            await Shell.Current.GoToAsync("..", false);
            await Shell.Current.GoToAsync("//schedule");
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            await DisplayAlertAsync("Couldn't open your schedule", "Please try again, or return using the back button.", "OK");
        }
        finally
        {
            leaving = false;
        }
    }
}
