#if ANDROID
using Microsoft.Maui.Platform;
#endif

namespace DotNetConf.App.Controls;

public class SearchEntry : Entry
{
#if ANDROID
    public SearchEntry()
    {
        Loaded += OnLoaded;
    }

    private Android.Widget.EditText? NativeInput => Handler switch
    {
        Microsoft.Maui.Handlers.EntryHandler2 material => material.PlatformView?.EditText,
        Microsoft.Maui.Handlers.EntryHandler standard => standard.PlatformView,
        _ => null
    };

    internal bool HasActiveInputConnection
    {
        get
        {
            var input = NativeInput;
            using var manager = input?.Context?.GetSystemService(Android.Content.Context.InputMethodService)
                as Android.Views.InputMethods.InputMethodManager;
            return input is not null && manager?.InvokeIsActive(input) == true;
        }
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        // MAUI's initial Entry mappings replace IME options, so preserve Search after they run.
        if (NativeInput is { } input)
            input.ImeOptions |= (Android.Views.InputMethods.ImeAction)Android.Views.InputMethods.ImeFlags.NoFullscreen;
    }
#endif

    static SearchEntry()
    {
#if ANDROID
        Microsoft.Maui.Handlers.EntryHandler2.Mapper.AppendToMapping(nameof(IEntry.Placeholder), (handler, view) =>
        {
            if (view is not SearchEntry)
                return;

            var layout = handler.PlatformView;
            layout.BoxBackgroundMode = Google.Android.Material.TextField.TextInputLayout.BoxBackgroundNone;
            layout.HintEnabled = false;
            layout.SetPadding(0, 0, 0, 0);
            layout.Background = null;
            layout.SetGravity((layout.Gravity & ~Android.Views.GravityFlags.VerticalGravityMask)
                | Android.Views.GravityFlags.CenterVertical);
            if (layout.EditText is { } input)
            {
                input.SetPadding(0, 0, 0, 0);
                input.Background = null;
                input.Hint = view.Placeholder;
                input.Gravity = (input.Gravity & ~Android.Views.GravityFlags.VerticalGravityMask)
                    | Android.Views.GravityFlags.CenterVertical;
            }
        });
        Microsoft.Maui.Handlers.EntryHandler2.Mapper.AppendToMapping(nameof(IEntry.PlaceholderColor), (handler, view) =>
        {
            if (view is SearchEntry && handler.PlatformView.EditText is { } input && view.PlaceholderColor is { } color)
                input.SetHintTextColor(color.ToPlatform());
        });
#elif IOS
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("AgendaSearch", (handler, view) =>
        {
            if (view is SearchEntry)
                handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
        });
#endif
    }
}
