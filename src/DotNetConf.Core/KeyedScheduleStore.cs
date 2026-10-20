namespace DotNetConf.Core;

/// <summary>
/// Stores each saved session under its own key, such as a local storage item. A write adds and removes only the
/// ids that differ from what's stored now, so tabs saving different sessions at once don't overwrite each other.
/// </summary>
public sealed class KeyedScheduleStore(Func<IEnumerable<string>> readIds, Action<string> save, Action<string> remove)
    : IScheduleStore
{
    public Task<IReadOnlyCollection<string>> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(JsonScheduleStore.Validate(readIds().ToArray()));

    public Task WriteAsync(IReadOnlyCollection<string> sessionIds, CancellationToken cancellationToken = default)
    {
        var stored = readIds().ToHashSet(StringComparer.Ordinal);
        var wanted = sessionIds.ToHashSet(StringComparer.Ordinal);
        foreach (var id in wanted.Where(id => !stored.Contains(id)))
            save(id);
        foreach (var id in stored.Where(id => !wanted.Contains(id)))
            remove(id);
        return Task.CompletedTask;
    }
}
