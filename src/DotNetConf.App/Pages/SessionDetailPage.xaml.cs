using System.ComponentModel;
using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Pages;

public partial class SessionDetailPage : ContentPage, IQueryAttributable
{
    private readonly ConferenceViewModel conference;
    private string? sessionId;
    internal string? SessionId => sessionId;

    public SessionDetailPage(ConferenceViewModel conference)
    {
        InitializeComponent();
        this.conference = conference;
        Status.BindingContext = conference;
        Shell.SetBackButtonBehavior(this, new BackButtonBehavior { AccessibilityLabel = "Back to previous page" });
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        sessionId = query.TryGetValue("id", out var value) && value is string id ? Uri.UnescapeDataString(id) : null;
        UpdateSession();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        conference.PropertyChanged += OnConferenceChanged;
        await conference.InitializeAsync();
        UpdateSession();
    }

    protected override void OnDisappearing()
    {
        conference.PropertyChanged -= OnConferenceChanged;
        base.OnDisappearing();
    }

    private void OnConferenceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ConferenceViewModel.IsBusy) && !conference.IsBusy)
            UpdateSession();
    }

    private void UpdateSession()
    {
        var session = conference.FindSession(sessionId);
        BindingContext = session;
        Details.IsVisible = session is not null;
        NotFound.IsVisible = session is null && !conference.IsBusy;
        TimeZone.Text = conference.TimeZoneLabel;
    }
}
