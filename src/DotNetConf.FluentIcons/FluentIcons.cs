using Microsoft.Maui.Hosting;

namespace IconFont.Maui.FluentIcons;

// Only the glyphs the app uses; code points match IconFont.Maui.FluentIcons 1.1.1.
public static class FluentIconsRegular
{
    public const string FontFamily = "FluentIcons";
    public const string CalendarAgenda24 = "\uf214";
    public const string ChevronRight24 = "\uf2b1";
    public const string Dismiss24 = "\uf36a";
    public const string Heart24 = "\uf47a";
    public const string Home24 = "\uf481";
    public const string Info24 = "\uf4a4";
    public const string Open24 = "\uf583";
    public const string People24 = "\uf5a9";
    public const string Search24 = "\uf690";
}

public static class FluentIconsFilled
{
    public const string FontFamily = "FluentIconsFilled";
    public const string Heart24 = "\uf47e";
}

public static class IconFontBuilderExtensions
{
    public static MauiAppBuilder UseFluentIconsRegular(this MauiAppBuilder builder) =>
        builder.ConfigureFonts(fonts => fonts.AddFont("FluentSystemIcons-Regular.ttf", FluentIconsRegular.FontFamily));

    public static MauiAppBuilder UseFluentIconsFilled(this MauiAppBuilder builder) =>
        builder.ConfigureFonts(fonts => fonts.AddFont("FluentSystemIcons-Filled.ttf", FluentIconsFilled.FontFamily));
}
