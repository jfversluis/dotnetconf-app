using System.ComponentModel;
using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Controls;

public partial class SpeakerDetailView : ContentView
{
    private ConferenceViewModel? conference;
    private string? speakerId;
    internal TextScaleMonitor TextScaleMonitor => ScaleMonitor;

    public SpeakerDetailView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void Show(ConferenceViewModel owner, string? id)
    {
        conference = owner;
        speakerId = id;
        Status.BindingContext = owner;
        UpdateSpeaker();
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        if (conference is null)
            return;
        conference.PropertyChanged += OnConferenceChanged;
        conference.AgendaChanged += OnAgendaChanged;
        await conference.InitializeAsync();
        UpdateSpeaker();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (conference is null)
            return;
        conference.PropertyChanged -= OnConferenceChanged;
        conference.AgendaChanged -= OnAgendaChanged;
    }

    private void OnConferenceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ConferenceViewModel.IsBusy))
            UpdateSpeaker();
    }

    private void OnAgendaChanged(object? sender, EventArgs args) => UpdateSpeaker();

    private void UpdateSpeaker()
    {
        if (conference is null)
            return;
        var speaker = conference.FindSpeaker(speakerId);
        BindingContext = speaker;
        Details.IsVisible = speaker is not null;
        NotFound.IsVisible = speaker is null && !conference.IsBusy;
    }
}
