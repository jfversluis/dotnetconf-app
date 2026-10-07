using CommunityToolkit.Mvvm.Input;
using DotNetConf.Core;

namespace DotNetConf.App.ViewModels;

public sealed class SpeakerViewModel
{
    public SpeakerViewModel(Speaker model, ConferenceViewModel owner)
    {
        Model = model;
        OpenCommand = new AsyncRelayCommand(() => owner.OpenSpeakerAsync(Id));
        this.owner = owner;
    }

    private readonly ConferenceViewModel owner;
    public Speaker Model { get; }
    public string Id => Model.Id;
    public string AutomationId => $"speaker-{Id}";
    public string Name => Model.Name;
    public string Tagline => Model.Tagline;
    public string Bio => string.IsNullOrWhiteSpace(Model.Bio) ? "Biography to be announced." : Model.Bio;
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => part[0]));
    public IEnumerable<SessionViewModel> Sessions => owner.SessionsForSpeaker(Id);
    public IAsyncRelayCommand OpenCommand { get; }
}
