namespace DotNetConf.App.Controls;

public sealed class TextScaleMonitor : Grid
{
    private readonly Label unscaled = CreateLabel(false);
    private readonly Label scaled = CreateLabel(true);

    internal event EventHandler? TextScaleChanged;

    internal double? TextScale
    {
        get
        {
            if (scaled.Handler is null || unscaled.Handler is null)
                return null;

            var baseline = unscaled.Measure(double.PositiveInfinity, double.PositiveInfinity).Height;
            var current = scaled.Measure(double.PositiveInfinity, double.PositiveInfinity).Height;
            var scale = current / baseline;
            return double.IsFinite(scale) && scale > 0 ? scale : null;
        }
    }

    public TextScaleMonitor()
    {
        Opacity = 0;
        InputTransparent = true;
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.Start;
        AutomationProperties.SetIsInAccessibleTree(this, false);
        AutomationProperties.SetExcludedWithChildren(this, true);
        Add(unscaled);
        Add(scaled);
        scaled.SizeChanged += (_, _) => TextScaleChanged?.Invoke(this, EventArgs.Empty);
        unscaled.SizeChanged += (_, _) => TextScaleChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Label CreateLabel(bool autoScale) => new()
    {
        Text = "Ag",
        FontFamily = "OpenSansRegular",
        FontSize = 16,
        FontAutoScalingEnabled = autoScale,
        HorizontalOptions = LayoutOptions.Start,
        VerticalOptions = LayoutOptions.Start
    };
}
