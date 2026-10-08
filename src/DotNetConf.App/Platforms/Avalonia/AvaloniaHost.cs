using Avalonia.Controls;
using Avalonia.Controls.Maui.Services;
using Avalonia.Media;
using Avalonia.Platform;
using DotNetConf.App.Controls;
using AvaloniaHandlers = Avalonia.Controls.Maui.Handlers;

namespace DotNetConf.App;

internal static class AvaloniaHost
{
    public static MauiAppBuilder UseAvaloniaHost(this MauiAppBuilder builder)
    {
        // Avalonia's FileSystem.AppDataDirectory is empty until ~/.config exists (new Linux accounts, containers).
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);

        builder.UseAvaloniaApp(useSingleViewLifetime: OperatingSystem.IsBrowser())
            .UseAvaloniaEssentials()
            // Buttons and Shell tabs resolve the image service by concrete type, which MAUI registers for itself.
            .ConfigureImageSources(services => services.AddService<FontImageSource, AvaloniaFontImageSourceService>());
        builder.Services.AddSingleton<IBrowser, AvaloniaBrowser>();

        AvaloniaHandlers.WindowHandler.Mapper.AppendToMapping(nameof(IWindow.Title), (handler, window) =>
        {
            handler.PlatformView.Title = window.Title ?? AppInfo.Name;
            handler.PlatformView.Icon ??= new WindowIcon(AssetLoader.Open(new Uri("avares://DotNetConf.App/windowicon.png")));
        });

        // The search field and filters draw their own outline, as on Android and iOS.
        AvaloniaHandlers.EntryHandler.Mapper.AppendToMapping("AgendaSearch", (handler, view) =>
        {
            if (view is not SearchEntry)
                return;

            var entry = handler.PlatformView;
            entry.BorderThickness = new Avalonia.Thickness(2);
            entry.BorderBrush = Brushes.Transparent;
            entry.Resources["EntryBorderBrushPointerOver"] = Brushes.Transparent;
            entry.Resources["EntryBorderBrushFocused"] = Brushes.Transparent;
        });
        foreach (var key in new[] { nameof(IPicker.Title), nameof(IPicker.TitleColor) })
        {
            AvaloniaHandlers.PickerHandler.Mapper.AppendToMapping(key, (handler, view) =>
            {
                if (view is not AgendaPicker)
                    return;

                var picker = handler.PlatformView;
                picker.Header = null;
                picker.BorderThickness = default;
                picker.Resources["ComboBoxBackgroundPointerOver"] = Brushes.Transparent;
                picker.Resources["ComboBoxBackgroundPressed"] = Brushes.Transparent;
            });
        }

        // Avalonia's CollectionView overlays EmptyView on the whole list, so keep it below the header.
        AvaloniaHandlers.CollectionViewHandler.Mapper.AppendToMapping("EmptyViewBelowHeader", (handler, view) =>
        {
            if (view is not StructuredItemsView { Header: View header, EmptyView: View empty })
                return;

            handler.PlatformView.LayoutUpdated += (_, _) =>
            {
                if (header.Handler?.PlatformView is Control top && empty.Handler?.PlatformView is Control body
                    && body.Margin.Top != top.Bounds.Height)
                    body.Margin = new Avalonia.Thickness(0, top.Bounds.Height, 0, 0);
            };
        });

        return builder;
    }
}
