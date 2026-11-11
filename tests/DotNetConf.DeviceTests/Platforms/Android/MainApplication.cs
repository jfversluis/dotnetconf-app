using Android.App;
using Android.Runtime;

namespace DotNetConf.DeviceTests;

[Application]
public sealed class MainApplication(nint handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp() => NativeTestApplication.CreateMauiApp();
}
