using IconFont.Maui.FluentIcons;
#if AVALONIA
using PlatformBitmapExportContext = Microsoft.Maui.Graphics.Skia.SkiaBitmapExportContext;
#else
using Microsoft.Maui.Graphics.Platform;
#endif

namespace DotNetConf.App.Controls;

internal static class CommunityPinImages
{
    public static ImageSource CreateCluster(int count)
    {
        const int size = 48;
        var scale = Math.Clamp((int)Math.Ceiling(DeviceDisplay.Current.MainDisplayInfo.Density), 1, 4);
        var path = Path.Combine(FileSystem.CacheDirectory, $"community-cluster-{count}-v1@{scale}x.png");
        if (!File.Exists(path))
        {
            using var bitmap = new PlatformBitmapExportContext(size * scale, size * scale, 1);
            var canvas = bitmap.Canvas;
            canvas.Scale(scale, scale);
            canvas.SetShadow(new SizeF(0, 2), 2, Colors.Black.WithAlpha(0.25f));
            canvas.FillColor = Colors.White;
            canvas.FillCircle(24, 23, 21);
            canvas.SetShadow(SizeF.Zero, 0, Colors.Transparent);
            canvas.FillColor = Color.FromArgb("#B9AAEE");
            canvas.FillCircle(24, 23, 19);
            canvas.FillColor = Color.FromArgb("#512BD4");
            canvas.FillCircle(24, 23, 15);
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.FontSize = count >= 100 ? 14 : 18;
            canvas.FontColor = Colors.White;
            canvas.DrawString(count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                6, 8, 36, 30, HorizontalAlignment.Center, VerticalAlignment.Center);
            using (var output = File.Create(path + ".tmp"))
                bitmap.WriteToStream(output);
            File.Move(path + ".tmp", path, overwrite: true);
        }
        return ImageSource.FromFile(path);
    }

    public static ImageSource Create(bool watchParty, IFontManager fontManager)
    {
        const float width = 44;
        const float height = 56;
        var scale = Math.Clamp((int)Math.Ceiling(DeviceDisplay.Current.MainDisplayInfo.Density), 1, 4);
        var name = watchParty ? "watch" : "present";
        var path = Path.Combine(FileSystem.CacheDirectory, $"community-{name}-v2@{scale}x.png");
        if (!File.Exists(path))
        {
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
            canvas.SetShadow(new SizeF(0, 2), 3, Colors.Black.WithAlpha(0.3f));
            canvas.FillColor = Color.FromArgb(watchParty ? "#512BD4" : "#9A007B");
            canvas.FillPath(outline);
            canvas.SetShadow(SizeF.Zero, 0, Colors.Transparent);
            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 2;
            canvas.DrawPath(outline);
            canvas.FillColor = Colors.White.WithAlpha(0.15f);
            canvas.FillCircle(22, 22, 15);
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
            File.Move(path + ".tmp", path, overwrite: true);
        }
        return ImageSource.FromFile(path);
    }
}
