using DotNetConf.App.Controls;
using Microsoft.Maui.Handlers;
using UIKit;
using NativeContentView = Microsoft.Maui.Platform.ContentView;

namespace DotNetConf.App.Platforms.iOS;

internal sealed class DuoSafeAreaContentHandler : ContentViewHandler
{
    private static bool IsDuo => OperatingSystem.IsIOSVersionAtLeast(27) &&
        (Environment.GetEnvironmentVariable("SIMULATOR_MODEL_IDENTIFIER") ?? DeviceInfo.Model) == "iPhone19,4";

    protected override NativeContentView CreatePlatformView() => new InsetContentView
    {
        CrossPlatformLayout = VirtualView,
        UpdateInsets = UpdateInsets
    };

    protected override void DisconnectHandler(NativeContentView platformView)
    {
        if (platformView is InsetContentView content)
            content.UpdateInsets = null;
        base.DisconnectHandler(platformView);
    }

    private void UpdateInsets()
    {
        if (!IsDuo || VirtualView is not DuoSafeAreaContent content || PlatformView.Window is null)
            return;

        Element? ancestor = content.Parent;
        while (ancestor is not null && ancestor is not Page)
            ancestor = ancestor.Parent;
        if (ancestor?.Handler?.PlatformView is not UIView page)
            return;

        // Nested scroll content must use the owning page's chrome, not its own safe-area guide.
        var frame = PlatformView.ConvertRectToView(PlatformView.Bounds, page);
        var safe = page.SafeAreaLayoutGuide.LayoutFrame;
        var padding = DuoSafeAreaContent.CalculateHorizontalPadding(
            new Rect(frame.X, frame.Y, frame.Width, frame.Height),
            new Rect(safe.X, safe.Y, safe.Width, safe.Height));
        if (content.Padding != padding)
            content.Padding = padding;
    }

    private sealed class InsetContentView : NativeContentView
    {
        public Action? UpdateInsets { get; set; }

        public override void SafeAreaInsetsDidChange()
        {
            base.SafeAreaInsetsDidChange();
            UpdateInsets?.Invoke();
        }

        public override void LayoutSubviews()
        {
            UpdateInsets?.Invoke();
            base.LayoutSubviews();
        }
    }
}
