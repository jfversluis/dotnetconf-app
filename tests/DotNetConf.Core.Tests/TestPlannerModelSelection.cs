using DotNetConf.App.Services;

namespace DotNetConf.Core.Tests;

internal sealed class TestPlannerModelSelection : IPlannerModelSelection
{
    public bool IsSupported { get; set; }
    public string? SelectedModelId { get; private set; }
    public string? Warning { get; set; }
    public Exception? SelectionFailure { get; set; }
    public event EventHandler? Changed;

    public void Select(string? modelId)
    {
        if (SelectionFailure is not null)
            throw SelectionFailure;
        var changed = SelectedModelId != modelId;
        SelectedModelId = modelId;
        Warning = null;
        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }
}
