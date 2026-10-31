namespace DotNetConf.Core;

/// <summary>
/// A session placed on a day's timeline. Overlapping sessions sit side by side in lanes.
/// <see cref="Index"/> is the session's position in the list the timeline was built from.
/// </summary>
public sealed record TimelineItem(Session Session, int Index, DateTimeOffset Start, DateTimeOffset End, int Lane, int LaneCount);
