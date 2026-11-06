using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetConf.Core;
using IconFont.Maui.FluentIcons;

namespace DotNetConf.App.ViewModels;

public partial class SessionViewModel : ObservableObject
{
    public SessionViewModel(Session model, IEnumerable<SpeakerViewModel> speakers, bool isSaved, ConferenceViewModel owner)
    {
        this.owner = owner;
        Model = model;
        Speakers = speakers.Where(speaker => model.SpeakerIds.Contains(speaker.Id)).ToArray();
        IsSaved = isSaved;
        ToggleSaveCommand = new AsyncRelayCommand(() => owner.ToggleSavedAsync(this), () => owner.IsScheduleAvailable);
        OpenCommand = new AsyncRelayCommand(() => owner.OpenSessionAsync(Id));
        WatchRecordingCommand = new AsyncRelayCommand(() => owner.OpenSessionVideoAsync(Model.RecordingUrl),
            () => HasRecording);
        ToggleWatchedCommand = new AsyncRelayCommand(() => owner.ToggleWatchedAsync(this),
            () => owner.IsWatchedAvailable && HasWatchActions);
        ShareCommand = new AsyncRelayCommand(() => owner.ShareTextAsync(Title, ShareText), () => !IsWithdrawn);
        UpdateResources();
        RefreshTime(TimeZoneInfo.Local);
    }

    public Session Model { get; private set; }
    private readonly ConferenceViewModel owner;
    public string Id => Model.Id;
    public string AutomationId => $"session-{Id}";
    public string SaveAutomationId => $"save-{Id}";
    public string Title => Model.Title;
    public string Description => string.IsNullOrWhiteSpace(Model.Description) ? "Details will be added when the agenda is announced." : Model.Description;
    public string SpeakerNames => Speakers.Count == 0 ? "Speaker to be announced" : string.Join(", ", Speakers.Select(speaker => speaker.Name));
    public string TopicLabel => string.Join(" / ", Model.Topics);
    public string NavigationDescription => $"{Title}. {DateLabel}, {TimeLabel}. {SpeakerNames}. {TopicLabel}"
        + (IsWithdrawn ? ". No longer in the published agenda." : "")
        + (HasRecording ? ". Video available." : "")
        + (IsWatched ? " Marked as watched." : "")
        + (HasConflict ? ". Overlaps another saved session." : ".");
    public IReadOnlyList<SpeakerViewModel> Speakers { get; private set; }
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand ToggleSaveCommand { get; }
    public IAsyncRelayCommand WatchRecordingCommand { get; }
    public IAsyncRelayCommand ToggleWatchedCommand { get; }
    public IAsyncRelayCommand ShareCommand { get; }
    public string ShareLabel => owner.ShareSessionLabel;
    public string ShareHint => owner.ShareSessionHint;
    public bool CanShare => !IsWithdrawn;
    public bool HasRecording => !IsWithdrawn && Model.RecordingUrl.Length > 0;
    public bool HasWatchActions => HasRecording || IsWatched;
    public bool HasVideoStatus => HasRecording || IsWatched;
    public string VideoStatusLabel => IsWatched ? "Watched" : "Video available";
    public string WatchedActionLabel => IsWatched ? "Mark as not watched" : "Mark as watched";
    public IReadOnlyList<ConferenceLinkViewModel> Resources { get; private set; } = [];
    public bool HasResources => Resources.Count > 0 && !IsWithdrawn;
    public string ShareText => $"Check out this .NET Conf session:\n{Title}" +
        (Speakers.Count > 0 ? $"\n{SpeakerNames}" : "") +
        $"\n\n{ConferenceLinks.SessionUrl(Id)}\n{ConferenceLinks.Hashtag}";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigationDescription))]
    [NotifyPropertyChangedFor(nameof(SaveHint))]
    public partial bool IsWithdrawn { get; set; }

    partial void OnIsWithdrawnChanged(bool value)
    {
        OnPropertyChanged(nameof(HasRecording));
        OnPropertyChanged(nameof(HasWatchActions));
        OnPropertyChanged(nameof(HasVideoStatus));
        OnPropertyChanged(nameof(HasResources));
        OnPropertyChanged(nameof(CanShare));
        WatchRecordingCommand.NotifyCanExecuteChanged();
        ToggleWatchedCommand.NotifyCanExecuteChanged();
        ShareCommand.NotifyCanExecuteChanged();
    }

    public void Update(Session model, IEnumerable<SpeakerViewModel> speakers)
    {
        Model = model;
        Speakers = speakers.Where(speaker => model.SpeakerIds.Contains(speaker.Id)).ToArray();
        UpdateResources();
        IsWithdrawn = false;
        WatchRecordingCommand.NotifyCanExecuteChanged();
        ToggleWatchedCommand.NotifyCanExecuteChanged();
        RefreshTime(TimeZoneInfo.Local);
        OnPropertyChanged(string.Empty);
    }

    private void UpdateResources() =>
        Resources = Model.ResourceLinks.Select(link => new ConferenceLinkViewModel(link, owner)).ToArray();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveLabel))]
    [NotifyPropertyChangedFor(nameof(SaveHint))]
    [NotifyPropertyChangedFor(nameof(SaveGlyph))]
    [NotifyPropertyChangedFor(nameof(SaveFontFamily))]
    public partial bool IsSaved { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigationDescription))]
    [NotifyPropertyChangedFor(nameof(HasWatchActions))]
    [NotifyPropertyChangedFor(nameof(HasVideoStatus))]
    [NotifyPropertyChangedFor(nameof(VideoStatusLabel))]
    [NotifyPropertyChangedFor(nameof(WatchedActionLabel))]
    public partial bool IsWatched { get; set; }

    partial void OnIsWatchedChanged(bool value) => ToggleWatchedCommand.NotifyCanExecuteChanged();

    internal void RepublishWatchedState()
    {
        OnPropertyChanged(string.Empty);
        ToggleWatchedCommand.NotifyCanExecuteChanged();
    }

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
    public string SaveHint => $"{(IsSaved ? "Saved. Remove from My Schedule" : "Not saved. Add to My Schedule")}: {Title}"
        + (IsWithdrawn ? ". No longer in the published agenda." : "");
    public string SaveGlyph => IsSaved ? FluentIconsFilled.Heart24 : FluentIconsRegular.Heart24;
    public string SaveFontFamily => IsSaved ? FluentIconsFilled.FontFamily : FluentIconsRegular.FontFamily;

    public void RefreshTime(TimeZoneInfo timeZone, bool republish = false)
    {
        var start = TimeZoneInfo.ConvertTime(Model.StartsAt, timeZone);
        var end = TimeZoneInfo.ConvertTime(Model.EndsAt, timeZone);
        var (startDay, endDay) = (DateOnly.FromDateTime(start.DateTime), DateOnly.FromDateTime(end.DateTime));
        DateLabel = $"{start:ddd}, {Agenda.MonthDay(startDay)}";
        TimeLabel = startDay == endDay
            ? $"{start:t} - {end:t}"
            : $"{Agenda.MonthDay(startDay)}, {start:t} - {Agenda.MonthDay(endDay)}, {end:t}";
        if (republish)
        {
            // A previous notification may have failed after changing the value.
            OnPropertyChanged(nameof(DateLabel));
            OnPropertyChanged(nameof(TimeLabel));
            OnPropertyChanged(nameof(NavigationDescription));
        }
    }
}
