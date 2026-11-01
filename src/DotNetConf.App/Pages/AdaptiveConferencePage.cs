using DotNetConf.App.Controls;
using DotNetConf.App.Services;
using DotNetConf.App.ViewModels;
#if ANDROID
using DotNetConf.App.Platforms.Android;
#endif
#if IOS
using DotNetConf.App.Platforms.iOS;
#endif

namespace DotNetConf.App.Pages;

public class AdaptiveConferencePage : ConferencePage
{
    private static readonly SafeAreaEdges PaneSafeAreaEdges = new(
        SafeAreaRegions.Container, SafeAreaRegions.Container, SafeAreaRegions.Container,
#if ANDROID
        SafeAreaRegions.None);
#else
        SafeAreaRegions.SoftInput);
#endif

    private Grid? panes;
    private Grid? detailPane;
    private BoxView? verticalDivider;
    private BoxView? horizontalDivider;
    private FoldableViewport? viewport;
    private readonly ContentView detailContent = new();
    private readonly TextScaleMonitor textScale = new();
    private DetailNavigationService? navigation;
    private SessionDetailView? sessionDetail;
    private SpeakerDetailView? speakerDetail;
    private AgendaSearchField? search;
    private Grid? searchLayout;
    private CollectionView? searchResults;
    private bool isAppearing;
    private bool isCompactSearchActive;
    private bool listHeaderShouldScroll;
    private bool listIsStacked;
    private bool searchLayoutNeedsRefresh;
    private bool? navigationBeforeSearch;
#if ANDROID
    private Android.Views.View? searchTabBar;
    private bool sawSearchSoftInput;
    private bool searchLostFocus;
    private bool dismissingSearchKeyboard;
#endif

    public static readonly BindableProperty SelectedIdProperty = BindableProperty.Create(
        nameof(SelectedId), typeof(string), typeof(AdaptiveConferencePage), null);
    public string? SelectedId { get => (string?)GetValue(SelectedIdProperty); private set => SetValue(SelectedIdProperty, value); }
    public DetailSelection Selection { get; private set; } = new(DetailKind.Session);
    public bool IsInline { get; set; }
    public bool CanShowInline { get; private set; }
    public bool IsCompactSearchActive => isCompactSearchActive;
    internal double? TextScale => textScale.TextScale;

    public AdaptiveConferencePage(ConferenceViewModel conference) : base(conference) { }

    protected void ConfigureDetails(DetailKind kind, DetailNavigationService service)
    {
        Selection = new(kind);
        navigation = service;
        service.RegisterHost(this);
        var list = Content;
        // Detach first so replacing Content does not sever the list's new logical parent.
        Content = null;
        detailPane = new Grid
        {
            RowDefinitions = [new(new GridLength(0)), new(GridLength.Star)],
            ColumnDefinitions = [new(new GridLength(1)), new(GridLength.Star)],
            SafeAreaEdges = PaneSafeAreaEdges,
            AutomationId = "detail-pane"
        };
        verticalDivider = new BoxView { VerticalOptions = LayoutOptions.Fill };
        detailPane.Add(verticalDivider);
        Grid.SetRowSpan(verticalDivider, 2);
        horizontalDivider = new BoxView { HorizontalOptions = LayoutOptions.Fill, IsVisible = false };
        detailPane.Add(horizontalDivider);
        Grid.SetColumnSpan(horizontalDivider, 2);
        detailPane.Add(detailContent, 1, 1);
        panes = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(new GridLength(0)), new(new GridLength(0))],
            RowDefinitions = [new(GridLength.Star), new(new GridLength(0)), new(new GridLength(0))],
            ColumnSpacing = 0, RowSpacing = 0,
            SafeAreaEdges = PaneSafeAreaEdges,
            AutomationId = "adaptive-list-detail"
        };
        panes.Add(list!);
        panes.Add(detailPane, 2);
        panes.Add(textScale);
        textScale.TextScaleChanged += (_, _) => service.RequestLayoutSync();
        Content = panes;
#if IOS
        _ = new EmptyKeyboardFrameWorkaround(panes, detailPane);
        viewport = new FoldableViewport(panes, panes, service.RequestLayoutSync, service.FoldableRegionSource);
#else
        viewport = new FoldableViewport(panes, this, service.RequestLayoutSync, service.FoldableRegionSource);
#endif
        RenderDetail();
    }

    public void RenderDetail()
    {
        var current = Selection.Current;
        SelectedId = IsInline ? Selection.ListItemId : null;
        if (!IsInline || !CanShowInline)
        {
            // Do not keep a live virtualized detail list measured inside a collapsed pane.
            ReleaseDetails();
            return;
        }
        if (current is null)
        {
            ReleaseDetails();
            var title = Selection.ListKind == DetailKind.Session ? "Choose a session" : "Choose a speaker";
            detailContent.Content = new VerticalStackLayout
            {
                Padding = 32, Spacing = 12, VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center, MaximumWidthRequest = 420,
                AutomationId = "detail-pane-empty",
                Children =
                {
                    new Label { Text = title, Style = (Style)Application.Current!.Resources["Subheading"] },
                    new Label { Text = DetailSelectionHint,
                        Style = (Style)Application.Current.Resources["Secondary"] }
                }
            };
            return;
        }

        if (current.Kind == DetailKind.Session)
        {
            sessionDetail ??= new SessionDetailView();
            sessionDetail.Show(Conference, current.Id);
            detailContent.Content = sessionDetail;
        }
        else
        {
            speakerDetail ??= new SpeakerDetailView();
            speakerDetail.Show(Conference, current.Id);
            detailContent.Content = speakerDetail;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        isAppearing = true;
        navigation?.RequestLayoutSync();
        UpdateSearchViewport();
    }

    protected override void OnDisappearing()
    {
        isAppearing = false;
        UpdateSearchViewport();
        ReleaseDetails();
        base.OnDisappearing();
    }

    private void ReleaseDetails()
    {
        detailContent.Content = null;
        sessionDetail = null;
        speakerDetail = null;
    }

    internal void UpdateLayoutPolicy(double windowWidth, double windowHeight, double textScale,
        DetailLayout? foregroundLayout = null)
    {
        if (panes is null || detailPane is null || viewport is null)
            return;

        // A hidden host retains its old measurement while native details resize in the foreground.
        var width = foregroundLayout.HasValue ? windowWidth : panes.Width > 0 ? panes.Width : windowWidth;
        var layout = AdaptDetailLayout(foregroundLayout ?? viewport.GetLayout(windowWidth, windowHeight, textScale),
            width, textScale);
        CanShowInline = layout.IsSplit;
        detailPane.IsVisible = CanShowInline;
        Grid.SetRow(detailPane, layout.IsStacked ? 2 : 0);
        Grid.SetColumn(detailPane, layout.IsStacked ? 0 : 2);
        panes.ColumnDefinitions[0].Width = layout.IsStacked ? GridLength.Star : layout.ListLength;
        panes.ColumnDefinitions[1].Width = layout.IsStacked ? new GridLength(0) : layout.HingeLength;
        panes.ColumnDefinitions[2].Width = layout.IsStacked ? new GridLength(0) : layout.DetailLength;
        panes.RowDefinitions[0].Height = layout.IsStacked ? layout.ListLength : GridLength.Star;
        panes.RowDefinitions[1].Height = layout.IsStacked ? layout.HingeLength : new GridLength(0);
        panes.RowDefinitions[2].Height = layout.IsStacked ? layout.DetailLength : new GridLength(0);
        detailPane.ColumnDefinitions[0].Width = new GridLength(layout.IsStacked ? 0 : 1);
        detailPane.RowDefinitions[0].Height = new GridLength(layout.IsStacked ? 1 : 0);
        verticalDivider!.IsVisible = !layout.IsStacked;
        horizontalDivider!.IsVisible = layout.IsStacked;
        listHeaderShouldScroll = DetailLayout.ShouldScrollListHeader(windowHeight, textScale, layout.IsStacked);
        listIsStacked = layout.IsStacked;
        UpdateListHeader();
        UpdateSearchViewport();
    }

    protected virtual DetailLayout AdaptDetailLayout(DetailLayout layout, double width, double textScale) => layout;

    protected virtual string DetailSelectionHint => "Select from the list to explore the details here.";

    protected void ConfigureSearchViewport(AgendaSearchField field, Grid layout, CollectionView results)
    {
        search = field;
        searchLayout = layout;
        searchResults = results;
        field.SearchFocusChanged += OnSearchViewportChanged;
        results.SizeChanged += OnSearchViewportChanged;
        layout.SizeChanged += OnSearchLayoutSizeChanged;
    }

    private void OnSearchViewportChanged(object? sender, EventArgs e)
    {
#if ANDROID
        if (e is FocusEventArgs focus)
            searchLostFocus = !focus.IsFocused;
#endif
        UpdateSearchViewport();
    }

#if ANDROID
    private async void OnSearchLayoutSizeChanged(object? sender, EventArgs e)
#else
    private void OnSearchLayoutSizeChanged(object? sender, EventArgs e)
#endif
    {
        if (navigationBeforeSearch.HasValue || searchLayoutNeedsRefresh)
        {
            searchLayoutNeedsRefresh = false;
            // Shell's navigation transition can leave Grid's star row measured at the old keyboard height.
            searchLayout?.InvalidateMeasure();
        }
#if ANDROID
        UpdateSearchKeyboardOverlap();
        // Android can reopen the IME after a fold has already removed search focus.
        if (searchLostFocus && !dismissingSearchKeyboard && isAppearing &&
            search is { IsSearchFocused: false, IsSoftInputShowing: true, HasActiveInputConnection: true })
        {
            dismissingSearchKeyboard = true;
            try { await search.HideKeyboardAsync(); }
            finally { dismissingSearchKeyboard = false; }
        }
#endif
    }

    private void UpdateSearchViewport()
    {
        if (search is null || searchResults is null)
            return;

        var window = Window;
#if ANDROID
        if (isAppearing && search.IsSearchFocused && navigationBeforeSearch.HasValue)
        {
            if (search.IsSoftInputShowing)
                sawSearchSoftInput = true;
            else if (sawSearchSoftInput)
            {
                sawSearchSoftInput = false;
                search.UnfocusSearch();
                return;
            }
        }
        else
            sawSearchSoftInput = false;
#endif
        var hide = isAppearing && window is not null &&
            DetailLayout.ShouldHideNavigationForSearch(search.IsSearchFocused, window.Width, window.Height,
                searchResults.Height, navigationBeforeSearch.HasValue);
#if ANDROID
        var keyboard = Android.App.Application.Context.Resources?.Configuration;
        var hasHardwareKeyboard = keyboard is
        {
            HardKeyboardHidden: Android.Content.Res.HardKeyboardHidden.No,
            Keyboard: Android.Content.Res.KeyboardType.Qwerty or Android.Content.Res.KeyboardType.Twelvekey
        };
        hide |= isAppearing && window is not null &&
            DetailLayout.ShouldPrepareShortLandscapeSearch(search.IsSearchFocused, window.Width, window.Height,
                hasHardwareKeyboard);
#endif
        if (hide && navigationBeforeSearch is null)
        {
            navigationBeforeSearch = Shell.GetNavBarIsVisible(this);
            searchLayoutNeedsRefresh = true;
#if ANDROID
            HideSearchTabBar();
#endif
            Shell.SetNavBarIsVisible(this, false);
        }
        else if (!hide && navigationBeforeSearch is { } wasVisible)
        {
            navigationBeforeSearch = null;
            searchLayoutNeedsRefresh = true;
            Shell.SetNavBarIsVisible(this, wasVisible);
#if ANDROID
            RestoreSearchTabBar();
#endif
        }
#if ANDROID
        if (hide)
            HideSearchTabBar();
        else
            RestoreSearchTabBar();
#endif
        if (isCompactSearchActive != hide)
        {
            isCompactSearchActive = hide;
            OnPropertyChanged(nameof(IsCompactSearchActive));
            UpdateListHeader();
        }
#if ANDROID
        UpdateSearchKeyboardOverlap();
#endif
    }

#if ANDROID
    private void HideSearchTabBar()
    {
        var tabBar = (Shell.Current?.CurrentItem?.Handler as
            Microsoft.Maui.Controls.Handlers.ShellItemHandler)?.BottomNavigationView;
        if (ReferenceEquals(searchTabBar, tabBar))
            return;
        RestoreSearchTabBar();
        if (Shell.GetTabBarIsVisible(this) &&
            tabBar is { IsAttachedToWindow: true, Parent: not null, Visibility: Android.Views.ViewStates.Visible })
        {
            searchTabBar = tabBar;
            // Removing Shell's tab fragment can race with a rapid navigation or fold transition.
            tabBar.Visibility = Android.Views.ViewStates.Gone;
        }
    }

    private void RestoreSearchTabBar()
    {
        if (searchTabBar is not { } tabBar)
            return;
        searchTabBar = null;
        tabBar.Visibility = Android.Views.ViewStates.Visible;
    }

    private void UpdateSearchKeyboardOverlap()
    {
        if (panes is null)
            return;

        var overlap = isAppearing && navigationBeforeSearch.HasValue && search?.IsSearchFocused == true
            ? SearchKeyboardInset.GetBottomOverlap(panes)
            : 0;
        if (overlap is not { } bottom || panes.Padding.Bottom == bottom)
            return;

        panes.Padding = new Thickness(panes.Padding.Left, panes.Padding.Top, panes.Padding.Right, bottom);
    }
#endif

    private void UpdateListHeader() =>
        OnListLayoutChanged(listHeaderShouldScroll || isCompactSearchActive, listIsStacked);

    protected static void MoveListHeader(ContentView pinnedHeader, ContentView scrollingHeader, bool scrollHeader)
    {
        var source = scrollHeader ? pinnedHeader : scrollingHeader;
        var target = scrollHeader ? scrollingHeader : pinnedHeader;
        var header = source.Content ?? throw new InvalidOperationException("The list header is missing.");
        source.Content = null;
        source.IsVisible = false;
        target.Content = header;
        target.IsVisible = true;
    }

    protected virtual void OnListLayoutChanged(bool scrollHeader, bool stacked) { }
}
