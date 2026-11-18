using DotNetConf.App.ViewModels;
using DotNetConf.App.Services;
using DotNetConf.Core;
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
    private DayFilter[] renderedDays = [];
    private bool headerTargetCollapsed;
    private bool focusSearchAfterExpansion;
    private double lastScrollOffset;
    private int headerAnimationGeneration;
    private long headerLayoutSettlesAt;
    private const string HeaderAnimation = "SessionHeader";
    private const uint HeaderAnimationLength = 200;
    private readonly SessionHeaderScrollState headerScroll = new();
    private readonly SessionCriteriaSnapshot criteria = new();

    public SessionsPage(ConferenceViewModel conference, DetailNavigationService navigation) : base(conference)
    {
        InitializeComponent();
        ToolbarItems.Remove(SearchToolbar);
        ConfigureDetails(DetailKind.Session, navigation);
        ConfigureSearchViewport(SearchField, SearchLayout, SearchResults);
        SearchResults.SizeChanged += OnResultsSizeChanged;
        SearchField.SearchFocusChanged += OnSearchFocusChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        Conference.Days.CollectionChanged += OnDaysChanged;
        Conference.PropertyChanged += OnConferencePropertyChanged;
        RebuildDayTabs();
        ClearNativeFilterBadge();
        // Android Shell unloads this page under pushed pages, so changes made there arrive while unsubscribed.
        RevealIfCriteriaChanged();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        SettleHeader();
        Conference.Days.CollectionChanged -= OnDaysChanged;
        Conference.PropertyChanged -= OnConferencePropertyChanged;
    }

    protected override void OnDisappearing()
    {
        SettleHeader();
        base.OnDisappearing();
    }

    protected override void OnListLayoutChanged(bool scrollHeader, bool stacked)
    {
        if (headerIsStacked != stacked)
        {
            CollapsibleContent.Spacing = stacked ? 8 : 16;
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
        if (scrollHeader)
            headerScroll.Expand();
        CollapsibleContent.Padding = new Thickness(0, 0, 0, scrollHeader ? 0 : stacked ? 8 : 16);
        UpdateHeaderVisibility();
    }

    private void OnSessionsScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if ((!OperatingSystem.IsIOS() && !OperatingSystem.IsAndroid()) || SearchResults.Height <= 0)
            return;

        lastScrollOffset = e.VerticalOffset;
        headerScroll.Update(lastScrollOffset, SearchResults.Height, headerScrolls || SearchField.IsSearchFocused,
            rebase: this.AnimationIsRunning(HeaderAnimation) || Environment.TickCount64 < headerLayoutSettlesAt);
        UpdateHeaderVisibility(animate: true);
    }

    private void OnResultsSizeChanged(object? sender, EventArgs e)
    {
        if (SearchResults.Height > 0)
            headerScroll.Update(lastScrollOffset, SearchResults.Height, rebase: true);
    }

    private void OnSearchFocusChanged(object? sender, FocusEventArgs e)
    {
        if (e.IsFocused)
        {
            headerScroll.Expand();
            UpdateHeaderVisibility();
        }
    }

    private void UpdateHeaderVisibility(bool animate = false)
    {
        var collapsed = headerScroll.IsCollapsed;
        if (collapsed)
        {
            if (!ToolbarItems.Contains(SearchToolbar))
                InsertSearchToolbar();
        }
        else
            ToolbarItems.Remove(SearchToolbar);

        if (animate && collapsed == headerTargetCollapsed)
            return;

        var changed = collapsed != headerTargetCollapsed;
        headerTargetCollapsed = collapsed;
        var generation = ++headerAnimationGeneration;
        this.AbortAnimation(HeaderAnimation);
#if IOS
        animate &= !UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif ANDROID
        animate &= Android.Animation.ValueAnimator.AreAnimatorsEnabled();
#endif
        if (!animate || !IsLoaded || CollapsibleHeader.Width <= 0)
        {
            // An instant resize can clamp or re-anchor the list on the next layout pass. Treat those
            // scroll events like the animated path does, rather than as a gesture that toggles back.
            if (changed)
                headerLayoutSettlesAt = Environment.TickCount64 + HeaderAnimationLength + 50;
            ApplyHeaderVisibility();
            return;
        }

        var startHeight = CollapsibleHeader.IsVisible
            ? Math.Max(0, CollapsibleHeader.HeightRequest >= 0
                ? CollapsibleHeader.HeightRequest : CollapsibleHeader.Height)
            : 0;
        var startOpacity = CollapsibleHeader.Opacity;
        var startTranslation = CollapsibleContent.TranslationY;
        SessionHeading.IsVisible = !headerScrolls;
        CollapsibleHeader.IsVisible = true;
        CollapsibleHeader.InputTransparent = collapsed;
        AutomationProperties.SetExcludedWithChildren(CollapsibleHeader, collapsed);
        var endHeight = collapsed ? 0 :
            CollapsibleContent.Measure(CollapsibleHeader.Width, double.PositiveInfinity).Height;
        var endOpacity = collapsed ? 0 : 1;
        var endTranslation = collapsed ? -12 : 0;
        CollapsibleHeader.HeightRequest = startHeight;
        new Animation(progress =>
        {
            CollapsibleHeader.HeightRequest = startHeight + (endHeight - startHeight) * progress;
            CollapsibleHeader.Opacity = startOpacity + (endOpacity - startOpacity) * progress;
            CollapsibleContent.TranslationY = startTranslation + (endTranslation - startTranslation) * progress;
        }).Commit(this, HeaderAnimation, length: HeaderAnimationLength, easing: Easing.CubicInOut,
            finished: (_, _) =>
            {
                // A disabled system ticker reports cancellation without a newer animation; still settle then.
                if (generation == headerAnimationGeneration)
                    ApplyHeaderVisibility();
            });
    }

    private void SettleHeader()
    {
        focusSearchAfterExpansion = false;
        headerAnimationGeneration++;
        this.AbortAnimation(HeaderAnimation);
        ApplyHeaderVisibility();
    }

    private void ApplyHeaderVisibility()
    {
        var collapsed = headerTargetCollapsed;
        CollapsibleHeader.IsVisible = !collapsed;
        CollapsibleHeader.HeightRequest = -1;
        CollapsibleHeader.Opacity = collapsed ? 0 : 1;
        CollapsibleHeader.InputTransparent = collapsed;
        AutomationProperties.SetExcludedWithChildren(CollapsibleHeader, collapsed);
        CollapsibleContent.TranslationY = collapsed ? -12 : 0;
        SessionHeading.IsVisible = !headerScrolls;
        if (!collapsed && focusSearchAfterExpansion)
        {
            focusSearchAfterExpansion = false;
            Dispatcher.Dispatch(() =>
            {
                if (IsLoaded && !headerTargetCollapsed)
                    SearchField.FocusSearch();
            });
        }
    }

    private void OnRevealSearchClicked(object? sender, EventArgs e)
    {
        headerScroll.Expand();
        focusSearchAfterExpansion = true;
        UpdateHeaderVisibility(animate: true);
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
            if (renderedDays.SequenceEqual(Conference.Days))
            {
                DayTabs.SelectedIndex = Conference.SelectedDayIndex;
                return;
            }
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
            renderedDays = Conference.Days.ToArray();
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
        if (e.PropertyName is nameof(ConferenceViewModel.SelectedDayIndex) or
            nameof(ConferenceViewModel.SessionQuery) or nameof(ConferenceViewModel.FilterSummary))
            RevealIfCriteriaChanged();
    }

    private void RevealIfCriteriaChanged()
    {
        // Resume and time-zone refreshes republish unchanged criteria; only a real change reveals the header.
        // FilterSummary abbreviates three or more topics to a count, so include the selected topic names.
        var filters = string.Join('\n', Conference.Topics.Where(topic => topic.IsSelected)
            .Select(topic => topic.Name).Prepend(Conference.FilterSummary));
        if (!criteria.Update(Conference.SelectedDay?.Day, Conference.SessionQuery, Conference.SessionMatchMode, filters))
            return;
        headerScroll.Expand();
        UpdateHeaderVisibility(animate: true);
    }

    private void InsertSearchToolbar()
    {
        // Android reuses native menu items by position and refreshes badges only for badged items, so a
        // badge-less item inserted ahead of Filter would inherit Filter's badge. Recreate Filter after it.
        var filterIndex = OperatingSystem.IsAndroid() ? ToolbarItems.IndexOf(FilterToolbar) : -1;
        if (filterIndex >= 0)
            ToolbarItems.RemoveAt(filterIndex);
        ToolbarItems.Insert(0, SearchToolbar);
        if (filterIndex >= 0)
            ToolbarItems.Insert(filterIndex + 1, FilterToolbar);
    }

    private void ClearNativeFilterBadge()
    {
        // iOS can retain the native badge after BadgeText becomes null; recreate its toolbar entry in place.
        var index = ToolbarItems.IndexOf(FilterToolbar);
        if (Conference.HasTopicFilters || index < 0)
            return;
        ToolbarItems.RemoveAt(index);
        ToolbarItems.Insert(index, FilterToolbar);
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
