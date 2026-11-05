namespace DotNetConf.App.Services;

public sealed record PlannerModelProfile(string? Id, string Name, string Description);

public interface IPlannerModelSelection
{
    bool IsSupported { get; }
    string? SelectedModelId { get; }
    string? Warning { get; }
    event EventHandler? Changed;
    void Select(string? modelId);
}

public sealed class PlannerModelSelection : IPlannerModelSelection
{
    public const string ToolCallingModelId = "gemini-nano-preview-fast";

    // Gemini Nano function calling is currently available through the preview-fast profile.
    public static IReadOnlyList<PlannerModelProfile> Profiles { get; } = Array.AsReadOnly<PlannerModelProfile>(
    [
        new(null, "Standard", "The regular on-device model."),
        new("gemini-nano-fast", "Standard fast", "A faster standard model."),
        new(ToolCallingModelId, "Gemini Nano 4 Fast",
            "The on-device model with function calling. Requires a supported device enrolled in Google's AICore Developer Preview."),
        new("gemini-nano-preview-full", "Preview full", "An experimental full model.")
    ]);

    public bool IsSupported => false;
    public string? SelectedModelId => ToolCallingModelId;
    public string? Warning => null;
    public event EventHandler? Changed { add { } remove { } }

    public void Select(string? modelId)
    {
        if (modelId != ToolCallingModelId)
            throw new ArgumentOutOfRangeException(nameof(modelId), "The Android planner uses Gemini Nano 4 Fast for function calling.");
    }
}
