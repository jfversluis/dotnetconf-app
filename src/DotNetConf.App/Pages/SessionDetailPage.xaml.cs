using DotNetConf.App.ViewModels;
using DotNetConf.App.Services;
using System.ComponentModel;

namespace DotNetConf.App.Pages;

public partial class SessionDetailPage : ContentPage, IQueryAttributable
{
    private readonly ConferenceViewModel conference;
    private readonly FoldableViewport viewport;
    private string? sessionId;
    private SessionViewModel? sharedSession;
    internal string? SessionId => sessionId;
    internal double? TextScale => Detail.TextScaleMonitor.TextScale;
    internal DetailLayout? CurrentLayout(double width, double height, double textScale) =>
        viewport.IsCurrent ? viewport.GetLayout(width, height, textScale) : null;

    public SessionDetailPage(ConferenceViewModel conference, DetailNavigationService navigation)
    {
        InitializeComponent();
        Detail.BindingContextChanged += OnDetailBindingContextChanged;
        ToolbarItems.Remove(ShareToolbar);
        this.conference = conference;
        viewport = new FoldableViewport(this, this, navigation.RequestLayoutSync, navigation.FoldableRegionSource);
        Detail.TextScaleMonitor.TextScaleChanged += (_, _) => navigation.RequestLayoutSync();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        sessionId = query.TryGetValue("sessionId", out var captured) && captured is string pathId
            ? pathId
            : query.TryGetValue("id", out var value) && value is string id ? Uri.UnescapeDataString(id) : null;
        Detail.Show(conference, sessionId);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        OnDetailBindingContextChanged(this, EventArgs.Empty);
    }

    protected override void OnDisappearing()
    {
        if (sharedSession is not null)
            sharedSession.PropertyChanged -= OnSessionChanged;
        sharedSession = null;
        ShareToolbar.BindingContext = null;
        ToolbarItems.Remove(ShareToolbar);
        base.OnDisappearing();
    }

    private void OnDetailBindingContextChanged(object? sender, EventArgs e)
    {
        if (sharedSession is not null)
            sharedSession.PropertyChanged -= OnSessionChanged;
        sharedSession = Detail.BindingContext as SessionViewModel;
        if (sharedSession is not null)
            sharedSession.PropertyChanged += OnSessionChanged;
        ShareToolbar.BindingContext = sharedSession;
        UpdateShareToolbar();
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SessionViewModel.CanShare))
            UpdateShareToolbar();
    }

    private void UpdateShareToolbar()
    {
        var show = sharedSession?.CanShare == true;
        if (show && !ToolbarItems.Contains(ShareToolbar))
            ToolbarItems.Add(ShareToolbar);
        else if (!show)
            ToolbarItems.Remove(ShareToolbar);
    }
}
