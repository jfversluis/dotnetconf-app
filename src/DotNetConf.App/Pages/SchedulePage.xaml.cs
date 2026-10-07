using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Pages;

public partial class SchedulePage : ConferencePage
{
    public SchedulePage(ConferenceViewModel conference) : base(conference) => InitializeComponent();
}
