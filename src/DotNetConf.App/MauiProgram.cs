using DotNetConf.App.Pages;
using DotNetConf.App.Planner;
using DotNetConf.App.Services;
using DotNetConf.App.ViewModels;
using DotNetConf.Core;
using IconFont.Maui.FluentIcons;
using DotNetConf.App.Controls;
using CommunityToolkit.Maui;
using Plugin.LocalNotification;
using Syncfusion.Maui.Toolkit.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.AI;
#if IOS || ANDROID
using Microsoft.Maui.Essentials.AI;
#endif
#if ANDROID
using Microsoft.Maui.Platform;
using Microsoft.Maui.LifecycleEvents;
using DotNetConf.App.Platforms.Android;
#else
using Microsoft.Maui.Foldable;
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
#if !ANDROID
            .UseFoldable()
#endif
            // Configured first so the Avalonia host can replace the toolkit's handlers.
            .ConfigureSyncfusionToolkit()
#if AVALONIA
            .UseAvaloniaHost()
#else
            // The schedule planner is an Avalonia control inside the native app.
            .UseAvaloniaEmbedding<PlannerApplication>()
            .UseMauiCommunityToolkit()
            .UseMauiMaps()
            .ConfigureMauiHandlers(handlers => handlers.AddHandler<AgendaPicker, AgendaPickerHandler>())
#endif
            .ConfigureMauiHandlers(handlers => handlers.AddHandler<DotNetConf.App.Planner.SchedulePlanner, SchedulePlannerHandler>())
            .UseFluentIconsRegular()
            .UseFluentIconsFilled()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("SpaceGrotesk-Bold.ttf", "SpaceGroteskBold");
            });

#if ANDROID
        builder.Services.AddSingleton<AndroidFoldableRegionSource>();
        builder.Services.AddSingleton<IFoldableRegionSource>(services =>
            services.GetRequiredService<AndroidFoldableRegionSource>());
        builder.ConfigureLifecycleEvents(events => events.AddAndroid(android => android
            .OnStart(activity => GetFoldableRegionSource().Start(activity))
            .OnStop(activity => GetFoldableRegionSource().Stop(activity))));
        DotNetConf.App.Platforms.Android.ShellToolbarAppearance.Register();
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<ShellItem, InstantTabsHandler>();
            handlers.AddHandler<SearchResultLayer, SearchResultLayerHandler>();
        });
#endif
        builder.UseLocalNotification(options => options.AddAndroid(android => android.AddChannel(
            new Plugin.LocalNotification.Core.Models.AndroidOption.AndroidNotificationChannelRequest
            {
                Id = SessionReminderService.ChannelId,
                Name = "Session reminders",
                Description = "Reminders when sessions in My Schedule begin"
            })));
        builder.Services.AddSingleton<SessionReminderService>();
        builder.Services.AddSingleton<ISessionReminderUpdates>(services => services.GetRequiredService<SessionReminderService>());
        builder.Services.AddTransient<OnboardingPage>();
        builder.Services.AddSingleton<IConferenceDataSource, ConferenceMetadataDataSource>();
        builder.Services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
        builder.Services.AddSingleton<IConferenceRepository>(services => new ConferenceRepository(
            services.GetRequiredService<IConferenceDataSource>(), services.GetRequiredService<HttpClient>(),
            services.GetService<IConferenceCacheStore>() ??
                new FileConferenceCacheStore(Path.Combine(FileSystem.AppDataDirectory, "agenda-cache.json"))));
        builder.Services.AddKeyedSingleton<IConferenceDataSource, ConferenceAgendaDataSource>("agenda");
#if !AVALONIA
        builder.Services.AddSingleton<IConnectivity>(_ => Connectivity.Current);
#endif
        builder.Services.TryAddSingleton<IScheduleStore>(_ =>
            new JsonScheduleStore(Path.Combine(FileSystem.AppDataDirectory, "schedule.json")));
        builder.Services.AddSingleton<ScheduleService>();
        builder.Services.AddSingleton<DetailNavigationService>();
        builder.Services.TryAddSingleton(Browser.Default);
        builder.Services.AddSingleton<ConferenceViewModel>();
        builder.Services.AddSingleton<IAskApplication>(services => services.GetRequiredService<ConferenceViewModel>());
        builder.Services.AddTransient<IOnDeviceAsk, OnDeviceAsk>();
        builder.Services.AddScoped<AskTools>();
        builder.Services.AddTransient<AskViewModel>();
#if IOS
        if (OperatingSystem.IsIOSVersionAtLeast(26))
        {
#pragma warning disable MAUIAI0001
            builder.Services.AddTransient<IChatClient>(services =>
            {
                if (!OperatingSystem.IsIOSVersionAtLeast(26))
                    throw new PlatformNotSupportedException("Apple Intelligence requires iOS 26 or later.");
                return new AppleIntelligenceChatClient(services.GetRequiredService<ILoggerFactory>(), services);
            });
#pragma warning restore MAUIAI0001
        }
#endif
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
#pragma warning disable MAUIAI0001
            builder.Services.AddTransient<IChatClient>(_ => new GeminiNanoChatClient());
#pragma warning restore MAUIAI0001
        }
#endif
        builder.Services.AddSingleton<IPlannerModelSelection, PlannerModelSelection>();
        builder.Services.AddSingleton<IPlannerModelStatus, OnDeviceModelStatus>();
        // Inactive cloud-provider example; setup and privacy requirements: docs/AI-PLANNER-DEMO.md.
        // builder.Services.AddTransient<IChatClient>(_ =>
        //     new OpenAI.OpenAIClient(apiKey).GetChatClient(modelId).AsIChatClient());
        // builder.Services.AddTransient<IPlannerModelStatus, CloudPlannerModelStatus>();
        builder.Services.AddSingleton<OnDevicePlanner>();
        builder.Services.AddSingleton<IOnDevicePlanner>(services => services.GetRequiredService<OnDevicePlanner>());
        builder.Services.AddSingleton<IPlannerScheduleUpdates>(services => services.GetRequiredService<ConferenceViewModel>());
        builder.Services.AddScoped<PlannerTools>();
        builder.Services.AddTransient<PlannerViewModel>();
        builder.Services.AddTransient<PlannerPage>();
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

#if ANDROID
    private static AndroidFoldableRegionSource GetFoldableRegionSource() =>
        IPlatformApplication.Current?.Services.GetRequiredService<AndroidFoldableRegionSource>()
        ?? throw new InvalidOperationException("The Android application is unavailable for fold monitoring.");
#endif
}
