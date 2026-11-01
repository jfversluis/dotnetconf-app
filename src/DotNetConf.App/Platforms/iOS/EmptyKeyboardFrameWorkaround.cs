using CoreFoundation;
using DotNetConf.App.Services;
using Foundation;
using UIKit;

namespace DotNetConf.App.Platforms.iOS;

internal sealed class EmptyKeyboardFrameWorkaround
{
    private readonly Grid panes;
    private readonly Grid detailPane;
    private readonly KeyboardFrameRefreshGuard guard = new();
    private NSObject? showObserver;
    private NSObject? hideObserver;

    public EmptyKeyboardFrameWorkaround(Grid panes, Grid detailPane)
    {
        this.panes = panes;
        this.detailPane = detailPane;
        panes.Loaded += OnLoaded;
        panes.Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (showObserver is not null)
            return;

        showObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIKeyboard.WillShowNotification, OnWillShow);
        hideObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIKeyboard.WillHideNotification, _ => guard.Cancel());
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        guard.Cancel();
        if (showObserver is not null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(showObserver);
            showObserver.Dispose();
            showObserver = null;
        }
        if (hideObserver is not null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(hideObserver);
            hideObserver.Dispose();
            hideObserver = null;
        }
    }

    private void OnWillShow(NSNotification notification)
    {
        if (notification.UserInfo?[UIKeyboard.FrameEndUserInfoKey] is not NSValue frame)
        {
            guard.Cancel();
            return;
        }

        var pending = guard.WillShow(frame.CGRectValue.IsEmpty);
        if (pending is null)
            return;

        // MAUI RC2 treats an empty keyboard frame during rotation as a visible keyboard.
        // Refresh only after notification delivery, and never after a newer keyboard event.
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            if (!panes.IsLoaded || !guard.IsCurrent(pending.Value))
                return;

            Refresh(panes);
            Refresh(detailPane);
        });
    }

    private static void Refresh(Grid grid)
    {
        if (grid.SafeAreaEdges.Bottom != SafeAreaRegions.SoftInput ||
            grid.Handler?.PlatformView is not UIView { Window: not null } view)
            return;

        var edges = grid.SafeAreaEdges;
        grid.SafeAreaEdges = new(edges.Left, edges.Top, edges.Right, SafeAreaRegions.None);
        view.SetNeedsLayout();
        view.LayoutIfNeeded();
        grid.SafeAreaEdges = edges;
        view.SetNeedsLayout();
        view.LayoutIfNeeded();
    }
}
