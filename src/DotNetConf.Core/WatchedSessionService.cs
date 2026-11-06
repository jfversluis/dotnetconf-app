namespace DotNetConf.Core;

/// <summary>Local watched-session IDs, independent of the agenda cache and saved schedule.</summary>
public sealed class WatchedSessionService(IScheduleStore store)
{
    private readonly ScheduleService state = new(store);

    public bool IsWatched(string sessionId) => state.IsSaved(sessionId);

    public Task ReloadAsync(CancellationToken cancellationToken = default) => state.ReloadAsync(cancellationToken);

    public Task<bool> SetWatchedAsync(string sessionId, bool watched, CancellationToken cancellationToken = default) =>
        state.SetSavedAsync(sessionId, watched, cancellationToken);
}
