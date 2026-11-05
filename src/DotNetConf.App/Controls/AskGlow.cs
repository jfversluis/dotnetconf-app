namespace DotNetConf.App.Controls;

/// <summary>Decorative only: native controls retain all text editing and accessibility.</summary>
public sealed class AskGlow : GraphicsView, IDrawable
{
    public static readonly BindableProperty IsWorkingProperty = BindableProperty.Create(nameof(IsWorking),
        typeof(bool), typeof(AskGlow), false, propertyChanged: (b, _, _) => ((AskGlow)b).UpdateMotion());
    private readonly IDispatcherTimer timer;
    private float phase;
    private bool active;
    public bool IsWorking { get => (bool)GetValue(IsWorkingProperty); set => SetValue(IsWorkingProperty, value); }

    public AskGlow()
    {
        Drawable = this;
        InputTransparent = true;
        AutomationProperties.SetIsInAccessibleTree(this, false);
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(50);
        timer.Tick += (_, _) =>
        {
            if (ReduceMotion)
            {
                timer.Stop();
                Invalidate();
                return;
            }
            phase += 0.045f;
            Invalidate();
        };
        Loaded += (_, _) =>
        {
            active = true;
            if (Application.Current is { } app) app.RequestedThemeChanged += ThemeChanged;
            UpdateMotion();
        };
        Unloaded += (_, _) =>
        {
            active = false;
            timer.Stop();
            if (Application.Current is { } app) app.RequestedThemeChanged -= ThemeChanged;
        };
    }

    public static bool ReduceMotion
    {
        get
        {
#if IOS
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif ANDROID
            return global::Android.Provider.Settings.Global.GetFloat(
                global::Android.App.Application.Context.ContentResolver,
                global::Android.Provider.Settings.Global.AnimatorDurationScale, 1f) == 0;
#else
            return true;
#endif
        }
    }

    private void UpdateMotion()
    {
        if (active && IsWorking && !ReduceMotion)
            timer.Start();
        else
            timer.Stop();
        Invalidate();
    }

    private void ThemeChanged(object? sender, AppThemeChangedEventArgs e) => Invalidate();

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (dirtyRect.Width < 30 || dirtyRect.Height < 30)
            return;
        var rect = new RectF(12, 12, dirtyRect.Width - 24, dirtyRect.Height - 24);
        var app = Application.Current ?? throw new InvalidOperationException("Ask requires application theme resources.");
        var dark = app.RequestedTheme == AppTheme.Dark;
        var palette = (LinearGradientBrush)app.Resources[dark ? "ConferenceDarkGradient" : "ConferenceGradient"];
        var purple = palette.GradientStops[0].Color;
        var magenta = palette.GradientStops[1].Color;
        for (var i = 6; i > 0; i--)
        {
            canvas.StrokeColor = (i % 2 == 0 ? purple : magenta).WithAlpha((IsWorking ? .045f : .025f) * (7 - i));
            canvas.StrokeSize = i * 3;
            canvas.DrawRoundedRectangle(rect, 24);
        }
        var gradient = new LinearGradientPaint
        {
            StartPoint = new Point(.5 + .5 * Math.Cos(phase), .5 + .5 * Math.Sin(phase)),
            EndPoint = new Point(.5 - .5 * Math.Cos(phase), .5 - .5 * Math.Sin(phase)),
            GradientStops = [new(0, purple), new(.48f, magenta), new(.72f, (Color)app.Resources["Lavender"]), new(1, purple)]
        };
        canvas.SetFillPaint(gradient, rect);
        canvas.FillRoundedRectangle(rect, 24);
        var width = IsWorking ? 2.5f : 1.5f;
        canvas.SetFillPaint(new SolidPaint((Color)app.Resources[dark ? "DarkSurface" : "LightSurface"]), rect);
        canvas.FillRoundedRectangle(rect.X + width, rect.Y + width, rect.Width - width * 2,
            rect.Height - width * 2, 24 - width);
    }
}
