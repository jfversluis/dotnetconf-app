using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Pages;

public class ConferencePage : ContentPage
{
    protected ConferenceViewModel Conference { get; }

    public ConferencePage(ConferenceViewModel conference)
    {
        Conference = conference;
        BindingContext = conference;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Conference.InitializeAsync();
    }
}
