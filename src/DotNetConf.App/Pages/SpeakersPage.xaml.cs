using DotNetConf.App.ViewModels;
using DotNetConf.App.Services;

namespace DotNetConf.App.Pages;

public partial class SpeakersPage : AdaptiveConferencePage
{
    public SpeakersPage(ConferenceViewModel conference, DetailNavigationService navigation) : base(conference)
    {
        InitializeComponent();
        ConfigureDetails(DetailKind.Speaker, navigation);
        ConfigureSearchViewport(SearchField, SearchLayout, SearchResults);
    }

    protected override void OnListLayoutChanged(bool scrollHeader, bool stacked) =>
        SpeakerHeading.IsVisible = !scrollHeader;
}
