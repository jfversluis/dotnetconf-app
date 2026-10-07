using System.ComponentModel;
using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Pages;

public partial class SpeakerDetailPage : ContentPage, IQueryAttributable
{
    private readonly ConferenceViewModel conference;
    private string? speakerId;
    internal string? SpeakerId => speakerId;

    public SpeakerDetailPage(ConferenceViewModel conference)
    {
        InitializeComponent();
        this.conference = conference;
        Status.BindingContext = conference;
        Shell.SetBackButtonBehavior(this, new BackButtonBehavior { AccessibilityLabel = "Back to previous page" });
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        speakerId = query.TryGetValue("id", out var value) && value is string id ? Uri.UnescapeDataString(id) : null;
        UpdateSpeaker();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        conference.PropertyChanged += OnConferenceChanged;
        await conference.InitializeAsync();
        UpdateSpeaker();
    }

    protected override void OnDisappearing()
    {
        conference.PropertyChanged -= OnConferenceChanged;
        base.OnDisappearing();
    }

    private void OnConferenceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ConferenceViewModel.IsBusy) && !conference.IsBusy)
            UpdateSpeaker();
    }

    private void UpdateSpeaker()
    {
        var speaker = conference.FindSpeaker(speakerId);
        BindingContext = speaker;
        Details.IsVisible = speaker is not null;
        NotFound.IsVisible = speaker is null && !conference.IsBusy;
    }
}
