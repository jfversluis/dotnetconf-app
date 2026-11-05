using Microsoft.Maui.Essentials.AI;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace DotNetConf.App.Services;

#pragma warning disable MAUIAI0001
public sealed class OnDeviceModelStatus : IPlannerModelStatus
{
#if ANDROID
    private readonly IPlannerModelSelection selection;
#endif
#if IOS || ANDROID
    private readonly ILogger<OnDeviceModelStatus> logger;
#endif

    public OnDeviceModelStatus(IPlannerModelSelection selection, ILogger<OnDeviceModelStatus> logger)
    {
#if ANDROID
        this.selection = selection;
#endif
#if IOS || ANDROID
        this.logger = logger;
#endif
    }

    public async Task<PlannerModelState> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
#if IOS
        if (!OperatingSystem.IsIOSVersionAtLeast(26))
            return new(false, false, "Apple Intelligence requires iOS 26 or later.");
        var status = await AppleIntelligenceChatClient.GetModelStatusAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var state = MapStatus(status.Availability, status.Capabilities, false, null);
        if (state.Setup == PlannerModelSetup.Unavailable)
            logger.LogWarning("On-device model is {Availability}.", status.Availability);
        return state;
#elif ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return new(false, false, "The local model requires Android 8 or later.");
        return await ReadAndroidAsync(selection.SelectedModelId, cancellationToken);
#else
        await Task.CompletedTask;
        return new(false, false, "On-device planning is not supported on this platform. Use hearts to build My Schedule.");
#endif
    }

    internal static PlannerModelState MapStatus(ChatModelAvailability availability,
        IReadOnlyCollection<ChatModelCapability>? capabilities, bool isAndroid, string? modelId)
    {
        var setup = availability switch
        {
            ChatModelAvailability.Available => PlannerModelSetup.Ready,
            ChatModelAvailability.NotReady when isAndroid => PlannerModelSetup.DownloadRequired,
            ChatModelAvailability.Downloading when isAndroid => PlannerModelSetup.Downloading,
            _ => PlannerModelSetup.Unavailable
        };
        var reason = isAndroid
            ? setup switch
            {
                PlannerModelSetup.DownloadRequired => "Download the selected Gemini Nano model once to get started.",
                PlannerModelSetup.Downloading => "Android is downloading the selected model. We'll check when it's ready.",
                _ => "This Gemini Nano model isn't available right now. Choose another model or check Android AICore and your device's AI settings. You can still save talks with hearts in Sessions."
            }
            : "On-device planning isn't available right now. Check that your device supports a local model and that it is enabled. You can always save talks with hearts in Sessions.";
        return new(setup == PlannerModelSetup.Ready,
            capabilities?.Contains(ChatModelCapability.StructuredOutput) == true,
            setup == PlannerModelSetup.Ready ? "" : reason)
        {
            Setup = setup,
            ToolCalling = capabilities?.Contains(ChatModelCapability.ToolCalling) == true,
            ModelId = isAndroid ? modelId : null
        };
    }

#if ANDROID
    private async Task<PlannerModelState> ReadAndroidAsync(string? modelId, CancellationToken cancellationToken)
    {
        var status = await GeminiNanoChatClient.GetModelStatusAsync(modelId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var state = MapStatus(status.Availability, status.Capabilities, true, modelId);
        if (state.Setup == PlannerModelSetup.Unavailable)
            logger.LogWarning("Gemini Nano model {ModelId} is {Availability}.", modelId ?? "standard", status.Availability);
        return state;
    }

    public async IAsyncEnumerable<PlannerModelDownload> DownloadAsync(string? modelId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var state = await ReadAndroidAsync(modelId, cancellationToken);
        if (state.Setup != PlannerModelSetup.DownloadRequired)
        {
            if (!state.Available && state.Setup != PlannerModelSetup.Downloading)
                throw new NotSupportedException(state.Reason);
            yield break;
        }
        await foreach (var update in GeminiNanoChatClient.DownloadModelAsync(modelId, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (update.State != GeminiNanoDownloadState.Completed)
                yield return new(update.BytesDownloaded, update.BytesToDownload);
        }
    }
#else
    public IAsyncEnumerable<PlannerModelDownload> DownloadAsync(string? modelId, CancellationToken cancellationToken) =>
        throw new PlatformNotSupportedException("Model downloads are managed by your device's AI settings on this platform.");
#endif
}
#pragma warning restore MAUIAI0001
