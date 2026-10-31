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

/// <summary>Local saved-session IDs, independent of the downloaded agenda.</summary>
public sealed class ScheduleService(IScheduleStore store)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private HashSet<string> savedIds = new(StringComparer.Ordinal);
    private bool loaded;

    public bool IsSaved(string sessionId) => Volatile.Read(ref savedIds).Contains(sessionId);

    /// <summary>Number of distinct saved IDs, including IDs the current agenda does not know.</summary>
    public int SavedCount => Volatile.Read(ref savedIds).Count;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (loaded)
                return;
            var ids = new HashSet<string>(await store.ReadAsync(cancellationToken), StringComparer.Ordinal);
            Volatile.Write(ref savedIds, ids);
            loaded = true;
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<bool> ToggleAsync(string sessionId, CancellationToken cancellationToken = default) =>
        UpdateAsync(sessionId, null, cancellationToken);

    /// <summary>Idempotently updates the latest stored schedule and returns the actual saved state.</summary>
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
            var added = desired ?? !next.Contains(sessionId);
            var changed = added ? next.Add(sessionId) : next.Remove(sessionId);
            if (changed)
                await store.WriteAsync(next, cancellationToken);
            Volatile.Write(ref savedIds, next);
            return added;
        }
        finally
        {
            gate.Release();
        }
    }
}
