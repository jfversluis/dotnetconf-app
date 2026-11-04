using DotNetConf.Core;

namespace DotNetConf.App.Services;

public interface IOnDevicePlanner
{
    string? UnsupportedReason { get; }
    Task<PlannerModelState> CheckReadinessAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<PlannerModelDownload> DownloadModelAsync(string? modelId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlannerStartingPoint>> SuggestStartingPointsAsync(
        ConferenceDocument conference, CancellationToken cancellationToken);
    Task<PlannerResult> ProposeAsync(ConferenceDocument conference, string request,
        TimeZoneInfo zone, string[] savedIds, CancellationToken cancellationToken, bool fillGaps = false,
        PlannerWatchingHours? watchingHours = null);
}

public interface IPlannerScheduleUpdates
{
    Task<PlannerSaveResult?> AddPlannerProposalAsync(ConferenceDocument agenda, ScheduleProposal proposal,
        TimeZoneInfo zone, CancellationToken cancellationToken = default);
    Task<bool> RefreshRemindersAsync();
}

public sealed record PlannerSaveResult(ProposalReview Review, bool PresentationUpdated);

public sealed record PlannerModelState(bool Available, bool StructuredOutput, string Reason)
{
    public bool ToolCalling { get; init; }
    public string? ModelId { get; init; }
    public PlannerModelSetup Setup { get; init; } = Available ? PlannerModelSetup.Ready : PlannerModelSetup.Unavailable;
}

public enum PlannerModelSetup { Unavailable, Ready, DownloadRequired, Downloading }

public sealed record PlannerModelDownload(long? BytesDownloaded, long? TotalBytes);

public interface IPlannerModelStatus
{
    Task<PlannerModelState> ReadAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<PlannerModelDownload> DownloadAsync(string? modelId, CancellationToken cancellationToken);
}
