namespace DotNetConf.App;

internal sealed class OnboardingPlannerPreparation
{
    private Task? preparation;
    private int visit;

    public bool HasStarted => preparation is not null;

    public void Leave() => visit++;

    public async Task<bool> EnterAsync(
        Func<Task> prepare, Func<Task> resume, Func<bool> isActive, Func<int> cancellationRevision)
    {
        var currentVisit = ++visit;
        var cancellationAtEntry = cancellationRevision();
        var firstVisit = preparation is null;
        preparation ??= prepare();
        await preparation;
        if (currentVisit != visit || !isActive())
            return false;
        if (!firstVisit && cancellationRevision() == cancellationAtEntry)
            await resume();
        return currentVisit == visit && isActive();
    }
}
