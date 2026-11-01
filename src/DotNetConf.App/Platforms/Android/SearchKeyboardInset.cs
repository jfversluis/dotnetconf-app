using AndroidX.Core.View;
using AndroidX.Window.Layout;
using DotNetConf.App.Services;
using Microsoft.Maui.Platform;

namespace DotNetConf.App.Platforms.Android;

internal static class SearchKeyboardInset
{
    public static double? GetBottomOverlap(VisualElement element)
    {
        if (element.Handler?.PlatformView is not global::Android.Views.View view ||
            view.Context?.GetActivity() is not { } activity ||
            ViewCompat.GetRootWindowInsets(view) is not { } insets || view.Height <= 0)
            return null;

        var imeType = WindowInsetsCompat.Type.Ime();
        if (!insets.IsVisible(imeType))
            return 0;

        var ime = insets.GetInsets(imeType);
        if (ime is null)
            return null;

        var origin = new int[2];
        view.GetLocationOnScreen(origin);
        var windowBottom = WindowMetricsCalculator.Companion.OrCreate
            .ComputeCurrentWindowMetrics(activity).Bounds.Bottom;
        var overlap = DetailLayout.KeyboardOverlap(view.Height, origin[1] + view.Height, windowBottom, ime.Bottom);
        return activity.FromPixels(overlap);
    }
}
