using DotNetConf.App.Services;

namespace DotNetConf.App.Tests;

internal sealed class TestReminders : ISessionReminderUpdates
{
    public Task<bool> RefreshAsync() => Task.FromResult(true);
}
