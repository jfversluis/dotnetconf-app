using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Pages;

public partial class HomePage : ConferencePage
{
    private readonly IPreferences preferences;

    public HomePage(ConferenceViewModel conference, IPreferences preferences) : base(conference)
    {
        this.preferences = preferences;
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (preferences.Get("planner-intro-shown-v1", false))
            return;
        preferences.Set("planner-intro-shown-v1", true);
        await Shell.Current.GoToAsync("planner");
    }
}
