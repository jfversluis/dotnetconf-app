namespace DotNetConf.App.Controls;

public class AskEditor : Editor
{
    static AskEditor()
    {
#if ANDROID
        Microsoft.Maui.Handlers.EditorHandler2.Mapper.AppendToMapping(nameof(IView.Background), (handler, view) =>
        {
            if (view is AskEditor)
                handler.PlatformView.Background = null;
        });
#endif
    }
}
