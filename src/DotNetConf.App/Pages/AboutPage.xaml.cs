using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Pages;

public partial class AboutPage : ConferencePage
{
    public AboutPage(ConferenceViewModel conference) : base(conference) => InitializeComponent();

    private async void OnDependenciesClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("third-party");

    private async void OnPrivacyClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("license", new ShellNavigationQueryParameters
        {
            ["notice"] = new DependencyNotice("Privacy policy", "", "PRIVACY.txt")
        });
}
