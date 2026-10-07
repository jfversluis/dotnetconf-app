using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Pages;

public partial class HomePage : ConferencePage
{
    public HomePage(ConferenceViewModel conference) : base(conference) => InitializeComponent();
}
