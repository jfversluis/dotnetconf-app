namespace DotNetConf.Core.Tests;

public sealed class SessionReminderTests
{
    [Fact]
    public void PlanIncludesOnlyFutureSavedSessionsWithStableIds()
    {
        var now = DateTimeOffset.Parse("2026-10-16T10:00Z");
        var conference = new ConferenceDocument
        {
            Sessions = [
                new() { Id = "past", StartsAt = now.AddMinutes(-1) },
                new() { Id = "now", StartsAt = now },
                new() { Id = "saved", Title = "A future talk", StartsAt = now.AddHours(1), EndsAt = now.AddHours(2) },
                new() { Id = "not-saved", StartsAt = now.AddHours(2) }]
        };
        var reminders = SessionReminders.Plan(conference, id => id != "not-saved", now);
        var reminder = Assert.Single(reminders);
        Assert.Equal("saved", reminder.SessionId);
        Assert.Equal("A future talk", reminder.Title);
        Assert.Equal(now.AddHours(1), reminder.StartsAt);
        Assert.Equal(now.AddHours(2), reminder.EndsAt);
        Assert.Equal(reminder.NotificationId, Assert.Single(SessionReminders.Plan(conference, _ => true, now),
            item => item.SessionId == "saved").NotificationId);
        Assert.Empty(SessionReminders.Plan(conference, _ => false, now));
    }

    [Fact]
    public void PendingBudgetKeepsNearestSixtyInDeterministicOrder()
    {
        var now = DateTimeOffset.Parse("2026-10-16T10:00Z");
        var conference = new ConferenceDocument
        {
            Sessions = Enumerable.Range(1, 100).Reverse().Select(index =>
                new Session { Id = $"session-{index}", StartsAt = now.AddMinutes(index) }).ToList()
        };
        var reminders = SessionReminders.Plan(conference, _ => true, now);
        Assert.Equal(60, reminders.Length);
        Assert.Equal("session-1", reminders[0].SessionId);
        Assert.Equal("session-60", reminders[^1].SessionId);
        Assert.Equal(60, reminders.Select(reminder => reminder.NotificationId).Distinct().Count());
    }
}
