namespace DotNetConf.App.Services;

public interface ISessionVideoLauncher
{
    Task<bool> OpenAsync(Uri uri);
}

#if AVALONIA
public sealed class SessionVideoLauncher(IBrowser browser) : ISessionVideoLauncher
{
    public Task<bool> OpenAsync(Uri uri) => browser.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
}
#else
public sealed class SessionVideoLauncher(ILauncher launcher) : ISessionVideoLauncher
{
    public Task<bool> OpenAsync(Uri uri) => launcher.OpenAsync(uri);
}
#endif
