using DotNetConf.App.Controls;
using DotNetConf.App.Services;

namespace DotNetConf.DeviceTests;

public sealed class NativeTestApplication : Application
{
    internal static readonly TaskCompletionSource<Shell> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<NativeTestApplication>();
#if IOS
        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<DuoSafeAreaContent, DotNetConf.App.Platforms.iOS.DuoSafeAreaContentHandler>());
#endif
        return builder.Build();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        Routing.RegisterRoute(SessionRoutes.Template, typeof(RouteResultPage));
        var shell = new Shell { FlyoutBehavior = FlyoutBehavior.Disabled };
        shell.Items.Add(new ShellContent
        {
            Route = "sessions", Title = "Sessions",
            Content = new ContentPage { Title = "Sessions", Content = new Label { Text = "Isolated native regression tests" } }
        });
        shell.Navigated += (_, _) => BackNavigation.Update(shell);
        shell.Loaded += (_, _) =>
        {
            if (!Ready.TrySetResult(shell))
                return;
#if IOS
            Task.Run(async () =>
            {
                try { Environment.Exit((await NativeTestRunner.RunAsync()).ExitCode); }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(exception);
                    Environment.Exit(1);
                }
            });
#endif
        };
        return new Window(shell);
    }
}

public sealed class RouteResultPage : ContentPage, IQueryAttributable
{
    public string? SessionId { get; private set; }

    public RouteResultPage()
    {
        Title = "Synthetic test session";
        Content = new Label { Text = "Synthetic route-test content; not conference data." };
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query) =>
        SessionId = query.TryGetValue("sessionId", out var value) ? value as string : null;
}
