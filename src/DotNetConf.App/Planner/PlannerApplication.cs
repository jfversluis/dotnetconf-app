namespace DotNetConf.App.Planner;

using Avalonia;
using Avalonia.Themes.Fluent;

/// <summary>
/// The Avalonia application behind the planner inside the native Android and iOS apps; the Fluent theme
/// styles its scroll viewers.
/// </summary>
internal sealed class PlannerApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}
