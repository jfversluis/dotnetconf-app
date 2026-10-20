using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Syncfusion.Maui.Toolkit;
using Syncfusion.Maui.Toolkit.Internals;
using AvaloniaHandlers = Avalonia.Controls.Maui.Handlers;
using SfPointerEventArgs = Syncfusion.Maui.Toolkit.Internals.PointerEventArgs;

namespace DotNetConf.App;

// Syncfusion.Maui.Toolkit 1.0.11 has native views only for Android, iOS, Mac Catalyst and Windows; on plain .NET its
// SfViewHandler throws and its touch detector does nothing. Its drawn views (SfView) are MAUI layouts, so Avalonia's
// layout handler hosts them, and pointer input is forwarded to views that listen to their own touches.
internal static class AvaloniaSyncfusion
{
    private static readonly ConditionalWeakTable<Control, IViewHandler> ForwardedControls = [];

    public static MauiAppBuilder UseAvaloniaSyncfusion(this MauiAppBuilder builder)
    {
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<IDrawableLayout, AvaloniaHandlers.LayoutHandler>());

        // As on Windows, a drawn view without a background lets input through. SfTabView's content area overlaps its
        // tab bar, and Avalonia's layouts otherwise get a transparent, hit-testable background.
        AvaloniaHandlers.LayoutHandler.Mapper.AppendToMapping(nameof(IView.Background), (handler, view) =>
        {
            if (view is IDrawableLayout && view.Background is null)
                handler.PlatformView.Background = null;
        });

        // SfTabItem selects its tab when it sees the pointer released.
        AvaloniaHandlers.ViewHandler.ViewMapper.AppendToMapping("SyncfusionTouch", (handler, view) =>
        {
            if (view is ITouchListener && handler.PlatformView is Control control
                && ForwardedControls.TryAdd(control, handler))
                ForwardTouches(control, handler);
        });
        return builder;
    }

    // Mirrors Syncfusion's Windows touch detector.
    private static void ForwardTouches(Control control, IViewHandler handler)
    {
        void Send(Avalonia.Input.PointerEventArgs e, PointerActions action)
        {
            if (handler.VirtualView is not View { IsEnabled: true, InputTransparent: false } view
                || view is not ITouchListener listener)
                return;
            var position = e.GetPosition(control);
            var device = e.Pointer.Type switch
            {
                PointerType.Mouse => PointerDeviceType.Mouse,
                PointerType.Pen => PointerDeviceType.Stylus,
                _ => PointerDeviceType.Touch,
            };
            listener.OnTouch(new SfPointerEventArgs(e.Pointer.Id, action, device, new Point(position.X, position.Y)));
        }

        control.PointerEntered += (_, e) => Send(e, PointerActions.Entered);
        control.PointerPressed += (_, e) => Send(e, PointerActions.Pressed);
        control.PointerMoved += (_, e) => Send(e, PointerActions.Moved);
        control.PointerReleased += (_, e) => Send(e, PointerActions.Released);
        control.PointerExited += (_, e) => Send(e, PointerActions.Exited);
    }
}
