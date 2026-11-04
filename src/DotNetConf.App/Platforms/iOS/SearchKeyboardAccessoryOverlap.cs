using CoreFoundation;
using CoreGraphics;
using Foundation;
using UIKit;

namespace DotNetConf.App.Platforms.iOS;

internal sealed class SearchKeyboardAccessoryOverlap
{
    private readonly Controls.AgendaSearchField search;
    private readonly Grid layout;
    private readonly double bottomMargin;
    private NSObject? frameObserver;
    private NSObject? hideObserver;
    private CGRect keyboardFrame;
    private double extension;

    public SearchKeyboardAccessoryOverlap(Controls.AgendaSearchField search, Grid layout)
    {
        this.search = search;
        this.layout = layout;
        bottomMargin = layout.Margin.Bottom;
        layout.Loaded += OnLoaded;
        layout.Unloaded += OnUnloaded;
        layout.SizeChanged += (_, _) => Update();
        search.SearchFocusChanged += (_, _) => Update();
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (frameObserver is not null || !OperatingSystem.IsIOSVersionAtLeast(26))
            return;

        frameObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIKeyboard.WillChangeFrameNotification, notification =>
        {
            keyboardFrame = notification.UserInfo?[UIKeyboard.FrameEndUserInfoKey] is NSValue value
                ? value.CGRectValue : CGRect.Empty;
            DispatchQueue.MainQueue.DispatchAsync(Update);
        });
        hideObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIKeyboard.WillHideNotification, _ =>
        {
            keyboardFrame = CGRect.Empty;
            SetExtension(0);
        });
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        keyboardFrame = CGRect.Empty;
        SetExtension(0);
        RemoveObserver(ref frameObserver);
        RemoveObserver(ref hideObserver);
    }

    private void Update()
    {
        var amount = 0d;
        if (layout.IsLoaded && search.IsSearchFocused && !keyboardFrame.IsEmpty &&
            search.NativeInputAccessory is { } accessory &&
            accessory.AccessibilityIdentifier == "DoneAccessory" &&
            !accessory.Subviews.Any(child => child is UIToolbar) &&
            layout.Handler?.PlatformView is UIView { Window: { } window } view)
        {
            var keyboard = window.ConvertRectFromCoordinateSpace(keyboardFrame, window.Screen.CoordinateSpace);
            // SizeChanged precedes the native frame update.
            var bounds = view.ConvertRectToView(new CGRect(0, 0, layout.Width, layout.Height), window);
            var naturalBottom = bounds.Bottom - extension + bottomMargin;

            // SoftInput includes MAUI's transparent floating accessory. Extend only the
            // list that ends at that inset, not a stacked pane above the keyboard.
            if (keyboard.Bottom >= window.Bounds.Bottom &&
                keyboard.Left < bounds.Right && keyboard.Right > bounds.Left &&
                Math.Abs(naturalBottom - keyboard.Top) <= 1)
                amount = Math.Min((double)accessory.Bounds.Height, (double)keyboard.Height);
        }
        SetExtension(amount);
    }

    private void SetExtension(double amount)
    {
        if (extension == amount)
            return;

        extension = amount;
        var margin = layout.Margin;
        layout.Margin = new(margin.Left, margin.Top, margin.Right, bottomMargin - amount);
    }

    private static void RemoveObserver(ref NSObject? observer)
    {
        if (observer is null)
            return;

        NSNotificationCenter.DefaultCenter.RemoveObserver(observer);
        observer.Dispose();
        observer = null;
    }
}
