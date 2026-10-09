using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Pages;

public partial class SessionFiltersPage : ConferencePage
{
    public SessionFiltersPage(ConferenceViewModel conference) : base(conference) => InitializeComponent();

    private async void OnShowSessionsClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("..");
}
