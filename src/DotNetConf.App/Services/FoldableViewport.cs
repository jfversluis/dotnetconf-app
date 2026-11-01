#if !ANDROID
using Microsoft.Maui.Foldable;
#else
using Microsoft.Maui.Platform;
#endif

namespace DotNetConf.App.Services;

internal sealed class FoldableViewport
{
    private readonly VisualElement content;
    private readonly VisualElement observer;
    private readonly Action changed;
    private readonly IFoldableRegionSource? regionSource;
    private bool subscribed;
#if !ANDROID
    private DualScreenInfo? foldInfo;
#endif

    public FoldableViewport(VisualElement content, VisualElement observer, Action changed,
        IFoldableRegionSource? regionSource)
    {
        this.content = content;
        this.observer = observer;
        this.changed = changed;
        this.regionSource = regionSource;
#if ANDROID
        if (regionSource is null)
            throw new InvalidOperationException("The Android fold-region source is not registered.");
#endif
        content.SizeChanged += OnLayoutChanged;
        content.Loaded += OnLayoutChanged;
        content.Unloaded += OnLayoutChanged;
        if (!ReferenceEquals(content, observer))
            observer.SizeChanged += OnLayoutChanged;
    }

    public bool IsCurrent => content.IsLoaded && observer.IsLoaded &&
        content.Handler is not null && content.Width > 0 && content.Height > 0;

    private void OnLayoutChanged(object? sender, EventArgs args)
    {
#if !ANDROID
        if (foldInfo is null && content.Width > 0 && content.Height > 0)
        {
            foldInfo = new DualScreenInfo(observer);
            foldInfo.PropertyChanged += (_, _) => changed();
        }
#endif
        if (content.IsLoaded && !subscribed && regionSource is not null)
        {
            regionSource.Changed += OnLayoutChanged;
            subscribed = true;
        }
        else if (!content.IsLoaded && subscribed && regionSource is not null)
        {
            regionSource.Changed -= OnLayoutChanged;
            subscribed = false;
        }
        changed();
    }

    public DetailLayout GetLayout(double windowWidth, double windowHeight, double textScale)
    {
        var contentIsCurrent = IsCurrent;
#if ANDROID
        var hinge = regionSource!.WindowRegion;
#else
        var hinge = foldInfo?.HingeBounds ?? Rect.Zero;
#endif
#if IOS
        if (contentIsCurrent)
            hinge = DetailLayout.HingeRelativeToContent(hinge, foldInfo?.SpanningBounds);
#elif ANDROID
        if (contentIsCurrent && hinge != Rect.Zero)
        {
            if (content.Handler?.PlatformView is not Android.Views.View nativeView)
                throw new InvalidOperationException("The foldable content has no Android view.");

            var origin = new int[2];
            nativeView.GetLocationInWindow(origin);
            hinge = DetailLayout.HingeRelativeToContent(hinge,
                new Point(nativeView.Context.FromPixels(origin[0]), nativeView.Context.FromPixels(origin[1])));
        }
#endif
        var contentWidth = content.Width > 0 ? content.Width : (double?)null;
        var contentHeight = content.Height > 0 ? content.Height : (double?)null;
        return DetailLayout.ForWindow(windowWidth, windowHeight, hinge, textScale,
            contentWidth, contentHeight, contentIsCurrent);
    }
}
