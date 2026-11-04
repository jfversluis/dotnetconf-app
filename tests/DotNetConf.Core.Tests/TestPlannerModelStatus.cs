using DotNetConf.App.Services;

namespace DotNetConf.Core.Tests;

internal sealed class TestPlannerModelStatus(bool available = true, bool structuredOutput = true, bool toolCalling = false,
    string? modelId = null) : IPlannerModelStatus
{
    public int Calls { get; private set; }
    public IAsyncEnumerable<PlannerModelDownload> DownloadAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("Synthetic model does not support downloads.");
    public Task<PlannerModelState> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return Task.FromResult(new PlannerModelState(available, structuredOutput, "Model is not downloaded.")
        {
            ToolCalling = toolCalling,
            ModelId = modelId
        });
    }
}
