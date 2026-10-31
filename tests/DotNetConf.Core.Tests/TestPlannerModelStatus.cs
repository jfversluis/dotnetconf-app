using DotNetConf.App.Services;

namespace DotNetConf.Core.Tests;

internal sealed class TestPlannerModelStatus(bool available = true, bool structuredOutput = true) : IPlannerModelStatus
{
    public int Calls { get; private set; }
    public Task<PlannerModelState> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return Task.FromResult(new PlannerModelState(available, structuredOutput, "Model is not downloaded."));
    }
}
