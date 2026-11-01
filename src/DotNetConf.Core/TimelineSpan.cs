namespace DotNetConf.Core;

/// <summary>
/// A continuous stretch of a day's timeline, starting <see cref="Offset"/> minutes from the top.
/// </summary>
public sealed record TimelineSpan(DateTimeOffset Start, DateTimeOffset End, double Offset)
{
    public double Length => (End - Start).TotalMinutes;
}
