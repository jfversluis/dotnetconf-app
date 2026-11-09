namespace DotNetConf.App.Controls;

public class DuoSafeAreaContent : ContentView
{
    public DuoSafeAreaContent() => SafeAreaEdges = SafeAreaEdges.None;

    public static Thickness CalculateHorizontalPadding(Rect frame, Rect safeFrame) => new(
        Math.Clamp(safeFrame.Left - frame.Left, 0, frame.Width), 0,
        Math.Clamp(frame.Right - safeFrame.Right, 0, frame.Width), 0);
}
