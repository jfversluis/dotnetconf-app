namespace DotNetConf.App.Controls;

public partial class SessionRow : DetailRow
{
    public static readonly BindableProperty ShowsSelectionSurfaceProperty = BindableProperty.Create(
        nameof(ShowsSelectionSurface), typeof(bool), typeof(SessionRow), false);
    public static readonly BindableProperty IsSaveSwipeReadyProperty = BindableProperty.Create(
        nameof(IsSaveSwipeReady), typeof(bool), typeof(SessionRow), false);

    public bool IsSaveSwipeReady
    {
        get => (bool)GetValue(IsSaveSwipeReadyProperty);
        private set => SetValue(IsSaveSwipeReadyProperty, value);
    }

    public bool ShowsSelectionSurface
    {
        get => (bool)GetValue(ShowsSelectionSurfaceProperty);
        set => SetValue(ShowsSelectionSurfaceProperty, value);
    }

    public SessionRow()
    {
        InitializeComponent();
#if IOS
        SessionSwipe.HandlerChanged += OnSwipeHandlerChanged;
#endif
#if AVALONIA
        // Native swipe handlers are not part of the experimental desktop/browser host.
        var content = SessionSwipe.Content;
        SessionSwipe.Content = null;
        Content = content;
#else
        RowActions.Children.Remove(MoreButton);
        RowActions.ColumnDefinitions.RemoveAt(2);
        SessionSwipe.SwipeStarted += OnSwipeStarted;
        SessionSwipe.SwipeChanging += OnSwipeChanging;
        SessionSwipe.SwipeEnded += (_, _) => ResetSaveSwipe();
        BindingContextChanged += (_, _) => ResetSaveSwipe();
        Unloaded += (_, _) => ResetSaveSwipe();
#endif
    }
#if !AVALONIA
    private void OnSwipeStarted(object? sender, SwipeStartedEventArgs e)
    {
        ResetSaveSwipe();
        // Use the same explicit distance for native execution and the ready feedback.
        SessionSwipe.Threshold = e.SwipeDirection == SwipeDirection.Right ? SessionSwipe.Width * 0.6 : 0;
    }

    private void OnSwipeChanging(object? sender, SwipeChangingEventArgs e)
    {
        var ready = e.SwipeDirection == SwipeDirection.Right && SessionSwipe.Threshold > 0
            && e.Offset >= SessionSwipe.Threshold
            && BindingContext is ViewModels.SessionViewModel session && session.ToggleSaveCommand.CanExecute(null);
        if (ready == IsSaveSwipeReady)
            return;

        IsSaveSwipeReady = ready;
        SaveSwipeIcon.AbortAnimation("SaveSwipeReady");
        var scale = ready ? 1.12 : 1;
        if (AskGlow.ReduceMotion)
            SaveSwipeIcon.Scale = 1;
        else
            SaveSwipeIcon.Animate("SaveSwipeReady", value => SaveSwipeIcon.Scale = value,
                SaveSwipeIcon.Scale, scale, length: 120, easing: Easing.CubicOut);

        if (ready && HapticFeedback.Default.IsSupported)
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
    }

    private void ResetSaveSwipe()
    {
        IsSaveSwipeReady = false;
        SaveSwipeIcon.AbortAnimation("SaveSwipeReady");
        SaveSwipeIcon.Scale = 1;
    }
#endif
#if IOS
    private void OnSwipeHandlerChanged(object? sender, EventArgs e)
    {
        if (SessionSwipe.Handler?.PlatformView is not Microsoft.Maui.Platform.MauiSwipeView nativeSwipe)
            return;
        // MAUI's swipe recognizers otherwise allow the row button to click when a swipe closes.
        foreach (var recognizer in nativeSwipe.GestureRecognizers ?? [])
            if (recognizer is UIKit.UIPanGestureRecognizer or UIKit.UITapGestureRecognizer)
                recognizer.CancelsTouchesInView = true;
    }
#endif
}
