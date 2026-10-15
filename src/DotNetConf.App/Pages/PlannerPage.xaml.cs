using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Pages;

public partial class PlannerPage : ContentPage
{
    private readonly PlannerViewModel viewModel;

    public PlannerPage(PlannerViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.InitializeAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.Cancel();
        base.OnDisappearing();
    }
}
