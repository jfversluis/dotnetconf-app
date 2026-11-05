using DotNetConf.Core;

namespace DotNetConf.App;

/// <summary>Bounded, versioned storage separate from preferences and saved-session keys.</summary>
internal sealed class BrowserCache(
    Func<string, string?> read,
    Action<string, string> write) : IConferenceCacheStore
{
    internal const string AgendaKey = "dotnetconf.cache.agenda.v1";
    internal const int MaximumAgendaBytes = 1024 * 1024;

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
