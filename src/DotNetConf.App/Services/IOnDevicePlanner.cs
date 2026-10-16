using DotNetConf.Core;

namespace DotNetConf.App.Services;

public interface IOnDevicePlanner
{
    string? UnsupportedReason { get; }
    Task CheckReadinessAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PlannerStartingPoint>> SuggestStartingPointsAsync(
        ConferenceDocument conference, CancellationToken cancellationToken);
    Task<PlannerResult> ProposeAsync(ConferenceDocument conference, string request,
        TimeZoneInfo zone, string[] savedIds, CancellationToken cancellationToken);
}

public interface IPlannerScheduleUpdates
{
    void RefreshSavedState();
    Task<bool> RefreshRemindersAsync();
}
