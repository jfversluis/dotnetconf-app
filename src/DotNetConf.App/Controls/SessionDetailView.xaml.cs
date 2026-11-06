using System.ComponentModel;
using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Controls;

public partial class SessionDetailView : ContentView
{
    private ConferenceViewModel? conference;
    private string? sessionId;
    internal TextScaleMonitor TextScaleMonitor => ScaleMonitor;

    public static readonly BindableProperty ShowInlineShareProperty = BindableProperty.Create(
        nameof(ShowInlineShare), typeof(bool), typeof(SessionDetailView), false);
    public bool ShowInlineShare
    {
        get => (bool)GetValue(ShowInlineShareProperty);
        set => SetValue(ShowInlineShareProperty, value);
    }

    public SessionDetailView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void Show(ConferenceViewModel owner, string? id)
    {
        conference = owner;
        sessionId = id;
        Status.BindingContext = owner;
        UpdateSession();
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        if (conference is null)
            return;
        conference.PropertyChanged += OnConferenceChanged;
        conference.AgendaChanged += OnAgendaChanged;
        await conference.InitializeAsync();
        UpdateSession();
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
            UpdateSession();
    }

    private void OnAgendaChanged(object? sender, EventArgs args) => UpdateSession();

    private void UpdateSession()
    {
        if (conference is null)
            return;
        var session = conference.FindSession(sessionId);
        BindingContext = session;
        Details.IsVisible = session is not null;
        NotFound.IsVisible = session is null && !conference.IsBusy;
        TimeZone.Text = conference.TimeZoneLabel;
    }
}
