#if ANDROID
using PlatformPicker = Microsoft.Maui.Platform.MauiMaterialPickerTextInputLayout;
using BasePickerHandler = Microsoft.Maui.Handlers.PickerHandler2;
#elif IOS
using PlatformPicker = Microsoft.Maui.Platform.MauiPicker;
using BasePickerHandler = Microsoft.Maui.Handlers.PickerHandler;
#endif

namespace DotNetConf.App.Controls;

public class AgendaPickerHandler : BasePickerHandler
{
    protected override void ConnectHandler(PlatformPicker platformView)
    {
        base.ConnectHandler(platformView);
#if ANDROID
        platformView.BoxBackgroundMode = Google.Android.Material.TextField.TextInputLayout.BoxBackgroundNone;
        platformView.Picker.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
        // Preserve PickerManager's touch policy: the first tap activates, keyboard focus remains available.
        platformView.Picker.FocusableInTouchMode = false;
#elif IOS
        platformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
    }
}
