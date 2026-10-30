using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace DotNetConf.App;

internal sealed partial class AvaloniaConnectivity : IConnectivity
{
    public NetworkAccess NetworkAccess { get; private set; } = NetworkAccess.Unknown;
    public IEnumerable<ConnectionProfile> ConnectionProfiles => [ConnectionProfile.Unknown];
    public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;

    public AvaloniaConnectivity()
    {
        if (OperatingSystem.IsBrowser())
        {
            SetOnline(IsOnline());
            Subscribe(SetOnline);
        }
    }

    private void SetOnline(bool online)
    {
        var next = online ? NetworkAccess.Internet : NetworkAccess.None;
        if (next == NetworkAccess)
            return;
        NetworkAccess = next;
        ConnectivityChanged?.Invoke(this, new(next, ConnectionProfiles));
    }

    [SupportedOSPlatform("browser")]
    [JSImport("globalThis.dotnetConfConnectivity.isOnline")]
    private static partial bool IsOnline();

    [SupportedOSPlatform("browser")]
    [JSImport("globalThis.dotnetConfConnectivity.subscribe")]
    private static partial void Subscribe([JSMarshalAs<JSType.Function<JSType.Boolean>>] Action<bool> callback);
}
