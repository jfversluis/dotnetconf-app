using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Pages;

public partial class AboutPage : ConferencePage
{
    public AboutPage(ConferenceViewModel conference) : base(conference) => InitializeComponent();
}
