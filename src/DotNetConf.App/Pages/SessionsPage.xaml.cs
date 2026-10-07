using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Pages;

public partial class SessionsPage : ConferencePage
{
    public SessionsPage(ConferenceViewModel conference) : base(conference) => InitializeComponent();
}
