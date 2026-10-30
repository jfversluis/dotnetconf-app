using DotNetConf.App.Services;
using DotNetConf.Core;

namespace DotNetConf.App;

/// <summary>Bounded, versioned storage separate from preferences and saved-session keys.</summary>
internal sealed class BrowserCache(
    Func<string, string?> read,
    Action<string, string> write,
    Action<string> remove,
    Func<string, List<string>> keys) : IConferenceCacheStore, ISpeakerImagePersistence
{
    internal const string AgendaKey = "dotnetconf.cache.agenda.v1";
    internal const string PortraitPrefix = "dotnetconf.cache.portrait.v1.";
    internal const int MaximumAgendaBytes = 1024 * 1024;
    internal const int MaximumPortraitBytes = 256 * 1024;
    internal const int MaximumPortraitCharacters = 700 * 1024;

    public Task<byte[]?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Decode(read(AgendaKey), MaximumAgendaBytes));
    }

    public Task WriteAsync(byte[] content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        write(AgendaKey, Encode(content, MaximumAgendaBytes));
        return Task.CompletedTask;
    }

    public byte[]? Read(string key) => Decode(read(PortraitPrefix + key), MaximumPortraitBytes);

    public void Write(string key, byte[] content)
    {
        var value = Encode(content, MaximumPortraitBytes);
        var others = keys(PortraitPrefix).Where(existing => existing != key).ToArray();
        var total = value.Length + others.Sum(existing => (long)(read(PortraitPrefix + existing)?.Length ?? 0));
        // Evict old entries before writing, without ever deleting the last-good value for this key.
        foreach (var existing in others)
        {
            if (total <= MaximumPortraitCharacters)
                break;
            total -= read(PortraitPrefix + existing)?.Length ?? 0;
            remove(PortraitPrefix + existing);
        }
        write(PortraitPrefix + key, value);
    }

    public void Trim(IReadOnlyCollection<string> retainedKeys)
    {
        long total = 0;
        foreach (var key in keys(PortraitPrefix))
        {
            if (!retainedKeys.Contains(key))
            {
                remove(PortraitPrefix + key);
                continue;
            }
            total += read(PortraitPrefix + key)?.Length ?? 0;
            if (total > MaximumPortraitCharacters)
                remove(PortraitPrefix + key);
        }
    }

    private static string Encode(byte[] content, int maximumBytes)
    {
        if (content.Length > maximumBytes)
            throw new IOException("The browser cache is full. This download exceeds its storage limit.");
        return Convert.ToBase64String(content);
    }

    private static byte[]? Decode(string? value, int maximumBytes)
    {
        if (value is null)
            return null;
        if (value.Length > ((maximumBytes + 2) / 3) * 4)
            throw new IOException("The saved browser cache exceeds its storage limit.");
        try
        {
            var decoded = Convert.FromBase64String(value);
            return decoded.Length <= maximumBytes ? decoded
                : throw new IOException("The saved browser cache exceeds its storage limit.");
        }
        catch (FormatException exception) { throw new IOException("The saved browser cache is invalid.", exception); }
    }
}
