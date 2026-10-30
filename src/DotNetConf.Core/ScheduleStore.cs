using System.Text.Json;

namespace DotNetConf.Core;

public interface IScheduleStore
{
    Task<IReadOnlyCollection<string>> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(IReadOnlyCollection<string> sessionIds, CancellationToken cancellationToken = default);
}

public sealed class JsonScheduleStore(string path) : IScheduleStore
{
    public async Task<IReadOnlyCollection<string>> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return Validate(await JsonSerializer.DeserializeAsync(
                stream, ConferenceJsonContext.Default.StringArray, cancellationToken));
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    internal static IReadOnlyCollection<string> Validate(string[]? ids)
    {
        if (ids is null)
            throw new InvalidDataException("The saved schedule is empty or invalid.");
        if (ids.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("The saved schedule contains invalid session IDs.");
        return ids.Distinct(StringComparer.Ordinal).ToArray();
    }

    public async Task WriteAsync(IReadOnlyCollection<string> sessionIds, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".schedule-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, sessionIds.Order(StringComparer.Ordinal).ToArray(),
                    ConferenceJsonContext.Default.StringArray, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}

/// <summary>
/// Saved-session state. Stored IDs are never rewritten on read; session aliases only widen what counts as the same
/// session. The alias map is alias -> canonical, one hop: keys and values are non-blank, a key never maps to itself,
/// and no value is also a key. Equivalence group = a canonical ID plus every alias that maps to it. Saving writes the
/// canonical ID; removing deletes every stored member of the group. IDs unknown to the map are their own group.
/// </summary>
public sealed class ScheduleService(IScheduleStore store)
{
    private sealed record State(HashSet<string> Ids, Dictionary<string, string> Aliases)
    {
        public string Canonical(string id) => Aliases.GetValueOrDefault(id) ?? id;
        public HashSet<string> Groups { get; } = Ids.Select(id => Aliases.GetValueOrDefault(id) ?? id)
            .ToHashSet(StringComparer.Ordinal);
    }

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object stateLock = new();
    private State state = new([], new(StringComparer.Ordinal));
    private bool loaded;

    public bool IsSaved(string sessionId)
    {
        var current = Volatile.Read(ref state);
        return current.Groups.Contains(current.Canonical(sessionId));
    }

    /// <summary>Number of distinct logical sessions, including IDs the current agenda does not know.</summary>
    public int SavedCount => Volatile.Read(ref state).Groups.Count;

    /// <summary>Replaces the alias map atomically; throws ArgumentException and keeps the old map if it is invalid.</summary>
    public void SetAliases(IReadOnlyDictionary<string, string> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (alias, canonical) in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(canonical))
                throw new ArgumentException("Session aliases require non-blank IDs.", nameof(aliases));
            if (string.Equals(alias, canonical, StringComparison.Ordinal))
                throw new ArgumentException($"Session alias '{alias}' maps to itself.", nameof(aliases));
            copy[alias] = canonical;
        }
        if (copy.Values.Any(copy.ContainsKey))
            throw new ArgumentException("Session aliases must map directly to a canonical ID, not to another alias.", nameof(aliases));
        lock (stateLock)
        {
            // Swap under a plain lock so a concurrent toggle publishes either the old or the new map, never a mix.
            var current = state;
            Volatile.Write(ref state, new State(current.Ids, copy));
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (loaded)
                return;
            var ids = new HashSet<string>(await store.ReadAsync(cancellationToken), StringComparer.Ordinal);
            lock (stateLock) { Volatile.Write(ref state, new State(ids, state.Aliases)); }
            loaded = true;
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<bool> ToggleAsync(string sessionId, CancellationToken cancellationToken = default) =>
        UpdateAsync(sessionId, null, cancellationToken);

    /// <summary>Idempotently sets the saved state of the session's logical group against the latest stored schedule and returns the actual state.</summary>
    public Task<bool> SetSavedAsync(string sessionId, bool desiredSaved, CancellationToken cancellationToken = default) =>
        UpdateAsync(sessionId, desiredSaved, cancellationToken);

    async Task<bool> UpdateAsync(string sessionId, bool? desired, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        await InitializeAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Another browser tab may have changed the schedule since this one loaded, so start from what's stored.
            var next = new HashSet<string>(await store.ReadAsync(cancellationToken), StringComparer.Ordinal);
            var aliases = Volatile.Read(ref state).Aliases;
            var canonical = aliases.GetValueOrDefault(sessionId) ?? sessionId;
            var group = next.Where(id => (aliases.GetValueOrDefault(id) ?? id) == canonical).ToArray();
            var added = desired ?? group.Length == 0;
            if (added)
            {
                if (group.Length == 0)
                    next.Add(canonical);
            }
            else
                next.ExceptWith(group);
            if (group.Length == 0 || !added)
                await store.WriteAsync(next, cancellationToken);
            lock (stateLock) { Volatile.Write(ref state, new State(next, state.Aliases)); }
            return added;
        }
        finally
        {
            gate.Release();
        }
    }
}
