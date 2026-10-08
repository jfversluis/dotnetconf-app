using IconFont.Maui.FluentIcons;
using Microsoft.Maui.Graphics.Platform;

namespace DotNetConf.App.Controls;

internal static class CommunityPinImages
{
    public static ImageSource Create(bool watchParty, IFontManager fontManager)
    {
        const float width = 44;
        const float height = 56;
        var scale = (int)Math.Ceiling(DeviceDisplay.Current.MainDisplayInfo.Density);
        var name = watchParty ? "watch" : "present";
        var path = Path.Combine(FileSystem.CacheDirectory, $"community-{name}-v1@{scale}x.png");
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
            File.Move(path + ".tmp", path, overwrite: true);
        }
        return ImageSource.FromFile(path);
    }
}
