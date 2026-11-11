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
        QuickActionsCommand = new AsyncRelayCommand(() => owner.ShowSessionActionsAsync(this));
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
        + (IsLive ? ". Live now." : "")
        + (HasRecording ? ". Video available." : "")
        + (IsWatched ? " Marked as watched." : "")
        + (HasConflict ? ". Overlaps another saved session." : ".");
    public IReadOnlyList<SpeakerViewModel> Speakers { get; private set; }
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand ToggleSaveCommand { get; }
    public IAsyncRelayCommand WatchRecordingCommand { get; }
    public IAsyncRelayCommand ToggleWatchedCommand { get; }
    public IAsyncRelayCommand ShareCommand { get; }
    public IAsyncRelayCommand QuickActionsCommand { get; }
    public string QuickActionsDescription => $"More actions for {Title}";
    internal IReadOnlyList<(string Label, IAsyncRelayCommand Command)> GetQuickActions()
    {
        List<(string Label, IAsyncRelayCommand Command)> actions = [];
        if (ToggleSaveCommand.CanExecute(null))
            actions.Add((SaveLabel, ToggleSaveCommand));
        if (ToggleWatchedCommand.CanExecute(null))
            actions.Add((WatchedActionLabel, ToggleWatchedCommand));
        if (ShareCommand.CanExecute(null))
            actions.Add((ShareLabel, ShareCommand));
        return actions;
    }
    public string ShareLabel => owner.ShareSessionLabel;
    public string ShareHint => owner.ShareSessionHint;
    public bool CanShare => !IsWithdrawn;
    public bool HasRecording => !IsWithdrawn && Model.RecordingUrl.Length > 0;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigationDescription))]
    public partial bool IsLive { get; private set; }

    public void RefreshLiveStatus(DateTimeOffset instant) =>
        IsLive = !IsWithdrawn && Agenda.IsInProgress(Model, instant);

    public bool HasWatchActions => !IsWithdrawn || IsWatched;
    public bool HasViewingGuidance => !IsWithdrawn && !HasRecording;
    public bool HasVideoStatus => HasRecording || IsWatched;
    public string VideoStatusLabel => IsWatched ? "Watched" : "Video available";
    public string WatchedActionLabel => IsWatched ? "Mark as not watched" : "Mark as watched";
    public string WatchedSwipeLabel => IsWatched ? "Unwatch" : "Watched";
    public string WatchedButtonLabel => IsWatched ? "Watched" : "Mark as watched";
    public string WatchedDescription => $"{(IsWatched ? "Watched" : "Not watched")}. {WatchedActionLabel}";
    public string WatchedGlyph => IsWatched ? FluentIconsFilled.CheckmarkCircle24 : FluentIconsRegular.Circle24;
    public string WatchedFontFamily => IsWatched ? FluentIconsFilled.FontFamily : FluentIconsRegular.FontFamily;
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
        RefreshLiveStatus(DateTimeOffset.UtcNow);
        OnPropertyChanged(nameof(HasRecording));
        OnPropertyChanged(nameof(HasWatchActions));
        OnPropertyChanged(nameof(HasViewingGuidance));
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
    [NotifyPropertyChangedFor(nameof(SaveSwipeInstruction))]
    [NotifyPropertyChangedFor(nameof(SaveSwipeReleaseInstruction))]
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
    [NotifyPropertyChangedFor(nameof(WatchedSwipeLabel))]
    [NotifyPropertyChangedFor(nameof(WatchedButtonLabel))]
    [NotifyPropertyChangedFor(nameof(WatchedDescription))]
    [NotifyPropertyChangedFor(nameof(WatchedGlyph))]
    [NotifyPropertyChangedFor(nameof(WatchedFontFamily))]
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
    public string SaveSwipeInstruction => IsSaved ? "Swipe to remove" : "Swipe to save";
    public string SaveSwipeReleaseInstruction => IsSaved ? "Release to remove" : "Release to save";
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
