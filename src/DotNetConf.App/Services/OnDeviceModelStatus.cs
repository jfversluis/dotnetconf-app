using Microsoft.Maui.Essentials.AI;

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
        return new(status.Availability == ChatModelAvailability.Available,
            status.Capabilities?.Contains(ChatModelCapability.StructuredOutput) == true,
            status.Availability == ChatModelAvailability.Available ? ""
                : $"The local model is {status.Availability}: {status.UnavailableReason ?? "check your device's AI settings"}. " +
                  "Use hearts to build My Schedule, or retry when the model is ready. No model download starts automatically.")
        {
            ToolCalling = status.Capabilities?.Contains(ChatModelCapability.ToolCalling) == true
        };
#endif
    }
}
#pragma warning restore MAUIAI0001
