namespace DotNetConf.App.Services;

public static class ScheduleLayout
{
    public const double WrappedTopicsWidth = 720;
    public const double ComfortableHeight = 600;
    public const double TimelineLabelsWidth = 340;

    public static bool ShouldStackTimelineLabels(double width) => width < TimelineLabelsWidth;

    public static bool ShouldWrapTopics(double width, double height, double textScale = 1) =>
        width >= WrappedTopicsWidth * Scale(textScale) && height >= ComfortableHeight * Scale(textScale);

    public static bool ShouldShowHeading(double height, double textScale = 1) =>
        height >= ComfortableHeight * Scale(textScale);

    private static double Scale(double value) =>
        double.IsFinite(value) && value > 0 ? Math.Max(1, value) :
        throw new ArgumentOutOfRangeException(nameof(value));
}
