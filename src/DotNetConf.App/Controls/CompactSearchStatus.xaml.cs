using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Controls;

public partial class CompactSearchStatus : ContentView
{
    public CompactSearchStatus() => InitializeComponent();

    private async void ShowDetailsClicked(object? sender, EventArgs args)
    {
        if (BindingContext is not ConferenceViewModel conference)
            return;
        var page = Navigation.ModalStack.LastOrDefault() ?? Shell.Current?.CurrentPage;
        if (page is null)
            return;
        if (conference.CanRefresh)
        {
            if (await page.DisplayAlertAsync("Something needs attention", conference.RefreshMessage, "Retry", "Close"))
                conference.RequestRefreshCommand.Execute(null);
        }
        else
            await page.DisplayAlertAsync("Something needs attention", conference.RefreshMessage, "Close");
    }
}
