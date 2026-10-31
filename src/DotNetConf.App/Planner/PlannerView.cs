namespace DotNetConf.App.Planner;

using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DotNetConf.App.ViewModels;
using DotNetConf.Core;

/// <summary>
/// The My Schedule timeline, drawn by Avalonia on every target: saved sessions on each local day to scale,
/// overlaps hatched, topics filtered in place and a live marker for the current time.
/// Only what is on screen gets controls, so it stays smooth with very large agendas, and it zooms by pinch,
/// trackpad, Ctrl + wheel, keyboard or its zoom buttons.
/// </summary>
public sealed class PlannerView : UserControl
{
    private const double DefaultMinuteHeight = 2.4;
    private const double MinMinuteHeight = 0.5;
    private const double MaxMinuteHeight = 8;
    // Smaller changes in minute height are no zoom at all.
    private const double ZoomTolerance = 0.001;
    private const double ZoomStep = 1.4;
    private const double WheelZoomStep = 1.15;
    private const double ZoomMilliseconds = 240;
    // Wide enough for the hour labels and, beside the cards, your route.
    private const double RulerWidth = 80;
    private const double MaxContentWidth = 860;
    private const double SideMargin = 16;
    // From this width the topics wrap; narrower screens swipe through them in one row.
    private const double WideLayout = 720;
    // The overview rail and a gap before it, kept clear on the right of the timeline.
    private const double RailSpace = OverviewRail.RailWidth + 6;
    private const double TopPadding = 4;
    private const double BottomPadding = 16;
    private const double DayHeaderHeight = 44;
    private const double DayGap = 28;
    private const double LaneGap = 8;
    private const double MinLaneWidth = 100;
    // The gap a card keeps from the times it starts and ends.
    private const double CardInset = 2;
    private const double OverflowSliceMinutes = 15;
    private const double OverflowStripWidth = 44;
    // During the conference the timeline opens with the current time this far down the screen.
    private const double NowFromTop = 0.3;
    private const double MaxTextScale = 1.6;
    private const int BackgroundLayoutThreshold = 5000;
    // Spare cards for zooming out: three times those on screen, within these bounds, built a batch at a time.
    private const int MinPoolSize = 150;
    private const int MaxPoolSize = 600;
    private const int PoolBatch = 12;
    // What the status line says.
    private const int StatusNone = 0;
    private const int StatusLive = 1;
    private const int StatusWrapped = 2;
    private const int StatusNext = 3;
    private const int StatusStarts = 4;
    private static readonly TimeSpan ClockInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NowGlideLimit = TimeSpan.FromMinutes(2);
    private static readonly CubicEaseOut EaseOut = new();

    private readonly ThemeVariantScope scope;
    private readonly LayoutTransformControl textScaler;
    private readonly Grid root;
    private readonly Border brandGlow;
    private readonly WrapPanel chips;
    private readonly ScrollViewer chipScroller;
    private readonly TextBlock summary;
    private readonly TextBlock status;
    private readonly Ellipse statusDot;
    private readonly StackPanel zoomButtons;
    private readonly RelayCommand zoomOutCommand;
    private readonly RelayCommand zoomInCommand;
    private readonly ScrollViewer scroller;
    private readonly Canvas canvas;
    private readonly TextBlock message;
    private readonly TimelineBackdrop backdrop = new();
    private readonly RouteLayer route = new();
    private readonly OverviewRail rail;
    private readonly Style[] motion;
    private readonly RealizedLayer<int, DayHeader> headers;
    private readonly RealizedLayer<(int Day, int From, int To, int Columns), OverflowMarker> overflow;
    private readonly Dictionary<int, SessionCard> cards = [];
    private readonly Stack<SessionCard> pool = new();
    private readonly HashSet<int> placed = [];
    private readonly List<int> stale = [];
    private readonly List<(int Day, double From, double To, TimeSpan Overlap)> bands = [];
    private readonly HashSet<string> selectedTopics = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> topicIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TopicFilterViewModel> watchedTopics = [];

    private IReadOnlyList<SessionViewModel> sessions = [];
    private IReadOnlyList<TopicFilterViewModel> topics = [];
    private ICommand? clearTopicsCommand;
    private PlannerTheme? currentTheme;
    private double bottomInset;
    private INotifyCollectionChanged? watchedTopicList;
    private bool attached;

    // The timeline as last built, and the sessions its item numbers index.
    private TimelineLayout? layout;
    private IReadOnlyList<SessionViewModel> layoutSessions = [];
    private TimeZoneInfo? layoutZone;
    private int buildVersion;
    private bool rebuildQueued;
    private (int Day, double Offset, bool Conflict)[] savedMarks = [];

    private double[] dayTops = [];
    private double minuteHeight = DefaultMinuteHeight;
    private double contentLeft;
    private double contentWidth;
    private double cardsLeft;
    private double cardsWidth;
    private int matchCount;

    private NowIndicator? nowIndicator;
    private (int Day, double Offset)? nowPosition;
    private DateTimeOffset? nowShownAt;
    private IDisposable? clockTimer;
    private int statusKind = StatusNone;

    private double? pinchStart;
    private (double From, double To, double Anchor, long Started)? zoomGlide;
    private bool zoomFrameRequested;
    private double textScale = 1;
    private bool reduceMotion;
    private bool warming;
    private bool entrancePlayed;
    private bool scrolledToNow;
    private bool focusFromKeyboard;

    public PlannerView()
    {
        motion =
        [
            Pulse(x => x.OfType<Border>().Class("glow").Class("conflict"), 0.25, 900),
            Pulse(x => x.OfType<Ellipse>().Class("live-dot"), 0.3, 700),
        ];

        chips = new WrapPanel { Margin = new Thickness(SideMargin, 16, SideMargin, 8) };
        summary = new TextBlock { FontFamily = PlannerTheme.BodyFont, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        statusDot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        statusDot.Classes.Add("live-dot");
        status = new TextBlock { FontFamily = PlannerTheme.BodyFont, FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        // A live region, so screen readers hear the status when it changes; see UpdateStatus.
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        zoomButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0) };
        var statusBar = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)],
            Margin = new Thickness(SideMargin + 4, 0, SideMargin, 12),
        };
        var state = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { statusDot, status } };
        Grid.SetColumn(state, 1);
        Grid.SetColumn(zoomButtons, 2);
        statusBar.Children.AddRange([summary, state, zoomButtons]);

        message = new TextBlock { FontFamily = PlannerTheme.BodyFont, Margin = new Thickness(SideMargin + 8, 24), IsVisible = false };
        canvas = new TimelineCanvas { Background = Brushes.Transparent };
        // Cards take their tab order from their place on the timeline, not from the pool they were recycled from.
        KeyboardNavigation.SetTabNavigation(canvas, KeyboardNavigationMode.Local);
        canvas.Children.AddRange([backdrop, route, message]);
        headers = new RealizedLayer<int, DayHeader>(canvas);
        overflow = new RealizedLayer<(int, int, int, int), OverflowMarker>(canvas);
        scroller = new ScrollViewer
        {
            Content = canvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            // The overview rail takes the scrollbar's place.
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        scroller.GestureRecognizers.Add(new PinchGestureRecognizer());
        rail = new OverviewRail(scroller) { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4) };
        zoomOutCommand = new RelayCommand(() => ZoomBy(1 / ZoomStep, scroller.Viewport.Height / 2),
            () => minuteHeight > MinMinuteHeight + ZoomTolerance);
        zoomInCommand = new RelayCommand(() => ZoomBy(ZoomStep, scroller.Viewport.Height / 2),
            () => minuteHeight < MaxMinuteHeight - ZoomTolerance);
        brandGlow = new Border { IsHitTestVisible = false };

        chipScroller = new ScrollViewer
        {
            Content = chips,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        root = new Grid { RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(1, GridUnitType.Star)] };
        Grid.SetRowSpan(brandGlow, 2);
        Grid.SetRow(statusBar, 1);
        Grid.SetRow(scroller, 2);
        Grid.SetRow(rail, 2);
        root.Children.AddRange([brandGlow, chipScroller, statusBar, scroller, rail]);
        // Scales the whole planner, text and layout together, for larger system text sizes.
        textScaler = new LayoutTransformControl { Child = root };
        scope = new ThemeVariantScope { Child = textScaler };
        Content = scope;
    }

    /// <summary>
    /// Where the current time comes from. The app keeps the system clock; another one moves the planner into the
    /// conference days while debugging.
    /// </summary>
    internal TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>
    /// Paints the planner in the app's colors. The timeline is built again in them.
    /// </summary>
    public void SetTheme(PlannerTheme theme)
    {
        currentTheme = theme;
        scope.RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        // Painted here rather than left to the host, which is plain white behind the planner on iOS.
        root.Background = new ImmutableSolidColorBrush(theme.Background);
        // A soft brand glow behind the filters, like the hero on the home page. It fades out above the timeline, so
        // a pinned day header sits on the plain background.
        brandGlow.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(PlannerTheme.WithAlpha(theme.Lavender, theme.IsDark ? 0.10 : 0.22), 0),
                new GradientStop(PlannerTheme.WithAlpha(theme.Lavender, 0), 1),
            },
        };
        message.Foreground = new ImmutableSolidColorBrush(theme.Secondary);
        rail.SetTheme(theme);
        route.SetTheme(theme);
        backdrop.SetTheme(theme);
        var zoomOut = new RoundButton("−", "Zoom out", theme) { Command = zoomOutCommand };
        var zoomIn = new RoundButton("+", "Zoom in", theme) { Command = zoomInCommand };
        AutomationProperties.SetHelpText(zoomOut, "Shows more of the day at once");
        AutomationProperties.SetHelpText(zoomIn, "Shows more detail for each session");
        zoomButtons.Children.Clear();
        zoomButtons.Children.AddRange([zoomOut, zoomIn]);
        // Cards are drawn in the old colors; start over with the new ones.
        ClearTimeline();
        foreach (var card in pool)
            canvas.Children.Remove(card);
        pool.Clear();
        QueueRebuild();
    }

    /// <summary>
    /// How much of the bottom edge a tab bar covers, so the end of the overview rail stays above it.
    /// </summary>
    public void SetBottomInset(double value)
    {
        bottomInset = value;
        rail.Margin = new Thickness(0, 4, 0, 4 + value / textScale);
        Relayout();
    }

    /// <summary>
    /// The saved sessions the timeline is built from, as a snapshot rebuilt whenever the schedule changes.
    /// </summary>
    public void SetSessions(IReadOnlyList<SessionViewModel>? value)
    {
        sessions = value ?? [];
        QueueRebuild();
    }

    /// <summary>
    /// The topic filters, shown as chips above the timeline.
    /// </summary>
    public void SetTopics(IReadOnlyList<TopicFilterViewModel>? value)
    {
        Unwatch();
        topics = value ?? [];
        Watch();
        QueueRebuild();
    }

    /// <summary>
    /// Clears the topic filters, for the All topics chip.
    /// </summary>
    public void SetClearTopicsCommand(ICommand? command)
    {
        clearTopicsCommand = command;
        if (chips.Children.FirstOrDefault() is TopicChip allTopics)
            allTopics.Command = command;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        AddInputHandlers();
        Watch();
        ApplyAccessibility();
        clockTimer = DispatcherTimer.Run(OnClockTick, ClockInterval);
        // A pending rebuild runs now, so the first frame on screen already has the chips and the timeline.
        if (rebuildQueued)
        {
            rebuildQueued = false;
            _ = RebuildAsync();
            return;
        }
        // Catches up with anything that changed while it was away, keeping the scroll position.
        foreach (var card in cards.Values)
            card.Update();
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        clockTimer?.Dispose();
        clockTimer = null;
        // A zoom glide cut short by leaving lands where it was heading.
        if (zoomGlide is { } glide)
        {
            zoomGlide = null;
            ZoomTo(glide.To, glide.Anchor);
        }
        Unwatch();
        RemoveInputHandlers();
        attached = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        chipScroller.HorizontalScrollBarVisibility = scroller.Bounds.Width >= WideLayout ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden;
        Relayout();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            return;
        var factor = e.Key switch
        {
            Key.OemPlus or Key.Add => ZoomStep,
            Key.OemMinus or Key.Subtract => 1 / ZoomStep,
            Key.D0 or Key.NumPad0 => DefaultMinuteHeight / (zoomGlide?.To ?? minuteHeight),
            _ => 0,
        };
        if (factor > 0)
        {
            ZoomBy(factor, scroller.Viewport.Height / 2);
            e.Handled = true;
        }
    }

    // Its own controls go with it, but each handler is still added on screen and removed off it.
    private void AddInputHandlers()
    {
        scroller.ScrollChanged += OnScrolled;
        scroller.AddHandler(InputElement.PinchEvent, OnPinch);
        scroller.AddHandler(InputElement.PinchEndedEvent, OnPinchEnded);
        scroller.AddHandler(InputElement.PointerTouchPadGestureMagnifyEvent, OnTouchPadMagnify);
        canvas.AddHandler(GotFocusEvent, OnCanvasGotFocus);
        canvas.AddHandler(RequestBringIntoViewEvent, OnRequestBringIntoView);
        // Tunnels, so Ctrl + wheel zooms before the scroller takes the wheel to scroll.
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
    }

    private void RemoveInputHandlers()
    {
        scroller.ScrollChanged -= OnScrolled;
        scroller.RemoveHandler(InputElement.PinchEvent, OnPinch);
        scroller.RemoveHandler(InputElement.PinchEndedEvent, OnPinchEnded);
        scroller.RemoveHandler(InputElement.PointerTouchPadGestureMagnifyEvent, OnTouchPadMagnify);
        canvas.RemoveHandler(GotFocusEvent, OnCanvasGotFocus);
        canvas.RemoveHandler(RequestBringIntoViewEvent, OnRequestBringIntoView);
        RemoveHandler(PointerWheelChangedEvent, OnWheel);
    }

    // The view models outlive this view, so it listens to them only while it is on screen, and stops listening to
    // exactly the instances it started with.
    private void Watch()
    {
        if (!attached)
            return;
        watchedTopicList = topics as INotifyCollectionChanged;
        if (watchedTopicList is not null)
            watchedTopicList.CollectionChanged += OnTopicsChanged;
        watchedTopics.AddRange(topics);
        foreach (var topic in watchedTopics)
            topic.PropertyChanged += OnTopicChanged;
        foreach (var card in cards.Values)
            card.Session!.PropertyChanged += OnSessionChanged;
    }

    private void Unwatch()
    {
        if (!attached)
            return;
        if (watchedTopicList is not null)
            watchedTopicList.CollectionChanged -= OnTopicsChanged;
        foreach (var topic in watchedTopics)
            topic.PropertyChanged -= OnTopicChanged;
        foreach (var card in cards.Values)
            card.Session!.PropertyChanged -= OnSessionChanged;
        watchedTopicList = null;
        watchedTopics.Clear();
    }

    // Read when the planner comes on screen and with each clock tick, so changes in the system settings apply
    // while it is showing.
    private void ApplyAccessibility()
    {
        var scale = ReadTextScale();
        if (scale != textScale)
        {
            textScale = scale;
            textScaler.LayoutTransform = scale > 1 ? new ScaleTransform(scale, scale) : null;
            SetBottomInset(bottomInset);
        }
        reduceMotion = ReadReduceMotion();
        nowIndicator?.SetRipple(!reduceMotion);
        foreach (var style in motion)
        {
            if (reduceMotion)
                Styles.Remove(style);
            else if (!Styles.Contains(style))
                Styles.Add(style);
        }
    }

    private bool OnClockTick()
    {
        ApplyAccessibility();
        // Only the clock moves the now marker with a glide; zooming and resizing place it at once.
        UpdateNow(glide: !reduceMotion);
        return true;
    }

    // Topics arrive one by one while the agenda loads, so the timeline is rebuilt once they have settled.
    private void QueueRebuild()
    {
        if (rebuildQueued)
            return;
        rebuildQueued = true;
        Dispatcher.Post(() =>
        {
            // Already done if the planner was attached in the meantime.
            if (!rebuildQueued)
                return;
            rebuildQueued = false;
            _ = RebuildAsync();
        });
    }

    // Starts the timeline over; a newer rebuild makes an unfinished one give up.
    private async Task RebuildAsync()
    {
        var version = ++buildVersion;
        ClearTimeline();
        if (currentTheme is not { } theme)
            return;
        try
        {
            BuildChips(theme);
            // Large agendas take a moment to lay out, so they are laid out off the UI thread; a conference-sized one
            // is ready in the same frame, so the timeline never flashes empty.
            var list = sessions;
            var models = list.Select(session => session.Model).ToArray();
            var timeZone = TimeZoneInfo.Local;
            var built = models.Length > BackgroundLayoutThreshold
                ? await Task.Run(() => TimelineLayout.Build(models, timeZone))
                : TimelineLayout.Build(models, timeZone);
            if (version != buildVersion)
                return;
            layout = built;
            layoutSessions = list;
            layoutZone = timeZone;
            rail.SetBusy(built.Busy);
            nowIndicator = new NowIndicator(theme, RulerWidth) { IsVisible = false };
            nowIndicator.SetRipple(!reduceMotion);
            canvas.Children.Add(nowIndicator);
            Refresh();
        }
        catch (Exception exception)
        {
            // Nothing here is expected to fail, but if it does the planner says so instead of failing silently.
            Trace.TraceError($"The schedule planner couldn't be built: {exception}");
            if (version != buildVersion)
                return;
            ClearTimeline();
            ShowMessage("The timeline couldn't be shown. The List view has your saved sessions.");
        }
    }

    private void BuildChips(PlannerTheme theme)
    {
        topicIndex.Clear();
        chips.Children.Clear();
        chips.Children.Add(new TopicChip("All topics", theme.Accent, theme)
        {
            IsChecked = topics.All(topic => !topic.IsSelected),
            Command = clearTopicsCommand,
        });
        for (var i = 0; i < topics.Count; i++)
        {
            topicIndex[topics[i].Name] = i;
            chips.Children.Add(new TopicChip(topics[i].Name, theme.TopicColor(i), theme)
            {
                IsChecked = topics[i].IsSelected,
                Command = topics[i].ToggleCommand,
                Tag = topics[i],
            });
        }
    }

    private void ClearTimeline()
    {
        foreach (var item in cards.Keys.ToList())
            Recycle(item);
        headers.Clear();
        overflow.Clear();
        backdrop.Clear();
        bands.Clear();
        savedMarks = [];
        dayTops = [];
        if (nowIndicator is not null)
            canvas.Children.Remove(nowIndicator);
        nowIndicator = null;
        nowPosition = null;
        nowShownAt = null;
        layout = null;
        layoutSessions = [];
        layoutZone = null;
        route.Reset();
        rail.SetBusy([]);
        rail.SetScale([], minuteHeight);
        ShowMessage(null);
    }

    private void ShowMessage(string? text)
    {
        message.Text = text;
        message.IsVisible = text is not null;
    }

    // Brings the whole timeline up to date with the filters, the saved sessions and the time.
    private void Refresh()
    {
        UpdateFilter();
        UpdateSchedule();
        UpdateNow();
        Relayout();
    }

    // Lays the days out at the current zoom and width.
    private void Relayout()
    {
        var width = scroller.Bounds.Width;
        if (width <= 0 || layout is not { } timeline)
            return;
        contentWidth = Math.Min(MaxContentWidth, width - SideMargin - RailSpace);
        // Centered, unless that would put the timeline under the overview rail.
        contentLeft = Math.Min((width - contentWidth) / 2, width - RailSpace - contentWidth);
        cardsLeft = contentLeft + RulerWidth;
        cardsWidth = contentWidth - RulerWidth;
        dayTops = new double[timeline.Days.Count];
        var dayStarts = new double[timeline.Days.Count];
        var y = TopPadding;
        for (var day = 0; day < timeline.Days.Count; day++)
        {
            dayTops[day] = y;
            dayStarts[day] = y + DayHeaderHeight;
            y += DayHeaderHeight + timeline.Days[day].Length * minuteHeight;
            if (day + 1 < timeline.Days.Count)
                y += DayGap;
        }
        canvas.Width = width;
        canvas.Height = y + BottomPadding + bottomInset / textScale;
        Place(backdrop, 0, 0, width, canvas.Height);
        backdrop.Column = (contentLeft, contentWidth, RulerWidth);
        Place(route, cardsLeft - RouteLayer.GutterWidth, 0, RouteLayer.GutterWidth, canvas.Height);
        route.SetScale(dayStarts, minuteHeight);
        rail.SetScale(dayStarts, minuteHeight);
        PlaceNowIndicator(glide: false);
        Realize();
    }

    // Creates controls only for what is on screen, plus half a screen above and below for smooth scrolling.
    private void Realize()
    {
        if (layout is not { } timeline || currentTheme is not { } theme || dayTops.Length == 0)
            return;
        var buffer = scroller.Viewport.Height / 2;
        var from = scroller.Offset.Y - buffer;
        var to = scroller.Offset.Y + scroller.Viewport.Height + buffer;
        var columns = Math.Clamp((int)((cardsWidth + LaneGap) / (MinLaneWidth + LaneGap)), 1, TimelineLayout.MaxColumns);
        // Each overflow marker covers a slice of the day, inset like a card; zoomed out, slices merge so the markers
        // never overlap.
        var sliceMinutes = (int)(OverflowSliceMinutes * Math.Ceiling((OverflowMarker.MarkerHeight + 2 * CardInset) / (OverflowSliceMinutes * minuteHeight)));
        placed.Clear();
        headers.Begin();
        overflow.Begin();
        backdrop.Clear();
        for (var day = FirstDayEndingAfter(from); day < timeline.Days.Count && dayTops[day] <= to; day++)
            RealizeDay(timeline, theme, day, from, to, columns, sliceMinutes);
        foreach (var (day, start, end, overlap) in bands)
            backdrop.Bands.Add((DayTop(day) + start * minuteHeight, (end - start) * minuteHeight, overlap));
        headers.End();
        var focusedMarker = KeepFocusedMarker(timeline, theme, columns, sliceMinutes);
        overflow.End();
        KeepFocusedRealized(timeline, theme, columns, focusedMarker?.FirstItem ?? -1);
        RecycleStale();
        PlayEntrance();
        WarmPool();
    }

    // One day's header, hours, breaks, cards and overflow counts between two points on the canvas.
    private void RealizeDay(TimelineLayout timeline, PlannerTheme theme, int day, double from, double to, int columns, int sliceMinutes)
    {
        var timelineDay = timeline.Days[day];
        var top = DayTop(day);
        var scrollTop = scroller.Offset.Y;
        // The day at the top of the screen keeps its header there until its last sessions push it up.
        var pinned = scrollTop > dayTops[day] && (day + 1 == dayTops.Length || scrollTop < dayTops[day + 1]);
        var pushedAt = top + timelineDay.Length * minuteHeight - DayHeaderHeight;
        var header = headers.Show(day, () => new DayHeader(timelineDay, theme), contentLeft,
            pinned ? Math.Min(scrollTop, pushedAt) : dayTops[day], contentWidth, DayHeaderHeight + DayHeader.FadeHeight);
        header.SetPinned(pinned, Math.Min(scrollTop - dayTops[day], pushedAt - scrollTop) / DayHeader.FadeHeight);
        header.NaturalTop = dayTops[day];

        var fromMinute = (from - top) / minuteHeight;
        var toMinute = (to - top) / minuteHeight;
        foreach (var (offset, hour) in timeline.DayHours[day])
        {
            if (offset >= fromMinute && offset <= toMinute)
                backdrop.Hours.Add((top + offset * minuteHeight, hour));
        }
        foreach (var gap in timelineDay.Breaks)
        {
            if (gap.Offset + Timeline.BreakLength >= fromMinute && gap.Offset <= toMinute)
                backdrop.Breaks.Add((top + gap.Offset * minuteHeight, Timeline.BreakLength * minuteHeight, gap.End - gap.Start));
        }
        foreach (var item in timeline.ShownBetween(day, fromMinute, toMinute, columns))
            PlaceCard(timeline, theme, item, day, columns);

        // Sessions past the lanes that fit are counted on markers in the last column.
        for (var slice = Math.Max(0, (int)Math.Floor(fromMinute / sliceMinutes)); slice * sliceMinutes <= Math.Min(toMinute, timelineDay.Length); slice++)
        {
            if (timeline.HiddenBetween(day, slice * sliceMinutes, (slice + 1) * sliceMinutes, columns) > 0)
                ShowMarker(timeline, theme, (day, slice * sliceMinutes, (slice + 1) * sliceMinutes, columns));
        }
    }

    private OverflowMarker ShowMarker(TimelineLayout timeline, PlannerTheme theme, (int Day, int From, int To, int Columns) slice)
    {
        // A single column has no last column to spare, so the markers take the strip its overlapping cards leave.
        var width = slice.Columns == 1 ? OverflowStripWidth : (cardsWidth - LaneGap * (slice.Columns - 1)) / slice.Columns;
        var marker = overflow.Show(slice, () => NewMarker(timeline, theme, slice),
            cardsLeft + cardsWidth - width, DayTop(slice.Day) + slice.From * minuteHeight + CardInset, width, OverflowMarker.MarkerHeight);
        marker.Fit(width);
        return marker;
    }

    private OverflowMarker NewMarker(TimelineLayout timeline, PlannerTheme theme, (int Day, int From, int To, int Columns) slice)
    {
        var items = timeline.HiddenItemsBetween(slice.Day, slice.From, slice.To, slice.Columns).ToArray();
        return new OverflowMarker(items[0], [.. items.Select(item => layoutSessions[timeline.Sessions[item]])], Matches, theme);
    }

    // A focused marker stays realized like a focused card, unless zooming or resizing has replaced its slice.
    private OverflowMarker? KeepFocusedMarker(TimelineLayout timeline, PlannerTheme theme, int columns, int sliceMinutes)
    {
        foreach (var (slice, marker) in overflow.Realized)
        {
            if (marker.IsKeyboardFocusWithin && slice.Columns == columns && slice.To - slice.From == sliceMinutes)
                return ShowMarker(timeline, theme, slice);
        }
        return null;
    }

    // The focused card or marker and the matching sessions either side of it stay realized, even off screen: scrolling
    // with the wheel doesn't drop keyboard focus, and Tab always has the next session to go to, past filtered-out
    // cards, which can't take focus.
    private void KeepFocusedRealized(TimelineLayout timeline, PlannerTheme theme, int columns, int focusedMarkerItem)
    {
        var focused = -1;
        foreach (var (item, card) in cards)
        {
            if (card.IsKeyboardFocusWithin)
                focused = item;
        }
        if (focused >= 0 && TimelineLayout.IsHidden(timeline.Lanes[focused], timeline.LaneCounts[focused], columns))
            return;
        var anchor = focused >= 0 ? focused : focusedMarkerItem;
        if (anchor < 0)
            return;
        var (previous, next) = timeline.ShownNeighbours(anchor, columns, item => Matches(layoutSessions[timeline.Sessions[item]]));
        foreach (var item in (ReadOnlySpan<int>)[focused, previous, next])
        {
            if (item >= 0 && !placed.Contains(item))
                PlaceCard(timeline, theme, item, timeline.DayOf(item), columns);
        }
    }

    private void RecycleStale()
    {
        stale.Clear();
        foreach (var item in cards.Keys)
        {
            if (!placed.Contains(item))
                stale.Add(item);
        }
        foreach (var item in stale)
            Recycle(item);
    }

    private void PlaceCard(TimelineLayout timeline, PlannerTheme theme, int item, int day, int columns)
    {
        var lanes = Math.Min(timeline.LaneCounts[item], columns);
        var width = columns == 1 && timeline.LaneCounts[item] > 1 ? cardsWidth - OverflowStripWidth - LaneGap : cardsWidth;
        var laneWidth = (width - LaneGap * (lanes - 1)) / lanes;
        var height = (timeline.Ends[item] - timeline.Starts[item]) * minuteHeight - 2 * CardInset;
        var card = Realized(timeline, theme, item);
        Place(card, cardsLeft + timeline.Lanes[item] * (laneWidth + LaneGap), DayTop(day) + timeline.Starts[item] * minuteHeight + CardInset,
            laneWidth, height);
        card.SetSize(laneWidth, height);
        placed.Add(item);
    }

    private SessionCard Realized(TimelineLayout timeline, PlannerTheme theme, int item)
    {
        if (cards.TryGetValue(item, out var card))
            return card;
        var session = layoutSessions[timeline.Sessions[item]];
        if (!pool.TryPop(out card))
        {
            card = new SessionCard(theme);
            canvas.Children.Add(card);
        }
        var topic = session.Model.Topics.Count == 0 ? 0 : topicIndex.GetValueOrDefault(session.Model.Topics[0]);
        card.Bind(session, theme.TopicColor(topic), !Matches(session), TimeState(session.Model, Clock.GetUtcNow()));
        // Items are numbered by day and start time; the heart shares its card's number, so it follows the card.
        KeyboardNavigation.SetTabIndex(card, item);
        KeyboardNavigation.SetTabIndex(card.Heart, item);
        card.IsVisible = true;
        cards[item] = card;
        if (attached)
            session.PropertyChanged += OnSessionChanged;
        return card;
    }

    private void Recycle(int item)
    {
        var card = cards[item];
        cards.Remove(item);
        if (attached)
            card.Session!.PropertyChanged -= OnSessionChanged;
        card.IsVisible = false;
        pool.Push(card);
    }

    // Zooming out can need several times more cards at once. Spare ones are built a few at a time while the
    // UI is idle, so a pinch only rebinds cards instead of creating them mid-gesture.
    private void WarmPool()
    {
        if (warming || currentTheme is not { } theme)
            return;
        var target = Math.Min(layout?.Count ?? 0, Math.Clamp(cards.Count * 3, MinPoolSize, MaxPoolSize));
        if (cards.Count + pool.Count >= target)
            return;
        warming = true;
        Dispatcher.Post(() =>
        {
            warming = false;
            // A new theme has emptied the pool in the meantime; it warms again in the new colors.
            if (!ReferenceEquals(theme, currentTheme))
                return;
            for (var i = 0; i < PoolBatch && cards.Count + pool.Count < target; i++)
            {
                var card = new SessionCard(theme) { IsVisible = false };
                canvas.Children.Add(card);
                pool.Push(card);
            }
            WarmPool();
        }, DispatcherPriority.Background);
    }

    // The timeline arrives as one piece: day headers, ruler and cards fade in and settle together.
    private void PlayEntrance()
    {
        if (entrancePlayed || reduceMotion || cards.Count == 0 || TopLevel.GetTopLevel(this) is null)
            return;
        entrancePlayed = true;
        canvas.RenderTransform = new TranslateTransform();
        _ = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(420),
            Easing = EaseOut,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, 16d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } },
            },
        }.RunAsync(canvas);
    }

    private void OnScrolled(object? sender, ScrollChangedEventArgs e)
    {
        // A wider or narrower timeline, as after a text size change, is laid out again; scrolling only realizes.
        if (e.ViewportDelta.X != 0)
            Relayout();
        else
            Realize();
        rail.InvalidateVisual();
    }

    private void OnCanvasGotFocus(object? sender, FocusChangedEventArgs e)
    {
        focusFromKeyboard = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
        // Focus moving to a card already on screen doesn't scroll, so its neighbours are realized here for the next Tab.
        if (focusFromKeyboard)
            Realize();
    }

    // Keyboard focus brings a card into view with room above for the pinned day header and below for a tab bar drawn
    // over the timeline, so the focused card is never left underneath either. Taps and screen readers keep the default.
    private void OnRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        if (!focusFromKeyboard || e.TargetObject is not InputElement { IsFocused: true })
            return;
        var above = DayHeaderHeight + DayHeader.FadeHeight;
        var rect = e.TargetRect;
        e.TargetRect = new Rect(rect.X, rect.Y - above, rect.Width, rect.Height + above + bottomInset);
    }

    private void OnPinch(object? sender, PinchEventArgs e)
    {
        zoomGlide = null;
        pinchStart ??= minuteHeight;
        ZoomTo(pinchStart.Value * e.Scale, e.ScaleOrigin.Y);
        e.Handled = true;
    }

    private void OnPinchEnded(object? sender, PinchEndedEventArgs e) => pinchStart = null;

    private void OnTouchPadMagnify(object? sender, PointerDeltaEventArgs e)
    {
        zoomGlide = null;
        ZoomTo(minuteHeight * (1 + e.Delta.X), e.GetPosition(scroller).Y);
        e.Handled = true;
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            return;
        ZoomBy(Math.Pow(WheelZoomStep, e.Delta.Y), e.GetPosition(scroller).Y);
        e.Handled = true;
    }

    // Steps from the buttons, keys and wheel glide to the new zoom instead of jumping, keeping the same moment under
    // the anchor; another step on the way carries on from where the glide was heading.
    private void ZoomBy(double factor, double anchor)
    {
        var target = Math.Clamp((zoomGlide?.To ?? minuteHeight) * factor, MinMinuteHeight, MaxMinuteHeight);
        if (reduceMotion || TopLevel.GetTopLevel(this) is null)
        {
            zoomGlide = null;
            ZoomTo(target, anchor);
            return;
        }
        zoomGlide = (minuteHeight, target, anchor, Stopwatch.GetTimestamp());
        RequestZoomFrame();
    }

    private void RequestZoomFrame()
    {
        if (zoomFrameRequested || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;
        zoomFrameRequested = true;
        topLevel.RequestAnimationFrame(OnZoomFrame);
    }

    private void OnZoomFrame(TimeSpan time)
    {
        zoomFrameRequested = false;
        if (zoomGlide is not { } glide)
            return;
        var progress = Math.Min(1, Stopwatch.GetElapsedTime(glide.Started).TotalMilliseconds / ZoomMilliseconds);
        // On a log scale, so zooming in and out move at the same pace.
        ZoomTo(glide.From * Math.Pow(glide.To / glide.From, EaseOut.Ease(progress)), glide.Anchor);
        if (progress < 1)
            RequestZoomFrame();
        else
            zoomGlide = null;
    }

    /// <summary>
    /// Changes how tall a minute is, keeping the moment at <paramref name="anchor"/> (from the top of the
    /// visible timeline) where it was.
    /// </summary>
    private void ZoomTo(double value, double anchor)
    {
        value = Math.Clamp(value, MinMinuteHeight, MaxMinuteHeight);
        if (layout is null || dayTops.Length == 0 || Math.Abs(value - minuteHeight) < ZoomTolerance)
            return;
        var y = scroller.Offset.Y + anchor;
        var day = Math.Min(FirstDayEndingAfter(y), dayTops.Length - 1);
        var minute = (y - DayTop(day)) / minuteHeight;
        minuteHeight = value;
        Relayout();
        scroller.Offset = new Vector(0, Math.Max(0, DayTop(day) + minute * minuteHeight - anchor));
        zoomOutCommand.NotifyCanExecuteChanged();
        zoomInCommand.NotifyCanExecuteChanged();
    }

    private void UpdateFilter()
    {
        selectedTopics.Clear();
        foreach (var topic in topics.Where(topic => topic.IsSelected))
            selectedTopics.Add(topic.Name);
        matchCount = selectedTopics.Count == 0 ? layoutSessions.Count : layoutSessions.Count(Matches);
        foreach (var card in cards.Values)
            card.SetDimmed(!Matches(card.Session!));
        foreach (var chip in chips.Children.OfType<TopicChip>())
            chip.IsChecked = chip.Tag is TopicFilterViewModel topic ? topic.IsSelected : selectedTopics.Count == 0;
        UpdateStatus();
    }

    private bool Matches(SessionViewModel session) =>
        selectedTopics.Count == 0 || session.Model.Topics.Any(selectedTopics.Contains);

    // The saved sessions on the timeline: overlap bands behind them, your route beside them and marks on the rail.
    private void UpdateSchedule()
    {
        bands.Clear();
        savedMarks = [];
        if (layout is not { } timeline)
            return;
        foreach (var (start, end) in Agenda.OverlapRanges(layoutSessions.Select(session => session.Model)))
        {
            if (timeline.Locate(start) is { } place && timeline.Days[place.Day].OffsetOf(end) is { } until)
                bands.Add((place.Day, place.Offset, until, end - start));
        }

        // A saved session that starts before the one on the route has ended takes a branch beside it.
        var stops = new List<(int Day, double From, double To, bool Branch, bool Conflict)>(layoutSessions.Count);
        var (routeDay, routeEnd) = (-1, DateTimeOffset.MinValue);
        foreach (var session in layoutSessions.OrderBy(session => session.Model.StartsAt))
        {
            var model = session.Model;
            if (timeline.Locate(model.StartsAt) is not { } place || timeline.Days[place.Day].OffsetOf(model.EndsAt) is not { } end)
                continue;
            if (place.Day != routeDay)
                (routeDay, routeEnd) = (place.Day, DateTimeOffset.MinValue);
            var branch = model.StartsAt < routeEnd;
            if (!branch)
                routeEnd = model.EndsAt;
            stops.Add((place.Day, place.Offset, end, branch, session.HasConflict));
        }
        route.SetStops(stops);
        savedMarks = [.. stops.Select(stop => (stop.Day, stop.From, stop.Conflict))];
    }

    // Moves what follows the clock: the now marker, live and ended cards, and the status line.
    private void UpdateNow(bool glide = false)
    {
        var instant = Clock.GetUtcNow();
        foreach (var card in cards.Values)
            card.SetTime(TimeState(card.Session!.Model, instant));
        CheckTimeZone();
        nowPosition = layout?.Locate(instant);
        if (nowIndicator is not null)
        {
            // The marker glides on only from where it was a moment ago, not after a night between days or a long pause.
            glide &= nowIndicator.IsVisible && nowShownAt is { } shown && instant - shown < NowGlideLimit;
            nowIndicator.IsVisible = nowPosition is not null;
            nowShownAt = nowPosition is null ? null : instant;
            nowIndicator.SetTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.Local));
            PlaceNowIndicator(glide);
        }
        rail.SetMarks(savedMarks, nowPosition);
        UpdateStatus();
    }

    private void PlaceNowIndicator(bool glide)
    {
        if (nowIndicator is null || nowPosition is not { } position || position.Day >= dayTops.Length)
            return;
        Canvas.SetLeft(nowIndicator, contentLeft);
        nowIndicator.Width = contentWidth;
        nowIndicator.MoveTo(DayTop(position.Day) + position.Offset * minuteHeight - NowIndicator.MarkerHeight / 2, glide);
        if (!scrolledToNow)
            Dispatcher.Post(ScrollToNow, DispatcherPriority.Background);
    }

    // During the conference the timeline opens at the current time, once, after layout has sized the scroller.
    private void ScrollToNow()
    {
        if (scrolledToNow || nowIndicator is null || double.IsNaN(nowIndicator.Top) || scroller.Extent.Height <= scroller.Viewport.Height)
            return;
        scrolledToNow = true;
        scroller.Offset = new Vector(0, Math.Max(0, nowIndicator.Top - scroller.Viewport.Height * NowFromTop));
    }

    // The app refreshes the local time zone as its pages appear; days and hours follow when it changes.
    private void CheckTimeZone()
    {
        if (layoutZone is not null && !layoutZone.HasSameRules(TimeZoneInfo.Local))
            QueueRebuild();
    }

    private void UpdateStatus()
    {
        if (currentTheme is not { } theme)
            return;
        var overlapCount = layoutSessions.Count(session => session.HasConflict);
        summary.Inlines =
        [
            new Run($"{layoutSessions.Count:N0} saved") { FontWeight = FontWeight.SemiBold, Foreground = new ImmutableSolidColorBrush(theme.Text) },
        ];
        if (overlapCount > 0)
            summary.Inlines.Add(new Run($"  ·  {overlapCount:N0} overlapping") { FontWeight = FontWeight.SemiBold, Foreground = new ImmutableSolidColorBrush(theme.Conflict) });
        if (selectedTopics.Count > 0)
            summary.Inlines.Add(new Run($"  ·  {matchCount:N0} of {layoutSessions.Count:N0} match") { Foreground = new ImmutableSolidColorBrush(theme.Secondary) });

        var instant = Clock.GetUtcNow();
        var live = layout?.AnyLive(instant) ?? false;
        var next = layout?.NextStart(instant);
        statusDot.IsVisible = live;
        statusDot.Fill = new ImmutableSolidColorBrush(theme.Magenta);
        status.Foreground = new ImmutableSolidColorBrush(live ? theme.Magenta : theme.Secondary);
        var kind = layout is null ? StatusNone
            : live ? StatusLive
            : next is null ? StatusWrapped
            : layout.AnyStarted(instant) ? StatusNext
            : StatusStarts;
        var text = kind switch
        {
            StatusLive => "Live now",
            StatusWrapped => "That's a wrap",
            StatusNext => $"Next in {Agenda.Duration(next!.Value - instant)}",
            StatusStarts => $"Starts in {Agenda.Duration(next!.Value - instant)}",
            _ => "",
        };
        // Screen readers hear when the state changes, not every minute of the countdown. Avalonia's live regions don't
        // reach TalkBack and VoiceOver, so a session going live and the end of the conference are announced there.
        AutomationProperties.SetLiveSetting(status, kind != statusKind ? AutomationLiveSetting.Polite : AutomationLiveSetting.Off);
        if (kind != statusKind && statusKind != StatusNone && kind is StatusLive or StatusWrapped && attached)
            Announce(text);
        statusKind = kind;
        status.Text = text;
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Session times change when the app picks up a new time zone.
        if (e.PropertyName == nameof(SessionViewModel.TimeLabel))
            CheckTimeZone();
        if (sender is not SessionViewModel session || cards.Values.FirstOrDefault(card => card.Session == session) is not { } card)
            return;
        if (e.PropertyName is nameof(SessionViewModel.IsSaved) or nameof(SessionViewModel.HasConflict))
            card.Update();
    }

    private void OnTopicChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TopicFilterViewModel.IsSelected))
            UpdateFilter();
    }

    private void OnTopicsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Listens to the topics that were added, and no longer to those that were removed.
        Unwatch();
        Watch();
        QueueRebuild();
    }

    private double DayTop(int day) => dayTops[day] + DayHeaderHeight;

    private int FirstDayEndingAfter(double y)
    {
        var (low, high) = (0, dayTops.Length);
        while (low < high)
        {
            var middle = (low + high) / 2;
            var end = middle + 1 < dayTops.Length ? dayTops[middle + 1] : double.MaxValue;
            if (end <= y)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    private static (bool Live, bool Ended, double Elapsed) TimeState(Session session, DateTimeOffset instant) =>
        (instant >= session.StartsAt && instant < session.EndsAt,
            instant >= session.EndsAt,
            (instant - session.StartsAt) / (session.EndsAt - session.StartsAt));

    private static void Place(Control control, double x, double y, double width, double height)
    {
        Canvas.SetLeft(control, x);
        Canvas.SetTop(control, y);
        control.Width = Math.Max(0, width);
        control.Height = Math.Max(0, height);
    }

    // A gentle, endless fade between the dimmest opacity and full, for the controls the selector picks.
    private static Style Pulse(Func<Selector?, Selector> selector, double dimmest, int milliseconds) =>
        new(selector)
        {
            Animations =
            {
                new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(milliseconds),
                    IterationCount = IterationCount.Infinite,
                    PlaybackDirection = PlaybackDirection.Alternate,
                    Easing = new SineEaseInOut(),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, dimmest) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d) } },
                    },
                },
            },
        };

    // How much larger than standard the user wants text, which Avalonia doesn't report, capped so the timeline keeps its
    // shape. Linux and the browser use the default.
    private static double ReadTextScale()
    {
#if ANDROID
        var scale = Android.App.Application.Context.Resources?.Configuration?.FontScale ?? 1;
#elif IOS
        var scale = (double)UIKit.UIFontMetrics.DefaultMetrics.GetScaledValue(1);
#else
        var scale = 1.0;
#endif
        return Math.Clamp(scale, 1, MaxTextScale);
    }

    private static bool ReadReduceMotion()
    {
#if ANDROID
        // Android has no separate switch; turning animations off sets the animator scale to zero.
        return Android.Provider.Settings.Global.GetFloat(Android.App.Application.Context.ContentResolver,
            Android.Provider.Settings.Global.AnimatorDurationScale, 1) == 0;
#elif IOS
        return UIKit.UIAccessibility.IsReduceMotionEnabled;
#else
        return false;
#endif
    }

    private static void Announce(string text)
    {
#if ANDROID || IOS
        Microsoft.Maui.Accessibility.SemanticScreenReader.Default.Announce(text);
#endif
    }
}
