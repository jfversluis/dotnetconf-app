namespace DotNetConf.App.Planner;

using Avalonia.Controls;

/// <summary>
/// Keeps controls on a canvas only while they are on screen. Each pass shows what is visible by key;
/// whatever wasn't shown since <see cref="Begin"/> is removed at <see cref="End"/>.
/// </summary>
internal sealed class RealizedLayer<TKey, TControl>(Canvas canvas) where TKey : notnull where TControl : Control
{
    private readonly Dictionary<TKey, TControl> realized = [];
    private readonly HashSet<TKey> shown = [];
    private readonly List<TKey> stale = [];

    public IReadOnlyDictionary<TKey, TControl> Realized => realized;

    public void Begin() => shown.Clear();

    public TControl Show(TKey key, Func<TControl> create, double x, double y, double width, double height)
    {
        shown.Add(key);
        if (!realized.TryGetValue(key, out var control))
        {
            control = create();
            realized[key] = control;
            canvas.Children.Add(control);
        }
        Canvas.SetLeft(control, x);
        Canvas.SetTop(control, y);
        control.Width = Math.Max(0, width);
        control.Height = Math.Max(0, height);
        return control;
    }

    public void End()
    {
        stale.Clear();
        foreach (var key in realized.Keys)
        {
            if (!shown.Contains(key))
                stale.Add(key);
        }
        foreach (var key in stale)
        {
            canvas.Children.Remove(realized[key]);
            realized.Remove(key);
        }
    }

    public void Clear()
    {
        foreach (var control in realized.Values)
            canvas.Children.Remove(control);
        realized.Clear();
        shown.Clear();
    }
}
