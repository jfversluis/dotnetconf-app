using Avalonia.Controls.Maui.Handlers;

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
            [nameof(SchedulePlanner.SavedSessions)] = (handler, view) => handler.AvaloniaControl?.SetSavedSessions(view.SavedSessions),
            [nameof(SchedulePlanner.ClearTopicsCommand)] = (handler, view) => handler.AvaloniaControl?.SetClearTopicsCommand(view.ClearTopicsCommand),
            [nameof(SchedulePlanner.BottomInset)] = (handler, view) => handler.AvaloniaControl?.SetBottomInset(view.BottomInset),
            [nameof(SchedulePlanner.Theme)] = MapTheme,
        };

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
            Purple: Resource("Purple"),
            Magenta: Resource("Magenta"),
            Lavender: Resource("Lavender"),
            Midnight: Resource("Midnight"),
            Conflict: Resource(dark ? "DarkError" : "LightError")));
    }
}
