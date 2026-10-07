using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetConf.Core;

namespace DotNetConf.App.ViewModels;

public partial class SessionViewModel : ObservableObject
{
    public SessionViewModel(Session model, IEnumerable<SpeakerViewModel> speakers, bool isSaved, ConferenceViewModel owner)
    {
        Model = model;
        Speakers = speakers.Where(speaker => model.SpeakerIds.Contains(speaker.Id)).ToArray();
        IsSaved = isSaved;
        ToggleSaveCommand = new AsyncRelayCommand(() => owner.ToggleSavedAsync(this));
        OpenCommand = new AsyncRelayCommand(() => owner.OpenSessionAsync(Id));
        RefreshTime(TimeZoneInfo.Local);
    }

    public Session Model { get; }
    public string Id => Model.Id;
    public string AutomationId => $"session-{Id}";
    public string SaveAutomationId => $"save-{Id}";
    public string Title => Model.Title;
    public string Description => string.IsNullOrWhiteSpace(Model.Description) ? "Details will be added when the agenda is announced." : Model.Description;
    public string SpeakerNames => Speakers.Count == 0 ? "Speaker to be announced" : string.Join(", ", Speakers.Select(speaker => speaker.Name));
    public string TopicLabel => string.Join(" / ", Model.Topics);
    public string NavigationDescription => $"{Title}. {DateLabel}, {TimeLabel}. {SpeakerNames}. {TopicLabel}"
        + (HasConflict ? ". Overlaps another saved session." : ".");
    public IReadOnlyList<SpeakerViewModel> Speakers { get; }
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand ToggleSaveCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveLabel))]
    [NotifyPropertyChangedFor(nameof(SaveHint))]
    public partial bool IsSaved { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigationDescription))]
    public partial bool HasConflict { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigationDescription))]
    public partial string TimeLabel { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigationDescription))]
    public partial string DateLabel { get; set; } = "";
    public string SaveLabel => IsSaved ? "Remove from schedule" : "Save to schedule";
    public string SaveHint => $"{(IsSaved ? "Saved. Remove from My Schedule" : "Not saved. Add to My Schedule")}: {Title}";

    public void RefreshTime(TimeZoneInfo timeZone)
    {
        var start = TimeZoneInfo.ConvertTime(Model.StartsAt, timeZone);
        var end = TimeZoneInfo.ConvertTime(Model.EndsAt, timeZone);
        DateLabel = start.ToString("ddd, MMM d");
        TimeLabel = start.Date == end.Date
            ? $"{start:t} - {end:t}"
            : $"{start:MMM d, t} - {end:MMM d, t}";
    }
}
