using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetConf.Core;
using System.Collections.ObjectModel;

namespace DotNetConf.App.ViewModels;

public sealed partial class SpeakerViewModel : ObservableObject
{
    private const string PlaceholderPicture = "speaker_placeholder.png";

    public SpeakerViewModel(Speaker model, ConferenceViewModel owner)
    {
        Model = model;
        this.owner = owner;
        ProfilePicture = CreateProfilePicture(model);
        OpenCommand = new AsyncRelayCommand(() => owner.OpenSpeakerAsync(Id));
    }

    private readonly ConferenceViewModel owner;
    public Speaker Model { get; private set; }
    public string Id => Model.Id;
    public string AutomationId => $"speaker-{Id}";
    public string Name => Model.Name;
    public string Tagline => Model.Tagline;
    public string Bio => string.IsNullOrWhiteSpace(Model.Bio) ? "Biography to be announced." : Model.Bio;
    [ObservableProperty] public partial ImageSource ProfilePicture { get; set; }
    public ObservableCollection<SessionViewModel> Sessions { get; } = [];
    public IAsyncRelayCommand OpenCommand { get; }

    public void Update(Speaker model)
    {
        if (Model.ProfilePicture != model.ProfilePicture ||
            (ProfilePicture is FileImageSource && !string.IsNullOrWhiteSpace(model.ProfilePicture)))
            ProfilePicture = CreateProfilePicture(model);
        if (Model == model)
            return;
        Model = model;
        OnPropertyChanged(string.Empty);
    }

    public void UpdateSessions(IEnumerable<SessionViewModel> sessions)
    {
        var next = sessions.ToArray();
        CollectionUpdates.Apply(Sessions, next);
    }

    public void UseFallbackPicture(Exception exception)
    {
        owner.ReportPhotoFailure(Id, exception);
        if (ProfilePicture is not FileImageSource current || current.File != PlaceholderPicture)
            ProfilePicture = ImageSource.FromFile(PlaceholderPicture);
    }

    private ImageSource CreateProfilePicture(Speaker model)
    {
        if (!string.IsNullOrWhiteSpace(model.ProfilePicture))
        {
            try
            {
                var uri = new Uri(model.ProfilePicture, UriKind.Absolute);
                // The feed includes an HTTP photo; use HTTPS without native cleartext exceptions.
                if (uri.Scheme == Uri.UriSchemeHttp)
                    uri = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = uri.IsDefaultPort ? -1 : uri.Port }.Uri;
                return ImageSource.FromUri(uri);
            }
            catch (UriFormatException exception)
            {
                owner.ReportPhotoFailure(model.Id, exception);
            }
        }
        return ImageSource.FromFile(PlaceholderPicture);
    }
}
