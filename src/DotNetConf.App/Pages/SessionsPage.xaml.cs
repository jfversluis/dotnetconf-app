using DotNetConf.App.ViewModels;
using DotNetConf.App.Services;
using System.Collections.Specialized;
using System.ComponentModel;
using Syncfusion.Maui.Toolkit.TabView;

namespace DotNetConf.App.Pages;

public partial class SessionsPage : AdaptiveConferencePage
{
    private bool rebuildingDays;
    private bool rebuildQueued;
    private bool headerScrolls;
    private bool headerIsStacked;

    public SessionsPage(ConferenceViewModel conference, DetailNavigationService navigation) : base(conference)
    {
        InitializeComponent();
        ConfigureDetails(DetailKind.Session, navigation);
        ConfigureSearchViewport(SearchField, SearchLayout, SearchResults);
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

    protected override void OnListLayoutChanged(bool scrollHeader, bool stacked)
    {
        SessionHeading.IsVisible = !scrollHeader;
        if (headerIsStacked != stacked)
        {
            SessionHeader.Spacing = stacked ? 8 : 16;
            SessionHeader.Padding = new Thickness(24, stacked ? 8 : 16, 24, stacked ? 4 : 8);
            SessionStatus.Spacing = stacked ? 8 : 16;
            SessionStatus.Padding = new Thickness(24, stacked ? 8 : 16, 24, stacked ? 4 : 8);
            headerIsStacked = stacked;
        }

        if (headerScrolls != scrollHeader)
        {
            FilterHeader.Padding = scrollHeader ? new Thickness(24, 8, 24, 8) : new Thickness(0);
            MoveListHeader(PinnedHeader, ScrollingHeader, scrollHeader);
            headerScrolls = scrollHeader;
        }
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
                    Padding = new Thickness(8, 12),
                    Spacing = 0,
                    MinimumWidthRequest = DayTabLayout.MinimumWidth,
                    VerticalOptions = LayoutOptions.Center,
                    AutomationId = $"day-{day.Label}"
                };
                var lines = day.Day is { } date
                    ? new[] { date.ToString("ddd"), date.ToString("MMM d") }
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

        var headers = DayTabs.Items.Select(item => item.HeaderContent)
            .OfType<VerticalStackLayout>()
            .ToArray();
        var widestLabel = headers.SelectMany(header => header.Children.OfType<Label>())
            .Select(label => label.Measure(double.PositiveInfinity, double.PositiveInfinity).Width)
            .DefaultIfEmpty(0).Max();
        var scroll = DayTabLayout.ShouldScroll(DayTabs.Width, DayTabs.Items.Count, widestLabel);
        var mode = scroll ? TabWidthMode.SizeToContent : TabWidthMode.Default;
        if (DayTabs.TabWidthMode != mode)
            DayTabs.TabWidthMode = mode;

        // The toolkit fixes HeaderContent's height to the current bar height.
        var width = scroll ? double.PositiveInfinity :
            Math.Max(1, DayTabs.Width / DayTabs.Items.Count - DayTabLayout.HorizontalPadding);
        var height = headers.Select(header => header.Padding.Top + header.Padding.Bottom +
            header.Children.OfType<Label>()
                .Sum(label => label.Measure(width, double.PositiveInfinity).Height))
            .DefaultIfEmpty(72).Max();
        DayTabs.TabBarHeight = Math.Max(72, Math.Ceiling(height));
    }
}
