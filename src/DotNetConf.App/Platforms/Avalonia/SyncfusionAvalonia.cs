using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Maui.Platform;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Microsoft.Maui.Graphics.Skia;
using Microsoft.Maui.Platform;
using Syncfusion.Maui.Toolkit;
using Syncfusion.Maui.Toolkit.Internals;
using AvaloniaHandlers = Avalonia.Controls.Maui.Handlers;
using AvaloniaRect = Avalonia.Rect;
using AvaloniaSize = Avalonia.Size;
using SfPointerEventArgs = Syncfusion.Maui.Toolkit.Internals.PointerEventArgs;

namespace DotNetConf.App;

// Syncfusion.Maui.Toolkit 1.0.11 only has native views for Android, iOS, Mac Catalyst and Windows; on plain .NET its
// SfViewHandler and touch detector are empty. Its drawn controls (SfView) are MAUI layouts that also paint themselves,
// so this hosts their children in an Avalonia panel and paints them with Skia, and forwards pointer input to the views
// that listen to their own touches.
internal static class SyncfusionAvalonia
{
    private static readonly ConditionalWeakTable<Control, object> TouchSources = [];

    public static MauiAppBuilder UseSyncfusionOnAvalonia(this MauiAppBuilder builder)
    {
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<IDrawableLayout, DrawableLayoutHandler>());
        AvaloniaHandlers.ViewHandler.ViewMapper.AppendToMapping("SyncfusionTouch", (handler, view) =>
        {
            if (view is View mauiView and ITouchListener listener && handler.PlatformView is Control control
                && TouchSources.TryAdd(control, listener))
                ForwardTouches(control, mauiView, listener);
        });
        return builder;
    }

    // Mirrors Syncfusion's Windows touch detector. SfTabItem, SfEffectsView and the tab bar's scroll arrows add
    // themselves as touch listeners, which is what selects a tab.
    private static void ForwardTouches(Control control, View view, ITouchListener listener)
    {
        void Send(Avalonia.Input.PointerEventArgs e, PointerActions action)
        {
            if (!view.IsEnabled || view.InputTransparent)
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

    // SfView only reaches Syncfusion's own handler type, so children and repaints are pulled from the view here.
    private sealed class DrawableLayoutHandler() : AvaloniaHandlers.LayoutHandler(DrawableMapper)
    {
        private static readonly IPropertyMapper<Microsoft.Maui.ILayout, DrawableLayoutHandler> DrawableMapper =
            new PropertyMapper<Microsoft.Maui.ILayout, DrawableLayoutHandler>(Mapper)
            {
                // As on Windows, a view without a background lets input through. SfTabView's content area overlaps
                // its tab bar, and Avalonia's layouts otherwise get a transparent, hit-testable background.
                [nameof(IView.Background)] = (handler, view) =>
                {
                    if (view.Background is null)
                        handler.PlatformView.Background = null;
                    else
                        MapBackground(handler, view);
                },
            };

        private DrawableLayoutPanel Panel => (DrawableLayoutPanel)PlatformView;

        protected override Panel CreatePlatformView() => new DrawableLayoutPanel((IDrawableLayout)VirtualView, MauiContext!);

        protected override void ConnectHandler(Panel platformView)
        {
            base.ConnectHandler(platformView);
            var view = (Element)VirtualView;
            view.ChildAdded += OnChildrenChanged;
            view.ChildRemoved += OnChildrenChanged;
            view.PropertyChanged += OnPropertyChanged;
        }

        protected override void DisconnectHandler(Panel platformView)
        {
            var view = (Element)VirtualView;
            view.ChildAdded -= OnChildrenChanged;
            view.ChildRemoved -= OnChildrenChanged;
            view.PropertyChanged -= OnPropertyChanged;
            base.DisconnectHandler(platformView);
        }

        private void OnChildrenChanged(object? sender, ElementEventArgs e) => Panel.SyncChildren();

        private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
            Panel.InvalidateDrawing();
    }

    private sealed class DrawableLayoutPanel : LayoutPanel
    {
        private readonly IDrawableLayout view;
        private readonly IMauiContext context;
        private readonly DrawingLayer below;
        private readonly DrawingLayer above;
        private AvaloniaSize arrangedSize;

        public DrawableLayoutPanel(IDrawableLayout view, IMauiContext context)
        {
            this.view = view;
            this.context = context;
            below = new DrawingLayer(view, DrawingOrder.BelowContent) { ZIndex = int.MinValue, IsHitTestVisible = false };
            above = new DrawingLayer(view, DrawingOrder.AboveContent) { ZIndex = int.MaxValue };
            // Children mirror the MAUI children and Panel inserts those by index, so the layers stay after them.
            VisualChildren.Add(below);
            VisualChildren.Add(above);
        }

        public void SyncChildren()
        {
            var controls = new List<Control>();
            foreach (var child in view)
            {
                if (child.ToPlatform(context) is not Control control)
                    continue;
                control.ZIndex = child.ZIndex;
                controls.Add(control);
            }
            for (var i = 0; i < controls.Count; i++)
            {
                if (i < Children.Count && Children[i] == controls[i])
                    continue;
                Children.Remove(controls[i]);
                Children.Insert(i, controls[i]);
            }
            while (Children.Count > controls.Count)
                Children.RemoveAt(Children.Count - 1);
        }

        public void InvalidateDrawing()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Post(InvalidateDrawing);
                return;
            }
            below.InvalidateVisual();
            above.InvalidateVisual();
        }

        protected override AvaloniaSize MeasureOverride(AvaloniaSize availableSize)
        {
            // SfView.Clear does not raise ChildRemoved.
            if (Children.Count != view.Count)
                SyncChildren();
            below.Measure(default);
            above.Measure(default);
            return base.MeasureOverride(availableSize);
        }

        protected override AvaloniaSize ArrangeOverride(AvaloniaSize finalSize)
        {
            var size = base.ArrangeOverride(finalSize);
            ClipToBounds = view.ClipsToBounds;
            above.IsHitTestVisible = view.DrawingOrder == DrawingOrder.AboveContentWithTouch;
            below.Arrange(new AvaloniaRect(size));
            above.Arrange(new AvaloniaRect(size));
            if (size != arrangedSize)
            {
                arrangedSize = size;
                InvalidateDrawing();
            }
            return size;
        }
    }

    private sealed class DrawingLayer(IDrawableLayout view, DrawingOrder order) : Control
    {
        public override void Render(DrawingContext context)
        {
            var drawingOrder = view.DrawingOrder == DrawingOrder.AboveContentWithTouch
                ? DrawingOrder.AboveContent
                : view.DrawingOrder;
            if (drawingOrder == order)
                DrawOperation.Draw(context, view, Bounds.Size);
        }
    }

    private sealed class DrawOperation(IDrawable drawable, AvaloniaRect bounds) : ICustomDrawOperation
    {
        public static void Draw(DrawingContext context, IDrawable drawable, AvaloniaSize size)
        {
            if (size.Width > 0 && size.Height > 0)
                context.Custom(new DrawOperation(drawable, new AvaloniaRect(size)));
        }

        public AvaloniaRect Bounds => bounds;

        public bool HitTest(Avalonia.Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            using var lease = (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature)?.Lease();
            if (lease?.SkCanvas is not { } canvas)
                return;
            canvas.Save();
            try
            {
                drawable.Draw(new SkiaCanvas { Canvas = canvas }, new RectF(0, 0, (float)bounds.Width, (float)bounds.Height));
            }
            finally
            {
                canvas.Restore();
            }
        }
    }
}
