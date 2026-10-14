using IconFont.Maui.FluentIcons;
#if AVALONIA
using Avalonia.Media.Imaging;
using AvaloniaControls = Avalonia.Controls;
using AvaloniaMedia = Avalonia.Media;
#else
using Microsoft.Maui.Graphics.Platform;
#endif

namespace DotNetConf.App.Controls;

internal static class CommunityPinImages
{
    /// <summary>
    /// Pixels per unit of a pin image. Linux and the browser report no display density, so they use one.
    /// </summary>
    public static int Scale => Math.Max(1, (int)Math.Ceiling(DeviceDisplay.Current.MainDisplayInfo.Density));

    public static ImageSource Create(bool watchParty, IFontManager fontManager)
    {
        const float width = 44;
        const float height = 56;
        var scale = Scale;
        var name = watchParty ? "watch" : "present";
        var path = Path.Combine(FileSystem.CacheDirectory, $"community-{name}-v1@{scale}x.png");
        if (!File.Exists(path))
        {
#if AVALONIA
            using (var output = File.Create(path + ".tmp"))
                DrawWithAvalonia(output, width, height, scale, watchParty, fontManager);
#else
            using var bitmap = new PlatformBitmapExportContext((int)width * scale, (int)height * scale, 1);
            var canvas = bitmap.Canvas;
            canvas.Scale(scale, scale);
            var outline = new PathF();
            outline.MoveTo(22, 53);
            outline.CurveTo(18, 47, 3, 34, 3, 23);
            outline.CurveTo(3, 12, 11, 3, 22, 3);
            outline.CurveTo(33, 3, 41, 12, 41, 23);
            outline.CurveTo(41, 34, 26, 47, 22, 53);
            outline.Close();
            canvas.SetShadow(new SizeF(0, 1), 2, Colors.Black.WithAlpha(0.25f));
            canvas.FillColor = Color.FromArgb(watchParty ? "#512BD4" : "#9A007B");
            canvas.FillPath(outline);
            canvas.SetShadow(SizeF.Zero, 0, Colors.Transparent);
            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 2;
            canvas.DrawPath(outline);
#if IOS
            var nativeFont = fontManager.GetFont(Microsoft.Maui.Font.OfSize(FluentIconsRegular.FontFamily, 24));
            canvas.Font = new Microsoft.Maui.Graphics.Font(nativeFont.Name);
#elif ANDROID
            canvas.Font = new Microsoft.Maui.Graphics.Font("FluentSystemIcons-Regular.ttf");
#endif
            canvas.FontSize = 24;
            canvas.FontColor = Colors.White;
            canvas.DrawString(watchParty ? FluentIconsRegular.VideoClip24 : FluentIconsRegular.Presenter24,
                10, 10, 24, 26, HorizontalAlignment.Center, VerticalAlignment.Center);
            using (var output = File.Create(path + ".tmp"))
                bitmap.WriteToStream(output);
#endif
            File.Move(path + ".tmp", path, overwrite: true);
        }
        return ImageSource.FromFile(path);
    }

#if AVALONIA
    // MAUI Graphics can't create a Skia surface on Avalonia's SkiaSharp.
    private static void DrawWithAvalonia(Stream output, float width, float height, int scale, bool watchParty, IFontManager fontManager)
    {
        var pin = new AvaloniaControls.Grid
        {
            Width = width,
            Height = height,
            Children =
            {
                new AvaloniaControls.Shapes.Path
                {
                    Data = AvaloniaMedia.Geometry.Parse("M22,53 C18,47 3,34 3,23 C3,12 11,3 22,3 C33,3 41,12 41,23 C41,34 26,47 22,53 Z"),
                    Fill = new AvaloniaMedia.SolidColorBrush(AvaloniaMedia.Color.Parse(watchParty ? "#512BD4" : "#9A007B")),
                    Stroke = AvaloniaMedia.Brushes.White,
                    StrokeThickness = 2,
                    Effect = new AvaloniaMedia.DropShadowEffect { OffsetX = 0, OffsetY = 1, BlurRadius = 2, Color = AvaloniaMedia.Color.FromArgb(64, 0, 0, 0) },
                },
                new AvaloniaControls.TextBlock
                {
                    Text = watchParty ? FluentIconsRegular.VideoClip24 : FluentIconsRegular.Presenter24,
                    FontFamily = ((Avalonia.Controls.Maui.FontManager)fontManager).GetFontFamily(Microsoft.Maui.Font.OfSize(FluentIconsRegular.FontFamily, 24)),
                    FontSize = 24,
                    Foreground = AvaloniaMedia.Brushes.White,
                    Width = 24,
                    Height = 26,
                    Margin = new Avalonia.Thickness(10, 10, 0, 0),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                    TextAlignment = AvaloniaMedia.TextAlignment.Center,
                },
            },
        };
        pin.Measure(new Avalonia.Size(width, height));
        pin.Arrange(new Avalonia.Rect(0, 0, width, height));
        using var bitmap = new RenderTargetBitmap(new Avalonia.PixelSize((int)width * scale, (int)height * scale), new Avalonia.Vector(96 * scale, 96 * scale));
        bitmap.Render(pin);
        bitmap.Save(output, PngBitmapEncoderOptions.Default);
    }
#endif
}
