using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace DotNetConf.App;

/// <summary>
/// Reads and writes local storage directly, so each tab sees what the others saved. Preferences keep a copy of
/// every value per tab and write them all back on each change, which overwrites what other tabs saved.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class BrowserStorage
{
    /// <summary>
    /// The keys that start with the prefix, minus the prefix.
    /// </summary>
    public static List<string> Keys(string prefix) => Use(() =>
    {
        var keys = new List<string>();
        for (var index = 0; Key(index) is { } key; index++)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                keys.Add(key[prefix.Length..]);
        }
        return keys;
    });

    public static void Set(string key, string value) => Use(() =>
    {
        SetItem(key, value);
        return true;
    });

    public static void Remove(string key) => Use(() =>
    {
        RemoveItem(key);
        return true;
    });

    private static T Use<T>(Func<T> access)
    {
        try
        {
            return access();
        }
        catch (JSException exception)
        {
            throw new IOException("Local storage is not available.", exception);
        }
    }

    [JSImport("globalThis.localStorage.key")]
    private static partial string? Key(int index);

    [JSImport("globalThis.localStorage.setItem")]
    private static partial void SetItem(string key, string value);

    [JSImport("globalThis.localStorage.removeItem")]
    private static partial void RemoveItem(string key);
}
