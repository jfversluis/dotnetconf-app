using CommunityToolkit.Mvvm.ComponentModel;
using DotNetConf.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Storage;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using Plugin.LocalNotification.Core.Models.AppleOption;

namespace DotNetConf.App.Services;

public interface ISessionReminderUpdates
{
    Task<bool> RefreshAsync();
}

public partial class SessionReminderService(
    [FromKeyedServices("agenda")] IConferenceDataSource dataSource, ScheduleService schedule, IPreferences preferences,
    INotificationService notifications, ILogger<SessionReminderService> logger) : ObservableObject, ISessionReminderUpdates
{
    public const string ChannelId = "session-reminders";
    private const string PreferenceKey = "session-reminders-enabled-v1";
    private const string Prefix = "session-reminder:";
    private readonly SemaphoreSlim gate = new(1, 1);
    public bool Enabled => preferences.Get(PreferenceKey, false);
    [ObservableProperty] public partial string Status { get; set; } = "Session reminders are optional.";

    public async Task EnableAsync()
    {
        try
        {
            var allowed = await notifications.RequestNotificationPermission(new NotificationPermission
            {
                Apple = new AppleNotificationPermission
                {
                    NotificationAuthorization = AppleAuthorizationOptions.Alert | AppleAuthorizationOptions.Sound
                }
            });
            preferences.Set(PreferenceKey, allowed);
            OnPropertyChanged(nameof(Enabled));
            if (!allowed)
            {
                Status = "Notifications aren't allowed. Browse normally, or enable them in device settings.";
                return;
            }
            await RefreshAsync();
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            logger.LogError(exception, "Could not enable session reminders.");
            Status = "Couldn't enable reminders. Try again in Settings; browsing and saved sessions still work.";
        }
    }

    public async Task DisableAsync()
    {
        preferences.Set(PreferenceKey, false);
        OnPropertyChanged(nameof(Enabled));
        await RefreshAsync();
    }

    public async Task<bool> RefreshAsync()
    {
        await gate.WaitAsync();
        try
        {
            var pending = await notifications.GetPendingNotificationList();
            var owned = pending.Where(request => request.ReturningData?.StartsWith(Prefix, StringComparison.Ordinal) == true).ToArray();
            var allowed = Enabled && await notifications.AreNotificationsEnabled();
            SessionReminder[] planned = [];
            ConferenceDocument? agenda = null;
            if (allowed)
            {
                agenda = await dataSource.LoadAsync();
                await schedule.InitializeAsync();
                TimeZoneInfo.ClearCachedData();
                planned = SessionReminders.Plan(agenda, schedule.IsSaved, DateTimeOffset.UtcNow);
            }
            foreach (var request in owned.Where(request => !planned.Any(reminder => reminder.NotificationId == request.NotificationId)))
            {
                if (!notifications.Cancel(request.NotificationId))
                    throw new InvalidOperationException("A previous session reminder could not be cancelled.");
            }
            foreach (var reminder in planned)
            {
                if (!await notifications.Show(new NotificationRequest
                {
                    NotificationId = reminder.NotificationId,
                    Title = agenda!.IsMock ? "Demo session reminder" : "Your saved session",
                    Description = reminder.Title,
                    ReturningData = Prefix + reminder.SessionId,
                    Android = new Plugin.LocalNotification.Core.Models.AndroidOption.AndroidOptions { ChannelId = ChannelId },
                    Schedule = new NotificationRequestSchedule
                    {
                        NotifyTime = reminder.StartsAt.LocalDateTime,
                        Android = new Plugin.LocalNotification.Core.Models.AndroidOption.AndroidScheduleOptions
                        {
                            AllowedDelay = reminder.EndsAt - reminder.StartsAt
                        }
                    }
                }))
                    throw new InvalidOperationException("The device could not schedule a session reminder.");
            }
            Status = !Enabled ? "Session reminders are off."
                : !allowed ? "Notifications are blocked in device settings. Your saved sessions are kept."
                : planned.Length == 0 ? "Reminders enabled. Save a future session to be notified when it begins."
                : planned.Length == SessionReminders.MaximumPending
                    ? $"Reminders enabled for your next {SessionReminders.MaximumPending} sessions. Reopen the app to refresh later reminders."
                    : $"Reminders enabled for {planned.Length} saved session{(planned.Length == 1 ? "" : "s")}.";
            return !Enabled || allowed;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            logger.LogError(exception, "Could not synchronize session reminders.");
            Status = "Couldn't update reminders. Your saved sessions are kept. Retry in Settings.";
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or NotSupportedException or ArgumentException or System.Text.Json.JsonException
            or OperationCanceledException or TimeoutException
#if IOS
            or Foundation.NSErrorException
#endif
        ;
}
