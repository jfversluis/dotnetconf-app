namespace DotNetConf.App.Planner;

// Inside the namespace, these usings win over MAUI's global ones (Color, Brush, Button, Grid...).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;

/// <summary>
/// The planner's look: the app's light or dark colors as Avalonia colors, and the fonts and button template both
/// themes share.
/// </summary>
public sealed record PlannerTheme(
    bool IsDark,
    Color Background,
    Color Surface,
    Color Text,
    Color Secondary,
    Color Outline,
    Color Accent,
    Color Purple,
    Color Magenta,
    Color Lavender,
    Color Midnight,
    Color Conflict)
{
    // MAUI fonts are embedded as Avalonia resources under Assets/Fonts on every target.
    public static FontFamily BodyFont { get; } = new("avares://DotNetConf.App/Assets/Fonts#Open Sans");
    public static FontFamily HeadingFont { get; } = new("avares://DotNetConf.App/Assets/Fonts#Space Grotesk");

    /// <summary>
    /// A heart in a 24 × 24 box, for the save buttons and the burst when a session is saved.
    /// </summary>
    public static Geometry HeartIcon { get; } = Geometry.Parse(
        "M12,21 C12,21 3,14.6 3,8.9 C3,5.9 5.3,3.8 8,3.8 C9.7,3.8 11.2,4.8 12,6.3 C12.8,4.8 14.3,3.8 16,3.8 C18.7,3.8 21,5.9 21,8.9 C21,14.6 12,21 12,21 Z");

    /// <summary>
    /// Lays a button out as just its content, so cards, hearts and chips draw themselves.
    /// </summary>
    public static FuncControlTemplate<Button> ButtonTemplate { get; } = new((button, _) => new ContentPresenter
    {
        Name = "PART_ContentPresenter",
        [!ContentPresenter.ContentProperty] = button[!ContentControl.ContentProperty],
    });

    // Topic colors sit next to the brand purple and magenta and keep their contrast in both themes.
    private static readonly Color[] LightTopics =
        [Rgb(0x51, 0x2B, 0xD4), Rgb(0xB8, 0x00, 0x92), Rgb(0x00, 0x5F, 0xB8), Rgb(0x00, 0x73, 0x6B), Rgb(0xB0, 0x4A, 0x00), Rgb(0x2E, 0x7D, 0x32)];
    private static readonly Color[] DarkTopics =
        [Rgb(0xB9, 0xAA, 0xEE), Rgb(0xFF, 0x8A, 0xDC), Rgb(0x8C, 0xC4, 0xFF), Rgb(0x6F, 0xDB, 0xC9), Rgb(0xFF, 0xB8, 0x6B), Rgb(0x96, 0xD8, 0x9A)];

    public Color TopicColor(int index) => (IsDark ? DarkTopics : LightTopics)[index % LightTopics.Length];

    public static Color WithAlpha(Color color, double alpha) => Color.FromArgb((byte)(alpha * 255), color.R, color.G, color.B);

    /// <summary>
    /// Mixes <paramref name="amount"/> of <paramref name="other"/> into <paramref name="color"/>.
    /// </summary>
    public static Color Blend(Color color, Color other, double amount) => Color.FromRgb(
        (byte)(color.R + (other.R - color.R) * amount),
        (byte)(color.G + (other.G - color.G) * amount),
        (byte)(color.B + (other.B - color.B) * amount));

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
