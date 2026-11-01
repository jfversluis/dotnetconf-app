namespace DotNetConf.App.Services;

public static class DayTabLayout
{
    public const double MinimumWidth = 64;
    public const double HorizontalPadding = 16;

    public static bool ShouldScroll(double availableWidth, int count, double widestLabel)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(availableWidth));
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (!double.IsFinite(widestLabel) || widestLabel < 0)
            throw new ArgumentOutOfRangeException(nameof(widestLabel));

        return availableWidth / count < Math.Max(MinimumWidth, widestLabel + HorizontalPadding);
    }
}
