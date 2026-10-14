using DotNetConf.App.ViewModels;
using IosPage = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page;

namespace DotNetConf.App.Pages;

public partial class SchedulePage : ConferencePage
{
    public SchedulePage(ConferenceViewModel conference) : base(conference)
    {
        InitializeComponent();
        ShowTimeline(true);
    }

    // On iOS the timeline runs under the translucent tab bar; the page's safe area says how far.
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == IosPage.SafeAreaInsetsProperty.PropertyName && Planner is not null)
            Planner.BottomInset = IosPage.GetSafeAreaInsets(this).Bottom;
    }

    private void OnTimelineClicked(object? sender, EventArgs e) => ShowTimeline(true);

    private void OnListClicked(object? sender, EventArgs e) => ShowTimeline(false);

    private void ShowTimeline(bool timeline)
    {
        Planner.IsVisible = timeline;
        SavedList.IsVisible = !timeline;
        Select(TimelineButton, timeline);
        Select(ListButton, !timeline);
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
