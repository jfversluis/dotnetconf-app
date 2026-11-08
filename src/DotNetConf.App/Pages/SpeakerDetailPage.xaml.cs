using DotNetConf.App.ViewModels;
using DotNetConf.App.Services;

namespace DotNetConf.App.Pages;

public partial class SpeakerDetailPage : ContentPage, IQueryAttributable
{
    private readonly ConferenceViewModel conference;
    private readonly FoldableViewport viewport;
    private string? speakerId;
    internal string? SpeakerId => speakerId;
    internal double? TextScale => Detail.TextScaleMonitor.TextScale;
    internal DetailLayout? CurrentLayout(double width, double height, double textScale) =>
        viewport.IsCurrent ? viewport.GetLayout(width, height, textScale) : null;

    public SpeakerDetailPage(ConferenceViewModel conference, DetailNavigationService navigation)
    {
        InitializeComponent();
        this.conference = conference;
        viewport = new FoldableViewport(this, this, navigation.RequestLayoutSync, navigation.FoldableRegionSource);
        Detail.TextScaleMonitor.TextScaleChanged += (_, _) => navigation.RequestLayoutSync();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        speakerId = query.TryGetValue("id", out var value) && value is string id ? Uri.UnescapeDataString(id) : null;
        Detail.Show(conference, speakerId);
    }
}
