namespace DotNetConf.App.Services;

/// <summary>
/// Remembers the last session criteria the Sessions header reacted to, so repeated
/// notifications (for example after resume or a time-zone refresh) do not reveal it again.
/// </summary>
public sealed class SessionCriteriaSnapshot
{
    private (DateOnly? Day, string Query, string MatchMode, string Filters)? applied;

    /// <summary>Records the criteria and reports whether they differ from the previous record.</summary>
    /// <remarks>The first record initializes the snapshot and is not reported as a change.</remarks>
    public bool Update(DateOnly? day, string? query, string? matchMode, string? filters)
    {
        var current = (day, query ?? "", matchMode ?? "", filters ?? "");
        var changed = applied is { } previous && previous != current;
        applied = current;
        return changed;
    }
}
