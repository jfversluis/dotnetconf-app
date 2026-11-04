using Microsoft.Maui.Essentials.AI;
using System.Runtime.CompilerServices;

namespace DotNetConf.App.Services;

#pragma warning disable MAUIAI0001
public sealed class OnDeviceModelStatus : IPlannerModelStatus
{
    public async Task<PlannerModelState> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
#if IOS
        if (!OperatingSystem.IsIOSVersionAtLeast(26))
            return new(false, false, "Apple Intelligence requires iOS 26 or later.");
        var status = await AppleIntelligenceChatClient.GetModelStatusAsync(cancellationToken);
#elif ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return new(false, false, "The local model requires Android 8 or later.");
        var status = await GeminiNanoChatClient.GetModelStatusAsync(cancellationToken);
#else
        await Task.CompletedTask;
        return new(false, false, "On-device planning is not supported on this platform. Use hearts to build My Schedule.");
#endif
#if IOS || ANDROID
        var setup = status.Availability == ChatModelAvailability.Available
            ? PlannerModelSetup.Ready : PlannerModelSetup.Unavailable;
        var reason = "On-device planning isn't available right now. Check that your device supports a local model and that it is enabled. You can always save talks with hearts in Sessions.";
#if ANDROID
        // The pinned preview exposes ML Kit's download states only through UnavailableReason.
        setup = status.UnavailableReason switch
        {
            "DOWNLOADABLE (1)" => PlannerModelSetup.DownloadRequired,
            "DOWNLOADING (2)" => PlannerModelSetup.Downloading,
            _ => setup
        };
        reason = setup switch
        {
            PlannerModelSetup.DownloadRequired => "Your phone supports on-device planning. Download Gemini Nano once to get started.",
            PlannerModelSetup.Downloading => "Android is downloading Gemini Nano. We'll check when it's ready.",
            _ => "Gemini Nano isn't available right now. Check for Android AICore updates in Google Play, then try again. Device support and model settings affect availability. You can still save talks with hearts in Sessions."
        };
#endif
        return new(status.Availability == ChatModelAvailability.Available,
            status.Capabilities?.Contains(ChatModelCapability.StructuredOutput) == true,
            setup == PlannerModelSetup.Ready ? "" : reason)
        {
            Setup = setup,
            ToolCalling = status.Capabilities?.Contains(ChatModelCapability.ToolCalling) == true
        };
#endif
    }

#if ANDROID
    public async IAsyncEnumerable<PlannerModelDownload> DownloadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var state = await ReadAsync(cancellationToken);
        if (state.Setup != PlannerModelSetup.DownloadRequired)
        {
            if (!state.Available && state.Setup != PlannerModelSetup.Downloading)
                throw new NotSupportedException(state.Reason);
            yield break;
        }
        await foreach (var update in GeminiNanoChatClient.DownloadModelAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (update.State != GeminiNanoDownloadState.Completed)
                yield return new(update.BytesDownloaded, update.BytesToDownload);
        }
    }
#else
    public IAsyncEnumerable<PlannerModelDownload> DownloadAsync(CancellationToken cancellationToken) =>
        throw new PlatformNotSupportedException("Model downloads are managed by your device's AI settings on this platform.");
#endif
}
#pragma warning restore MAUIAI0001
