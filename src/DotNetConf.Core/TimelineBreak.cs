namespace DotNetConf.Core;

/// <summary>
/// A long gap between sessions, drawn with a fixed length instead of to scale. It runs from the last session
/// before it ending to the next one starting.
/// </summary>
public sealed record TimelineBreak(DateTimeOffset Start, DateTimeOffset End, double Offset);
