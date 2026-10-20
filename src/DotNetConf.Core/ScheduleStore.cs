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

public sealed class ScheduleService(IScheduleStore store)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private HashSet<string> savedIds = new(StringComparer.Ordinal);
    private bool loaded;

    public bool IsSaved(string sessionId) => savedIds.Contains(sessionId);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (loaded)
                return;
            savedIds = new(await store.ReadAsync(cancellationToken), StringComparer.Ordinal);
            loaded = true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> ToggleAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        await InitializeAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Another browser tab may have changed the schedule since this one loaded, so start from what's stored.
            var next = new HashSet<string>(await store.ReadAsync(cancellationToken), StringComparer.Ordinal);
            var added = !next.Contains(sessionId);
            if (added)
                next.Add(sessionId);
            else
                next.Remove(sessionId);
            await store.WriteAsync(next, cancellationToken);
            savedIds = next;
            return added;
        }
        finally
        {
            gate.Release();
        }
    }
}
