using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Pages;

public partial class SpeakersPage : ConferencePage
{
    public SpeakersPage(ConferenceViewModel conference) : base(conference) => InitializeComponent();
}
