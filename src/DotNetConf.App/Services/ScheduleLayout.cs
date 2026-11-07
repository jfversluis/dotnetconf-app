namespace DotNetConf.App.Services;

public static class ScheduleLayout
{
    public const double WrappedTopicsWidth = 720;
    public const double ComfortableHeight = 600;
    public const double TimelineLabelsWidth = 340;
    public const double MinimumTimelinePaneWidth = 340;
    public const double MinimumDetailPaneWidth = 300;
    public const double MinimumTimelineContentWidth = 320;
    public const double MinimumReadableTimelineCardHeight = 48;

    public static double TimelineContentWidth(double viewportWidth) =>
        double.IsFinite(viewportWidth) && viewportWidth > 0
            ? Math.Max(MinimumTimelineContentWidth, viewportWidth)
            : throw new ArgumentOutOfRangeException(nameof(viewportWidth));

    public static DetailLayout ForDetails(DetailLayout layout, double width, double textScale = 1)
    {
        var scale = Scale(textScale);
        var compact = new DetailLayout(false, false, GridLength.Star, new(0), new(0));
        if (!layout.IsSplit || layout.IsStacked)
            return compact;

        // A reported vertical division determines the pane edges; flat wide windows split evenly.
        var divided = layout.ListLength.IsAbsolute;
        var left = divided ? layout.ListLength.Value : width / 2;
        var right = width - left - layout.HingeLength.Value;
        if (left < MinimumTimelinePaneWidth * scale || right < MinimumDetailPaneWidth * scale)
            return compact;

        return divided ? layout : new(true, false, GridLength.Star, new(0), GridLength.Star);
    }

    public static bool ShouldStackTimelineLabels(double width) => width < TimelineLabelsWidth;

    public static bool ShouldPinTimelineDayHeader(double viewportHeight, double headerHeight, double bottomInset) =>
        viewportHeight - bottomInset >= headerHeight + MinimumReadableTimelineCardHeight;

    public static bool ShouldUseCompactHeader(double width, double height, double textScale = 1)
    {
        var scale = Scale(textScale);
        return width < 260 * scale || height < ComfortableHeight * scale;
    }

    public static bool ShouldWrapTopics(double width, double height, double textScale = 1) =>
        width >= WrappedTopicsWidth * Scale(textScale) && height >= ComfortableHeight * Scale(textScale);

    public static bool ShouldShowHeading(double height, double textScale = 1) =>
        height >= ComfortableHeight * Scale(textScale);

    private static double Scale(double value) =>
        double.IsFinite(value) && value > 0 ? Math.Max(1, value) :
        throw new ArgumentOutOfRangeException(nameof(value));
}
