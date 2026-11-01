using System.Windows.Input;
using DotNetConf.App.ViewModels;

namespace DotNetConf.App.Planner;

/// <summary>
/// The schedule planner as a MAUI view. <see cref="SchedulePlannerHandler"/> draws it with the Avalonia
/// <see cref="PlannerView"/>: embedded in the native Android and iOS apps, and directly on Linux and in the browser.
/// </summary>
public sealed class SchedulePlanner : View
{
    public static readonly BindableProperty SessionsProperty = BindableProperty.Create(
        nameof(Sessions), typeof(IReadOnlyList<SessionViewModel>), typeof(SchedulePlanner), Array.Empty<SessionViewModel>());

    public static readonly BindableProperty TopicsProperty = BindableProperty.Create(
        nameof(Topics), typeof(IReadOnlyList<TopicFilterViewModel>), typeof(SchedulePlanner), Array.Empty<TopicFilterViewModel>());

    public static readonly BindableProperty ClearTopicsCommandProperty = BindableProperty.Create(
        nameof(ClearTopicsCommand), typeof(ICommand), typeof(SchedulePlanner));

    public static readonly BindableProperty BottomInsetProperty = BindableProperty.Create(
        nameof(BottomInset), typeof(double), typeof(SchedulePlanner), 0d);

    public static readonly BindableProperty ThemeProperty = BindableProperty.Create(
        nameof(Theme), typeof(AppTheme), typeof(SchedulePlanner), AppTheme.Unspecified);

    // MAUI keeps this in step with the app theme, so the handler can map it like any other property.
    public SchedulePlanner() => this.SetAppTheme(ThemeProperty, AppTheme.Light, AppTheme.Dark);

    /// <summary>
    /// A snapshot of the saved sessions to show on the timeline.
    /// </summary>
    public IReadOnlyList<SessionViewModel> Sessions
    {
        get => (IReadOnlyList<SessionViewModel>)GetValue(SessionsProperty);
        set => SetValue(SessionsProperty, value);
    }

    /// <summary>
    /// The shared topic filters, so the planner and the Sessions tab stay in step.
    /// </summary>
    public IReadOnlyList<TopicFilterViewModel> Topics
    {
        get => (IReadOnlyList<TopicFilterViewModel>)GetValue(TopicsProperty);
        set => SetValue(TopicsProperty, value);
    }

    /// <summary>
    /// Clears the topic filters, for the planner's All topics chip.
    /// </summary>
    public ICommand? ClearTopicsCommand
    {
        get => (ICommand?)GetValue(ClearTopicsCommandProperty);
        set => SetValue(ClearTopicsCommandProperty, value);
    }

    /// <summary>
    /// How much of the planner's bottom edge the tab bar covers, where it is translucent and drawn over it on iOS.
    /// </summary>
    public double BottomInset
    {
        get => (double)GetValue(BottomInsetProperty);
        set => SetValue(BottomInsetProperty, value);
    }

    /// <summary>
    /// The app's current light or dark theme.
    /// </summary>
    public AppTheme Theme => (AppTheme)GetValue(ThemeProperty);
}
