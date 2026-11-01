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
}
