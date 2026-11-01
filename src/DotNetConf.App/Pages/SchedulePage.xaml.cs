using DotNetConf.App.ViewModels;
using DotNetConf.App.Services;
using IosPage = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page;

namespace DotNetConf.App.Pages;

public partial class SchedulePage : AdaptiveConferencePage
{
    private bool timelineSelected = true;

    public SchedulePage(ConferenceViewModel conference, DetailNavigationService navigation) : base(conference)
    {
        InitializeComponent();
        ConfigureDetails(DetailKind.Session, navigation);
        ShowTimeline(true);
    }

    protected override void OnAppearing()
    {
        Conference.ScheduleChanged += OnScheduleChanged;
        base.OnAppearing();
        UpdateBottomInset();
        UpdateHeader();
        UpdatePresentation();
    }

    protected override void OnDisappearing()
    {
        Conference.ScheduleChanged -= OnScheduleChanged;
        base.OnDisappearing();
    }

    private void OnScheduleChanged(object? sender, EventArgs e) => UpdatePresentation();

    protected override DetailLayout AdaptDetailLayout(DetailLayout layout, double width, double textScale)
    {
        if (OperatingSystem.IsIOS())
            width -= IosPage.GetSafeAreaInsets(this).HorizontalThickness;
        return ScheduleLayout.ForDetails(layout, width, textScale);
    }

    protected override string DetailSelectionHint => "Select a session from your schedule to explore the details here.";

    protected override void OnListLayoutChanged(bool scrollHeader, bool stacked) => UpdateHeader();

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateHeader();
    }

    private void UpdateHeader()
    {
        if (Height > 0 && TextScale is { } scale)
        {
            ScheduleHeading.IsVisible = ScheduleLayout.ShouldShowHeading(Height, scale);
            Planner.TextScale = scale;
        }
    }

    // On iOS the timeline runs under the translucent tab bar; the page's safe area says how far.
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == IosPage.SafeAreaInsetsProperty.PropertyName && Planner is not null)
            UpdateBottomInset();
    }

    private void UpdateBottomInset() =>
        Planner.BottomInset = OperatingSystem.IsIOS() ? IosPage.GetSafeAreaInsets(this).Bottom : 0;

    private void OnTimelineClicked(object? sender, EventArgs e) => ShowTimeline(true);

    private void OnListClicked(object? sender, EventArgs e) => ShowTimeline(false);

    private void ShowTimeline(bool timeline)
    {
        timelineSelected = timeline;
        UpdatePresentation();
        Select(TimelineButton, timeline);
        Select(ListButton, !timeline);
    }

    private void UpdatePresentation()
    {
        var showPlanner = timelineSelected && Conference.SavedTimelineSessions.Count > 0;
        Planner.IsVisible = showPlanner;
        SavedList.IsVisible = !showPlanner;
    }

    private static void Select(Button button, bool selected)
    {
        var resources = Application.Current!.Resources;
        if (selected)
        {
            button.SetAppThemeColor(BackgroundColorProperty, (Color)resources["Purple"], (Color)resources["Lavender"]);
            button.SetAppThemeColor(Button.TextColorProperty, (Color)resources["White"], (Color)resources["Midnight"]);
        }
        else
        {
            button.SetAppThemeColor(BackgroundColorProperty, Colors.Transparent, Colors.Transparent);
            button.SetAppThemeColor(Button.TextColorProperty, (Color)resources["Purple"], (Color)resources["Lavender"]);
        }
        SemanticProperties.SetDescription(button, $"{button.Text}{(selected ? ", selected" : "")}");
    }
}
