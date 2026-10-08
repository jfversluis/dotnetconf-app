using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Pages;

public partial class CommunityPage : ConferencePage
{
    public bool HasMapConfiguration { get; } = CommunityEventsPage.IsMapConfigured();

    public CommunityPage(ConferenceViewModel conference) : base(conference) => InitializeComponent();
}
