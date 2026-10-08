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
        CommunityTab.ContentTemplate = new DataTemplate(() => services.GetRequiredService<CommunityPage>());
        Routing.RegisterRoute("session-detail", typeof(SessionDetailPage));
        Routing.RegisterRoute("speaker-detail", typeof(SpeakerDetailPage));
        Routing.RegisterRoute("about", typeof(AboutPage));
        Routing.RegisterRoute("settings", typeof(SettingsPage));
        Routing.RegisterRoute("third-party", typeof(ThirdPartyPage));
        Routing.RegisterRoute("license", typeof(LicensePage));
        Routing.RegisterRoute("community-map", typeof(CommunityEventsPage));
        Routing.RegisterRoute("community-event", typeof(CommunityEventDetailPage));
        conference.ScheduleChanged += (_, _) =>
            ScheduleTab.BadgeText = conference.SavedCount == 0 ? null : conference.SavedCount.ToString();
    }

    private void RefreshTabIcons()
    {
        var app = Application.Current!;
        var color = (Color)app.Resources[app.RequestedTheme == AppTheme.Dark ? "Lavender" : "Purple"];
        foreach (var tab in new[] { HomeTab, SessionsTab, SpeakersTab, ScheduleTab, CommunityTab })
        {
            if (tab.Icon is FontImageSource icon)
                tab.Icon = new FontImageSource { Glyph = icon.Glyph, FontFamily = icon.FontFamily, Size = icon.Size, Color = color };
        }
    }
}
