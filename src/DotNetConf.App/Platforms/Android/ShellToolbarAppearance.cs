using Android.Content.Res;
using Android.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace DotNetConf.App.Platforms.Android;

internal static class ShellToolbarAppearance
{
    public static void Register()
    {
        // Pinned MAUI 11 RC2 skips BarBackground when Material 3 is enabled.
        ToolbarHandler.Mapper.AppendToMapping(nameof(Toolbar.BarBackground), (handler, view) =>
        {
            if (view is Toolbar { BarBackground: SolidColorBrush { Color: { } color } } &&
                handler.PlatformView is AndroidX.AppCompat.Widget.Toolbar toolbar)
            {
                toolbar.BackgroundTintMode = PorterDuff.Mode.Src;
                toolbar.BackgroundTintList = ColorStateList.ValueOf(color.ToPlatform());
            }
        });
    }
}
