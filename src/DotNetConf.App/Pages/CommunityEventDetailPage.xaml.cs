using DotNetConf.App.ViewModels;
using DotNetConf.Core;

namespace DotNetConf.App.Pages;

public partial class CommunityEventDetailPage : ContentPage, IQueryAttributable
{
    private string? eventId;
    public ConferenceViewModel Conference { get; }
    public CommunityEvent? Event { get; private set; }
    public bool HasEvent => Event is not null;
    public bool IsMissing => Event is null && !Conference.IsBusy;

    public CommunityEventDetailPage(ConferenceViewModel conference)
    {
        Conference = conference;
        InitializeComponent();
        BindingContext = this;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        eventId = query.TryGetValue("id", out var value) && value is string id ? Uri.UnescapeDataString(id) : null;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Conference.InitializeAsync();
        Event = Conference.CommunityEvents.FirstOrDefault(e => e.Id == eventId);
        OnPropertyChanged(nameof(Event));
        OnPropertyChanged(nameof(HasEvent));
        OnPropertyChanged(nameof(IsMissing));
    }
}
