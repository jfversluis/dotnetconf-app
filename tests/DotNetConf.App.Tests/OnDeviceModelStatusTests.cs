using DotNetConf.App.Services;
using Microsoft.Maui.Essentials.AI;

namespace DotNetConf.App.Tests;

#pragma warning disable MAUIAI0001
public sealed class OnDeviceModelStatusTests
{
    [Theory]
    [InlineData(ChatModelAvailability.Available, PlannerModelSetup.Ready)]
    [InlineData(ChatModelAvailability.NotReady, PlannerModelSetup.DownloadRequired)]
    [InlineData(ChatModelAvailability.Downloading, PlannerModelSetup.Downloading)]
    [InlineData(ChatModelAvailability.Unavailable, PlannerModelSetup.Unavailable)]
    [InlineData(ChatModelAvailability.Unknown, PlannerModelSetup.Unavailable)]
    public void AndroidAvailabilityAndCapabilitiesDescribeTheSelectedProfile(
        ChatModelAvailability availability, PlannerModelSetup setup)
    {
        var state = OnDeviceModelStatus.MapStatus(availability, [ChatModelCapability.ToolCalling],
            true, "gemini-nano-preview-fast");

        Assert.Equal(setup, state.Setup);
        Assert.Equal(setup == PlannerModelSetup.Ready, state.Available);
        Assert.True(state.ToolCalling);
        Assert.False(state.StructuredOutput);
        Assert.Equal("gemini-nano-preview-fast", state.ModelId);
        Assert.Equal(setup == PlannerModelSetup.Ready, state.Reason.Length == 0);
    }

    [Theory]
    [InlineData(ChatModelAvailability.NotReady)]
    [InlineData(ChatModelAvailability.Disabled)]
    [InlineData(ChatModelAvailability.NotSupported)]
    [InlineData(ChatModelAvailability.Unknown)]
    public void AppleAvailabilityNeverOffersAnAndroidDownload(ChatModelAvailability availability)
    {
        var state = OnDeviceModelStatus.MapStatus(availability, null, false, "ignored");
        Assert.False(state.Available);
        Assert.Equal(PlannerModelSetup.Unavailable, state.Setup);
        Assert.False(state.ToolCalling);
        Assert.Null(state.ModelId);
        Assert.Equal("On-device planning isn't available right now. Check that your device supports a local model and that it is enabled. You can always save talks with hearts in Sessions.", state.Reason);
    }

    [Fact]
    public void AppleReadyPreservesStructuredOutputAndToolCapabilities()
    {
        var state = OnDeviceModelStatus.MapStatus(ChatModelAvailability.Available,
            [ChatModelCapability.StructuredOutput, ChatModelCapability.ToolCalling], false, null);
        Assert.True(state.Available);
        Assert.True(state.StructuredOutput);
        Assert.True(state.ToolCalling);
        Assert.Equal(PlannerModelSetup.Ready, state.Setup);
        Assert.Empty(state.Reason);
    }
}
#pragma warning restore MAUIAI0001
