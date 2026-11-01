namespace DotNetConf.App.Services;

public readonly record struct DetailLayout(
    bool IsSplit, bool IsStacked, GridLength ListLength, GridLength HingeLength, GridLength DetailLength)
{
    public const double SplitWidth = 640;
    public const double MinimumWindowHeight = 600;
    public const double MinimumStackedPaneHeight = 200;
    public const double ListHorizontalPadding = 48;
    public const double MinimumListContentWidth = 150;
    public const double MinimumSearchResultsViewportHeight = 120;

    public static bool ShouldScrollListHeader(double height, double textScale, bool isStacked)
    {
        if (!double.IsFinite(textScale) || textScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(textScale));
        return isStacked || height < MinimumWindowHeight * Math.Max(1, textScale);
    }

    public static bool ShouldHideNavigationForSearch(bool isFocused, double width, double height,
        double resultsHeight, bool navigationAlreadyHidden) =>
        isFocused && width > height && height > 0 &&
        (navigationAlreadyHidden || resultsHeight >= 0 && resultsHeight < MinimumSearchResultsViewportHeight);

    public static bool ShouldPrepareShortLandscapeSearch(bool isFocused, double width, double height,
        bool hasHardwareKeyboard) =>
        isFocused && !hasHardwareKeyboard && width > height && height > 0 && height < MinimumWindowHeight;

    public static double KeyboardOverlap(double viewHeight, double viewBottom, double windowBottom, double imeInset) =>
        Math.Clamp(viewBottom - (windowBottom - imeInset), 0, viewHeight);

    public static DetailLayout ForWindow(double width, double height, Rect hinge, double textScale = 1,
        double? contentWidth = null, double? contentHeight = null, bool contentIsCurrent = true)
    {
        if (!double.IsFinite(textScale) || textScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(textScale));
        if (!contentIsCurrent)
        {
            // An unloaded Shell host can retain the size and fold coordinates of an old window.
            hinge = Rect.Zero;
            contentWidth = null;
            contentHeight = null;
        }
        if (contentWidth is { } actualWidth && (!double.IsFinite(actualWidth) || actualWidth <= 0))
            throw new ArgumentOutOfRangeException(nameof(contentWidth));
        if (contentHeight is { } actualHeight && (!double.IsFinite(actualHeight) || actualHeight <= 0))
            throw new ArgumentOutOfRangeException(nameof(contentHeight));
        var availableWidth = contentWidth ?? width;
        var availableHeight = contentHeight ?? height;
        var horizontalHinge = hinge != Rect.Zero && hinge.Width > hinge.Height &&
            hinge.Top > 0 && hinge.Bottom < availableHeight;
        var verticalHinge = hinge != Rect.Zero && hinge.Height > hinge.Width &&
            hinge.Left > 0 && hinge.Right < availableWidth;
        var listWidth = horizontalHinge ? availableWidth : verticalHinge ? hinge.Left : availableWidth * 0.42;
        var scale = Math.Max(1, textScale);
        if (width <= SplitWidth || (!horizontalHinge && height < MinimumWindowHeight * scale) ||
            (horizontalHinge && (hinge.Top < MinimumStackedPaneHeight * scale ||
                                 availableHeight - hinge.Bottom < MinimumStackedPaneHeight * scale)) ||
            listWidth - ListHorizontalPadding < MinimumListContentWidth * scale)
            return new(false, false, GridLength.Star, new(0), new(0));

        if (horizontalHinge)
            return new(true, true, new(hinge.Top), new(hinge.Height), GridLength.Star);

        if (verticalHinge)
            return new(true, false, new(hinge.Left), new(hinge.Width), GridLength.Star);

        return new(true, false, new(0.42, GridUnitType.Star), new(0), new(0.58, GridUnitType.Star));
    }

    public static Rect HingeRelativeToContent(Rect windowHinge, IReadOnlyList<Rect>? spanningBounds)
    {
        if (windowHinge == Rect.Zero || spanningBounds is not { Count: 2 })
            return windowHinge;

        var first = spanningBounds[0];
        var second = spanningBounds[1];
        const double tolerance = 0.01;
        if (windowHinge.Height > windowHinge.Width &&
            first.Width > 0 && first.Height > 0 && second.Width > 0 && second.Height > 0 &&
            Math.Abs(first.Top - second.Top) < tolerance &&
            Math.Abs(second.Left - first.Right - windowHinge.Width) < tolerance)
            return new Rect(first.Right, first.Top, windowHinge.Width, first.Height);

        if (windowHinge.Width > windowHinge.Height &&
            first.Width > 0 && first.Height > 0 && second.Width > 0 && second.Height > 0 &&
            Math.Abs(first.Left - second.Left) < tolerance &&
            Math.Abs(second.Top - first.Bottom - windowHinge.Height) < tolerance)
            return new Rect(first.Left, first.Bottom, first.Width, windowHinge.Height);

        return windowHinge;
    }

    public static Rect HingeRelativeToContent(Rect windowHinge, Point contentOrigin) =>
        windowHinge == Rect.Zero ? Rect.Zero :
        new Rect(windowHinge.X - contentOrigin.X, windowHinge.Y - contentOrigin.Y,
            windowHinge.Width, windowHinge.Height);
}
