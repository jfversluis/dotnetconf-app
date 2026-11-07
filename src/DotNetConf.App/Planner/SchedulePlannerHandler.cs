using Avalonia.Controls.Maui.Handlers;
#if IOS
using Avalonia.Controls.Maui.Platforms.iOS.Handlers;
#endif

namespace DotNetConf.App.Planner;

/// <summary>
/// Hosts <see cref="PlannerView"/> for <see cref="SchedulePlanner"/> and passes it the app's data and colors.
/// </summary>
public sealed class SchedulePlannerHandler() : AvaloniaControlHandler<SchedulePlanner, PlannerView>(PlannerMapper)
{
    private static readonly IPropertyMapper<SchedulePlanner, SchedulePlannerHandler> PlannerMapper =
        new PropertyMapper<SchedulePlanner, SchedulePlannerHandler>(
#if AVALONIA
            // The package's handler chains MAUI's own view mapper, which does nothing on the Avalonia host; the host's
            // mapper goes first so IsVisible, Margin and the rest reach the planner like any other view.
            Avalonia.Controls.Maui.Handlers.ViewHandler.ViewMapper,
#endif
            Mapper)
        {
            [nameof(SchedulePlanner.Sessions)] = (handler, view) => handler.AvaloniaControl?.SetSessions(view.Sessions),
            [nameof(SchedulePlanner.Topics)] = (handler, view) => handler.AvaloniaControl?.SetTopics(view.Topics),
            [nameof(SchedulePlanner.ClearTopicsCommand)] = (handler, view) => handler.AvaloniaControl?.SetClearTopicsCommand(view.ClearTopicsCommand),
            [nameof(SchedulePlanner.BottomInset)] = (handler, view) => handler.AvaloniaControl?.SetBottomInset(view.BottomInset),
            [nameof(SchedulePlanner.TextScale)] = (handler, view) => handler.AvaloniaControl?.SetTextScale(view.TextScale),
            [nameof(SchedulePlanner.SelectedId)] = (handler, view) => handler.AvaloniaControl?.SetSelectedId(view.SelectedId),
            [nameof(SchedulePlanner.Theme)] = MapTheme,
        };

#if IOS
    static SchedulePlannerHandler()
    {
        PlannerMapper.AppendToMapping(nameof(IView.Visibility), (handler, _) => handler.RefreshIfVisible());
    }

    protected override AvaloniaControlHostView CreatePlatformView() => new PlannerHostView();

    protected override void ConnectHandler(AvaloniaControlHostView platformView)
    {
        base.ConnectHandler(platformView);
        ((PlannerHostView)platformView).AttachedToWindow = RefreshIfVisible;
        RefreshIfVisible();
    }

    protected override void DisconnectHandler(AvaloniaControlHostView platformView)
    {
        ((PlannerHostView)platformView).AttachedToWindow = null;
        base.DisconnectHandler(platformView);
    }

    private void RefreshIfVisible()
    {
        if (VirtualView is { IsVisible: true } && PlatformView?.Window is not null)
            AvaloniaControl?.RefreshPresentation();
    }

    private sealed class PlannerHostView : AvaloniaControlHostView
    {
        public Action? AttachedToWindow { get; set; }

        public override void MovedToWindow()
        {
            base.MovedToWindow();
            if (Window is not null)
                AttachedToWindow?.Invoke();
        }
    }
#endif

#if ANDROID || IOS
    protected override void OnAvaloniaControlCreated(PlannerView control)
    {
        base.OnAvaloniaControlCreated(control);
        // Avalonia 12.1.4 pads the host border with window insets already handled by native Shell.
        if (Avalonia.Controls.TopLevel.GetTopLevel(control)?.Content is Avalonia.Controls.Control host)
            Avalonia.Controls.TopLevel.SetAutoSafeAreaPadding(host, false);
    }
#endif

    private static void MapTheme(SchedulePlannerHandler handler, SchedulePlanner view)
    {
        if (handler.AvaloniaControl is not { } control || Application.Current is not { } app)
            return;
        var dark = view.Theme == AppTheme.Dark;
        Avalonia.Media.Color Resource(string key)
        {
            var color = (Color)app.Resources[key];
            return Avalonia.Media.Color.FromArgb(
                (byte)(color.Alpha * 255), (byte)(color.Red * 255), (byte)(color.Green * 255), (byte)(color.Blue * 255));
        }

        control.SetTheme(new PlannerTheme(
            IsDark: dark,
            Background: Resource(dark ? "DarkBackground" : "LightBackground"),
            Surface: Resource(dark ? "DarkSurface" : "LightSurface"),
            Text: Resource(dark ? "DarkText" : "LightText"),
            Secondary: Resource(dark ? "DarkSecondary" : "LightSecondary"),
            Outline: Resource(dark ? "DarkOutline" : "LightOutline"),
            Accent: Resource(dark ? "Lavender" : "Purple"),
            Magenta: Resource("Magenta"),
            Lavender: Resource("Lavender"),
            Midnight: Resource("Midnight"),
            Conflict: Resource(dark ? "DarkError" : "LightError"),
            Live: Resource(dark ? "DarkLive" : "LightLive")));
    }
}
