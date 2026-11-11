using DotNetConf.App.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace DotNetConf.Core.Tests;

public sealed class ReminderLifecycleTests
{
    private const string Key = "session-reminders-enabled-v1";

    [Fact]
    public async Task DeniedPermissionDoesNotScheduleOrRemoveLikes()
    {
        var (service, notifications, _, store) = Create();
        notifications.RequestNotificationPermission(Arg.Any<NotificationPermission>()).Returns(false);
        await service.EnableAsync();
        Assert.False(service.Enabled);
        Assert.Contains("aren't allowed", service.Status);
        Assert.Equal(ReminderState.PermissionBlocked, service.State);
        Assert.True(service.PermissionBlocked);
        Assert.False(service.CanEnable);
        await notifications.DidNotReceive().Show(Arg.Any<NotificationRequest>());
        Assert.Equal(["future"], await store.ReadAsync());
    }

    [Fact]
    public async Task ReconciliationCancelsOnlyObsoleteOwnedRequests()
    {
        var (service, notifications, preferences, _) = Create();
        preferences.Set(Key, true);
        notifications.GetPendingNotificationList().Returns([
            new() { NotificationId = 42, ReturningData = "session-reminder:old" },
            new() { NotificationId = 99, ReturningData = "another-feature" }
        ]);
        Assert.True(await service.RefreshAsync());
        notifications.Received(1).Cancel(Arg.Is<int[]>(ids => ids.SequenceEqual(new[] { 42 })));
        notifications.DidNotReceive().Cancel(Arg.Is<int[]>(ids => ids.Contains(99)));
        await notifications.Received(1).Show(Arg.Is<NotificationRequest>(request =>
            request.ReturningData == "session-reminder:future" && request.Title == "Demo session reminder"));
        Assert.Contains("1 saved session", service.Status);
        Assert.Equal(ReminderState.Enabled, service.State);
        Assert.False(service.CanEnable);
        Assert.False(service.CanRetry);
    }

    [Fact]
    public async Task FailedSchedulingReleasesGateAndCanBeRetried()
    {
        var (service, notifications, preferences, _) = Create();
        preferences.Set(Key, true);
        notifications.Show(Arg.Any<NotificationRequest>()).Returns(false, true);
        Assert.False(await service.RefreshAsync());
        Assert.Contains("Couldn't update", service.Status);
        Assert.Equal(ReminderState.Error, service.State);
        Assert.True(service.CanRetry);
        Assert.False(service.RetryEnabling);
        Assert.True(await service.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Contains("1 saved session", service.Status);
    }

    [Fact]
    public async Task NotificationTimeoutIsVisibleAndCanBeRetried()
    {
        var (service, notifications, _, store) = Create();
        notifications.GetPendingNotificationList().ThrowsAsync(new TimeoutException("Synthetic notification timeout"));

        Assert.False(await service.RefreshAsync());
        Assert.Contains("Couldn't update", service.Status);
        Assert.Equal(["future"], await store.ReadAsync());

        notifications.GetPendingNotificationList().Returns([]);
        Assert.True(await service.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("Session reminders are off.", service.Status);
    }

    [Fact]
    public async Task FailedCancellationIsVisibleAndRetryDoesNotTouchUnrelatedRequests()
    {
        var (service, notifications, _, store) = Create();
        notifications.GetPendingNotificationList().Returns([
            new() { NotificationId = 42, ReturningData = "session-reminder:old" },
            new() { NotificationId = 99, ReturningData = "another-feature" }
        ]);
        notifications.Cancel(Arg.Any<int[]>()).Returns(false, true);
        await service.DisableAsync();
        Assert.Contains("Couldn't update", service.Status);
        Assert.True(await service.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("Session reminders are off.", service.Status);
        Assert.Equal(["future"], await store.ReadAsync());
        notifications.DidNotReceive().Cancel(Arg.Is<int[]>(ids => ids.Contains(99)));
        await notifications.DidNotReceive().Show(Arg.Any<NotificationRequest>());
    }

    [Fact]
    public async Task RevokedPermissionRemovesOwnedRemindersAndPreservesSavedSessions()
    {
        var (service, notifications, preferences, store) = Create();
        preferences.Set(Key, true);
        notifications.AreNotificationsEnabled(Arg.Any<NotificationPermission>()).Returns(false);
        notifications.GetPendingNotificationList().Returns([
            new() { NotificationId = 42, ReturningData = "session-reminder:future" }
        ]);
        Assert.False(await service.RefreshAsync());
        Assert.True(service.Enabled);
        Assert.Contains("blocked", service.Status);
        Assert.Equal(ReminderState.PermissionBlocked, service.State);
        notifications.Received(1).Cancel(Arg.Is<int[]>(ids => ids.SequenceEqual(new[] { 42 })));
        Assert.Equal(["future"], await store.ReadAsync());
        await notifications.DidNotReceive().Show(Arg.Any<NotificationRequest>());
    }

    [Fact]
    public async Task TurningOffBlockedRemindersKeepsThemOffAndHidesPermissionRecovery()
    {
        var (service, notifications, preferences, _) = Create();
        preferences.Set(Key, true);
        notifications.AreNotificationsEnabled(Arg.Any<NotificationPermission>()).Returns(false);
        Assert.False(await service.RefreshAsync());

        await service.DisableAsync();

        Assert.False(service.Enabled);
        Assert.Equal(ReminderState.Off, service.State);
        Assert.True(service.CanEnable);
        Assert.False(service.PermissionBlocked);
    }

    [Fact]
    public async Task EnablingFailureAndReconciliationFailureHaveDifferentRetryIntents()
    {
        var (service, notifications, _, _) = Create();
        notifications.RequestNotificationPermission(Arg.Any<NotificationPermission>())
            .ThrowsAsync(new InvalidOperationException("Synthetic permission failure"));
        await service.EnableAsync();
        Assert.Equal(ReminderState.Error, service.State);
        Assert.True(service.RetryEnabling);

        notifications.GetPendingNotificationList().ThrowsAsync(new IOException("Synthetic cancellation failure"));
        await service.DisableAsync();
        Assert.False(service.Enabled);
        Assert.Equal(ReminderState.Error, service.State);
        Assert.False(service.RetryEnabling);
        Assert.True(service.CanRetry);
    }

    [Fact]
    public async Task ACancelledSharedAgendaRefreshPreservesRemindersAndCanBeRetried()
    {
        var repository = Substitute.For<IConferenceRepository>();
        var empty = new ConferenceSnapshot(new(), null, null, false, []);
        repository.LoadAsync(Arg.Any<CancellationToken>()).Returns(empty);
        using var cancelledRefresh = new CancellationTokenSource();
        cancelledRefresh.Cancel();
        repository.RefreshAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromCanceled<ConferenceSnapshot>(cancelledRefresh.Token), Task.FromResult(empty));
        var (service, notifications, preferences, store) = Create(new ConferenceAgendaDataSource(repository));
        preferences.Set(Key, true);
        notifications.GetPendingNotificationList().Returns([
            new() { NotificationId = 42, ReturningData = "session-reminder:old" },
            new() { NotificationId = 99, ReturningData = "another-feature" }
        ]);

        Assert.False(await service.RefreshAsync());
        Assert.Contains("Couldn't update", service.Status);
        notifications.DidNotReceive().Cancel(Arg.Any<int[]>());
        Assert.Equal(["future"], await store.ReadAsync());
        Assert.True(await service.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        notifications.DidNotReceive().Cancel(Arg.Is<int[]>(ids => ids.Contains(99)));
    }

    private static (SessionReminderService Service, INotificationService Notifications,
        IPreferences Preferences, IScheduleStore Store) Create(IConferenceDataSource? source = null)
    {
        var preferences = Substitute.For<IPreferences>();
        var enabled = false;
        preferences.Get(Key, false).Returns(_ => enabled);
        preferences.When(p => p.Set(Key, Arg.Any<bool>())).Do(call => enabled = call.Arg<bool>());
        var notifications = Substitute.For<INotificationService>();
        notifications.GetPendingNotificationList().Returns([]);
        notifications.AreNotificationsEnabled(Arg.Any<NotificationPermission>()).Returns(true);
        notifications.Show(Arg.Any<NotificationRequest>()).Returns(true);
        notifications.Cancel(Arg.Any<int[]>()).Returns(true);
        var data = Substitute.For<IConferenceDataSource>();
        data.LoadAsync(Arg.Any<CancellationToken>()).Returns(new ConferenceDocument
        {
            IsMock = true,
            Sessions = [new() { Id = "future", Title = "Future session",
                StartsAt = DateTimeOffset.UtcNow.AddDays(1), EndsAt = DateTimeOffset.UtcNow.AddDays(1).AddHours(1) }]
        });
        var store = Substitute.For<IScheduleStore>();
        store.ReadAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyCollection<string>>(["future"]));
        return (new(source ?? data, new ScheduleService(store), preferences, notifications,
            NullLogger<SessionReminderService>.Instance), notifications, preferences, store);
    }
}
