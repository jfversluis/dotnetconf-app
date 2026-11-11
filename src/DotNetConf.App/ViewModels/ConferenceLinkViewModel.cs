using CommunityToolkit.Mvvm.Input;
using DotNetConf.Core;

namespace DotNetConf.App.ViewModels;

public sealed class ConferenceLinkViewModel(ConferenceLink link, ConferenceViewModel owner)
{
    public string Title => link.Title;
    public string Url => link.Url;
    public string Description => $"{Title}. Opens {new Uri(Url).Host} in your browser.";
    public string AutomationId => $"link-{Uri.EscapeDataString(Url)}";
    public IAsyncRelayCommand OpenCommand { get; } = new AsyncRelayCommand(() => owner.OpenLinkAsync(link));
}
