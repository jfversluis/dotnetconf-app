using DotNetConf.App.ViewModels;
using System.ComponentModel;

namespace DotNetConf.App.Pages;

public class ConferencePage : ContentPage
{
    protected ConferenceViewModel Conference { get; }
    private string? lastAnnouncedError;

    public ConferencePage(ConferenceViewModel conference)
    {
        Conference = conference;
        BindingContext = conference;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Conference.PropertyChanged += OnConferencePropertyChanged;
        await Conference.InitializeAsync();
    }

    protected override void OnDisappearing()
    {
        Conference.PropertyChanged -= OnConferencePropertyChanged;
        base.OnDisappearing();
    }

    private void OnConferencePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ConferenceViewModel.RefreshMessage))
            return;
        var message = Conference.RefreshMessage;
        if (message.Length > 0 && message != lastAnnouncedError &&
            (OperatingSystem.IsIOS() || OperatingSystem.IsAndroid()))
            SemanticScreenReader.Default.Announce(message);
        lastAnnouncedError = message;
    }
}
