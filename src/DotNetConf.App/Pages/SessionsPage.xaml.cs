using DotNetConf.App.ViewModels;
using DotNetConf.Core;
using System.Collections.Specialized;
using System.ComponentModel;
using Syncfusion.Maui.Toolkit.TabView;

namespace DotNetConf.App.Pages;

public partial class SessionsPage : ConferencePage
{
    private bool rebuildingDays;
    private bool rebuildQueued;

    public SessionsPage(ConferenceViewModel conference) : base(conference)
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        Conference.Days.CollectionChanged += OnDaysChanged;
        Conference.PropertyChanged += OnConferencePropertyChanged;
        RebuildDayTabs();
        ClearNativeFilterBadge();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        Conference.Days.CollectionChanged -= OnDaysChanged;
        Conference.PropertyChanged -= OnConferencePropertyChanged;
    }

    private void OnDaysChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (rebuildQueued)
            return;
        rebuildQueued = true;
        Dispatcher.Dispatch(() =>
        {
            rebuildQueued = false;
            RebuildDayTabs();
        });
    }

    private void RebuildDayTabs()
    {
        rebuildingDays = true;
        try
        {
            DayTabs.Items.Clear();
            foreach (var day in Conference.Days)
            {
                var header = new VerticalStackLayout
                {
                    Padding = new Thickness(4, 12),
                    Spacing = 0,
                    VerticalOptions = LayoutOptions.Center,
                    AutomationId = $"day-{day.Label}"
                };
                var lines = day.Day is { } date
                    ? new[] { date.ToString("ddd"), Agenda.MonthDay(date) }
                    : new[] { "All", "days" };
                foreach (var line in lines)
                {
                    var label = new Label
                    {
                        Text = line,
                        FontFamily = "OpenSansSemibold",
                        FontSize = 14,
                        FontAutoScalingEnabled = true,
                        HorizontalTextAlignment = TextAlignment.Center
                    };
                    label.SetAppThemeColor(Label.TextColorProperty,
                        (Color)Application.Current!.Resources["Midnight"],
                        (Color)Application.Current.Resources["LavenderLight"]);
                    label.SizeChanged += OnDayHeaderSizeChanged;
                    header.Add(label);
                }
                SemanticProperties.SetDescription(header, $"Show sessions: {day.Label}");
                header.Loaded += OnDayHeaderSizeChanged;
                header.SizeChanged += OnDayHeaderSizeChanged;
                DayTabs.Items.Add(new SfTabItem { Header = day.Label, HeaderContent = header });
            }
            DayTabs.SelectedIndex = Conference.SelectedDayIndex;
        }
        finally
        {
            rebuildingDays = false;
        }
    }

    private void OnConferencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!rebuildingDays && !rebuildQueued && e.PropertyName == nameof(ConferenceViewModel.SelectedDayIndex))
            DayTabs.SelectedIndex = Conference.SelectedDayIndex;
        if (e.PropertyName == nameof(ConferenceViewModel.FilterBadgeText))
            Dispatcher.Dispatch(ClearNativeFilterBadge);
    }

    private void ClearNativeFilterBadge()
    {
        // iOS can retain the native badge after BadgeText becomes null; recreate its toolbar entry.
        if (!Conference.HasTopicFilters && ToolbarItems.Remove(FilterToolbar))
            ToolbarItems.Insert(0, FilterToolbar);
    }

    private void OnDaySelectionChanged(object? sender, TabSelectionChangedEventArgs e)
    {
        if (!rebuildingDays && !rebuildQueued)
            Conference.SelectedDayIndex = DayTabs.SelectedIndex;
    }

    private void OnDayHeaderSizeChanged(object? sender, EventArgs e)
    {
        if (DayTabs is null || DayTabs.Width <= 0 || DayTabs.Items.Count == 0)
            return;

        // Measure the children: the toolkit fixes HeaderContent's height to the current bar height.
        var width = Math.Max(1, DayTabs.Width / DayTabs.Items.Count - 8);
        var height = DayTabs.Items.Select(item => item.HeaderContent)
            .OfType<VerticalStackLayout>()
            .Select(header => 24 + header.Children.OfType<Label>()
                .Sum(label => label.Measure(width, double.PositiveInfinity).Height))
            .DefaultIfEmpty(72).Max();
        DayTabs.TabBarHeight = Math.Max(72, Math.Ceiling(height));
    }
}
