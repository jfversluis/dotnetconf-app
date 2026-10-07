using DotNetConf.App.Pages;
using DotNetConf.App.ViewModels;

namespace DotNetConf.App;

public partial class AppShell : Shell
{
    public AppShell(IServiceProvider services, ConferenceViewModel conference)
    {
        InitializeComponent();
#if ANDROID
        this.SetAppThemeColor(TabBarBackgroundColorProperty,
            (Color)Application.Current!.Resources["LightSurface"],
            (Color)Application.Current.Resources["DarkSurface"]);
        ScheduleTab.Title = "Schedule";
#endif
        RefreshTabIcons();
        Application.Current!.RequestedThemeChanged += (_, _) => RefreshTabIcons();
        HomeTab.ContentTemplate = new DataTemplate(() => services.GetRequiredService<HomePage>());
        SessionsTab.ContentTemplate = new DataTemplate(() => services.GetRequiredService<SessionsPage>());
        SpeakersTab.ContentTemplate = new DataTemplate(() => services.GetRequiredService<SpeakersPage>());
        ScheduleTab.ContentTemplate = new DataTemplate(() => services.GetRequiredService<SchedulePage>());
        AboutTab.ContentTemplate = new DataTemplate(() => services.GetRequiredService<AboutPage>());
        Routing.RegisterRoute("session-detail", typeof(SessionDetailPage));
        Routing.RegisterRoute("speaker-detail", typeof(SpeakerDetailPage));
        conference.ScheduleChanged += (_, _) =>
            ScheduleTab.BadgeText = conference.SavedCount == 0 ? null : conference.SavedCount.ToString();
    }

    private void RefreshTabIcons()
    {
        var app = Application.Current!;
        var color = (Color)app.Resources[app.RequestedTheme == AppTheme.Dark ? "Lavender" : "Purple"];
        foreach (var tab in new[] { HomeTab, SessionsTab, SpeakersTab, ScheduleTab, AboutTab })
        {
            if (tab.Icon is FontImageSource icon)
                tab.Icon = new FontImageSource { Glyph = icon.Glyph, FontFamily = icon.FontFamily, Size = icon.Size, Color = color };
        }
    }
}
