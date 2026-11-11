using Foundation;
using UIKit;

namespace DotNetConf.DeviceTests;

public static class Program
{
    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(NativeTestAppDelegate));
}

[Register("NativeTestAppDelegate")]
public sealed class NativeTestAppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => NativeTestApplication.CreateMauiApp();
}

[Register("SceneDelegate")]
public sealed class SceneDelegate : MauiUISceneDelegate;
