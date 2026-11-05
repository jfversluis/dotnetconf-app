using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Storage;

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
    public const string PreferenceKey = "planner-model";

    public static IReadOnlyList<PlannerModelProfile> Profiles { get; } = Array.AsReadOnly<PlannerModelProfile>(
    [
        new(null, "Standard", "The regular on-device model. Tool support depends on your phone."),
        new("gemini-nano-fast", "Standard fast", "A faster standard model, if available on your phone."),
        new("gemini-nano-preview-fast", "Preview fast",
            "An experimental model that may support tools. It can require Google's developer preview."),
        new("gemini-nano-preview-full", "Preview full",
            "An experimental full model. Availability and tool support depend on your phone.")
    ]);

    private readonly IPreferences preferences;
    private readonly WeakEventManager changed = new();
    private string? selectedModelId;

    public PlannerModelSelection(IPreferences preferences, ILogger<PlannerModelSelection> logger)
    {
        this.preferences = preferences;
        try
        {
            var stored = preferences.Get(PreferenceKey, "");
            var id = string.IsNullOrEmpty(stored) ? null : stored;
            if (Profiles.Any(profile => profile.Id == id))
                selectedModelId = id;
            else
            {
                logger.LogWarning("Ignoring an unrecognized saved on-device model choice.");
                Warning = "Your saved model choice is no longer available. Standard is selected; you can choose another.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Could not read the saved on-device model choice.");
            Warning = "Couldn't read your saved model choice. Standard is selected for now.";
        }
    }

    public bool IsSupported => OperatingSystem.IsAndroidVersionAtLeast(26);
    public string? SelectedModelId => Volatile.Read(ref selectedModelId);
    public string? Warning { get; private set; }
    public event EventHandler? Changed
    {
        add => changed.AddEventHandler(value, nameof(Changed));
        remove => changed.RemoveEventHandler(value, nameof(Changed));
    }

    public void Select(string? modelId)
    {
        if (!Profiles.Any(profile => profile.Id == modelId))
            throw new ArgumentOutOfRangeException(nameof(modelId), "Unknown on-device model.");
        var previous = SelectedModelId;
        preferences.Set(PreferenceKey, modelId ?? "");
        Volatile.Write(ref selectedModelId, modelId);
        Warning = null;
        if (previous != modelId)
            changed.HandleEvent(this, EventArgs.Empty, nameof(Changed));
    }
}
