using Android.App;
using Android.Runtime;
using AndroidX.Core.Content;
using AndroidX.Window.Java.Layout;
using AndroidX.Window.Layout;
using DotNetConf.App.Services;
using Microsoft.Maui.Platform;

namespace DotNetConf.App.Platforms.Android;

internal sealed class AndroidFoldableRegionSource : Java.Lang.Object, IFoldableRegionSource, AndroidX.Core.Util.IConsumer
{
    private Activity? activity;
    private WindowInfoTrackerCallbackAdapter? tracker;

    public Rect WindowRegion { get; private set; }
    public event EventHandler? Changed;

    public void Start(Activity owner)
    {
        if (ReferenceEquals(activity, owner))
            return;
        if (activity is not null)
            Stop(activity);

        activity = owner;
        tracker = new WindowInfoTrackerCallbackAdapter(WindowInfoTracker.Companion.GetOrCreate(owner));
        var executor = ContextCompat.GetMainExecutor(owner)
            ?? throw new InvalidOperationException("The Android main-thread executor is unavailable.");
        tracker.AddWindowLayoutInfoListener(owner, executor, this);
    }

    public void Stop(Activity owner)
    {
        if (!ReferenceEquals(activity, owner))
            return;

        tracker?.RemoveWindowLayoutInfoListener(this);
        tracker = null;
        activity = null;
        SetRegion(Rect.Zero);
    }

    public void Accept(Java.Lang.Object? value)
    {
        if (activity is not { } owner)
            return;
        if (value is not WindowLayoutInfo info)
            throw new InvalidOperationException("Android Window Manager supplied an invalid layout update.");

        var region = Rect.Zero;
        using var foldingType = Java.Lang.Class.FromType(typeof(IFoldingFeature));
        foreach (var feature in info.DisplayFeatures)
        {
            if (feature is not Java.Lang.Object nativeFeature || !foldingType.IsInstance(nativeFeature))
                continue;

            var fold = feature.JavaCast<IFoldingFeature>();
            if (!fold.IsSeparating)
                continue;

            var bounds = fold.Bounds;
            region = new Rect(owner.FromPixels(bounds.Left), owner.FromPixels(bounds.Top),
                owner.FromPixels(bounds.Width()), owner.FromPixels(bounds.Height()));
            break;
        }
        SetRegion(region);
    }

    private void SetRegion(Rect region)
    {
        if (WindowRegion == region)
            return;

        WindowRegion = region;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
