using DotNetConf.App.Pages;
using DotNetConf.App.Services;
using DotNetConf.App.ViewModels;
using DotNetConf.Core;
using IconFont.Maui.FluentIcons;
using DotNetConf.App.Controls;
using CommunityToolkit.Maui;
using Syncfusion.Maui.Toolkit.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
#if ANDROID
using Microsoft.Maui.Platform;
#endif
using Microsoft.Extensions.Logging;
#if DEBUG
#if !AVALONIA
using Microsoft.Maui.DevFlow.Agent;
#endif
#endif

namespace DotNetConf.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
#if AVALONIA
            .UseAvaloniaHost()
#else
            .UseMauiCommunityToolkit()
            .UseMauiMaps()
            .ConfigureSyncfusionToolkit()
            .ConfigureMauiHandlers(handlers => handlers.AddHandler<AgendaPicker, AgendaPickerHandler>())
#endif
            .UseFluentIconsRegular()
            .UseFluentIconsFilled()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("SpaceGrotesk-Bold.ttf", "SpaceGroteskBold");
            });

#if ANDROID
        DotNetConf.App.Platforms.Android.ShellToolbarAppearance.Register();
        Microsoft.Maui.Handlers.EntryHandler2.Mapper.AppendToMapping(nameof(IEntry.Placeholder), (handler, view) =>
        {
            if (view is not SearchEntry)
                return;

            var layout = handler.PlatformView;
            layout.BoxBackgroundMode = Google.Android.Material.TextField.TextInputLayout.BoxBackgroundNone;
            layout.HintEnabled = false;
            layout.SetPadding(0, 0, 0, 0);
            layout.Background = null;
            layout.SetGravity((layout.Gravity & ~Android.Views.GravityFlags.VerticalGravityMask)
                | Android.Views.GravityFlags.CenterVertical);
            if (layout.EditText is { } input)
            {
                input.SetPadding(0, 0, 0, 0);
                input.Background = null;
                input.Hint = view.Placeholder;
                input.Gravity = (input.Gravity & ~Android.Views.GravityFlags.VerticalGravityMask)
                    | Android.Views.GravityFlags.CenterVertical;
            }
        });
        Microsoft.Maui.Handlers.EntryHandler2.Mapper.AppendToMapping(nameof(IEntry.PlaceholderColor), (handler, view) =>
        {
            if (view is SearchEntry && handler.PlatformView.EditText is { } input && view.PlaceholderColor is { } color)
                input.SetHintTextColor(color.ToPlatform());
        });
#elif IOS
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("AgendaSearch", (handler, view) =>
        {
            if (view is not SearchEntry)
                return;

            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
        });
#endif

        builder.Services.AddSingleton<IConferenceDataSource, ConferenceMetadataDataSource>();
        builder.Services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
        builder.Services.AddKeyedSingleton<HttpClient>("speaker-portraits", (_, _) =>
            OperatingSystem.IsBrowser()
                ? new HttpClient { Timeout = TimeSpan.FromSeconds(20) }
                : new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) });
        builder.Services.AddSingleton<IConferenceRepository>(services => new ConferenceRepository(
            services.GetRequiredService<IConferenceDataSource>(), services.GetRequiredService<HttpClient>(),
            services.GetService<IConferenceCacheStore>() ??
                new FileConferenceCacheStore(Path.Combine(FileSystem.AppDataDirectory, "agenda-cache.json"))));
#if !AVALONIA
        builder.Services.AddSingleton<IConnectivity>(_ => Connectivity.Current);
#endif
        builder.Services.AddSingleton(services => new SpeakerImageCache(
            services.GetRequiredKeyedService<HttpClient>("speaker-portraits"), Path.Combine(FileSystem.CacheDirectory, "speaker-portraits"),
            services.GetRequiredService<ILogger<SpeakerImageCache>>(), services.GetService<ISpeakerImagePersistence>()));
        builder.Services.TryAddSingleton<IScheduleStore>(_ =>
            new JsonScheduleStore(Path.Combine(FileSystem.AppDataDirectory, "schedule.json")));
        builder.Services.AddSingleton<ScheduleService>();
        builder.Services.AddSingleton<DetailNavigationService>();
        builder.Services.TryAddSingleton(Browser.Default);
        builder.Services.AddSingleton<ConferenceViewModel>();
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<SessionsPage>();
        builder.Services.AddTransient<SessionFiltersPage>();
        builder.Services.AddTransient<SpeakersPage>();
        builder.Services.AddTransient<SchedulePage>();
        builder.Services.AddTransient<AboutPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<ThirdPartyPage>();
        builder.Services.AddTransient<LicensePage>();
        builder.Services.AddSingleton<IPreferences>(Preferences.Default);
        builder.Services.AddTransient<CommunityEventsPage>();
        builder.Services.AddTransient<CommunityPage>();
        builder.Services.AddTransient<CommunityEventDetailPage>();
        builder.Services.AddTransient<SessionDetailPage>();
        builder.Services.AddTransient<SpeakerDetailPage>();
#if DEBUG
        builder.Logging.AddDebug();
#if !AVALONIA
        builder.AddMauiDevFlowAgent();
#endif
#endif
        return builder.Build();
    }
}
