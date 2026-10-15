using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace DotNetConf.App;

/// <summary>
/// Reads and writes a browser local storage key on every call, so each tab sees what the others saved.
/// </summary>
/// <remarks>
/// Preferences keep one copy of every value per tab and write all of them back on each change, so a tab would
/// overwrite what another tab saved since it loaded. Storage errors surface as <see cref="IOException"/>, as the
/// schedule file's do.
/// </remarks>
[SupportedOSPlatform("browser")]
internal static class BrowserStorage
{
    public static string? Get(string key) => Use(storage => storage.GetPropertyAsString(key));

    public static void Set(string key, string value) => Use(storage =>
    {
        storage.SetProperty(key, value);
        return value;
    });

    private static T Use<T>(Func<JSObject, T> access)
    {
        try
        {
            using var storage = JSHost.GlobalThis.GetPropertyAsJSObject("localStorage")
                ?? throw new IOException("Local storage is not available.");
            return access(storage);
        }
        catch (JSException exception)
        {
            throw new IOException("Local storage is not available.", exception);
        }
    }
}
