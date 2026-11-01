namespace DotNetConf.App;

public sealed class FirstRunPlannerState(bool completed, Action persistCompletion)
{
    public const string PreferenceKey = "planner-onboarding-completed-v1";
    private bool started;

    public bool TryBegin()
    {
        if (completed || started)
            return false;
        started = true;
        return true;
    }

    public void RetryLaunch() => started = false;

    public async Task CompleteAfterDismissalAsync(Func<Task> dismiss)
    {
        await dismiss();
        try
        {
            Complete();
        }
        catch
        {
            RetryLaunch();
            throw;
        }
    }

    public void Complete()
    {
        if (completed)
            return;
        persistCompletion();
        completed = true;
    }
}
