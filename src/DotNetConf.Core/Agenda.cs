using System.Globalization;

namespace DotNetConf.Core;

public static class Agenda
{
    public static IEnumerable<Session> Search(
        ConferenceDocument conference, string? query, DateOnly? localDay,
        IReadOnlyCollection<string>? topics, TimeZoneInfo timeZone)
    {
        var terms = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var speakers = conference.Speakers.ToDictionary(speaker => speaker.Id, speaker => speaker.Name);
        return conference.Sessions
            .Where(session => localDay is null || LocalDay(session.StartsAt, timeZone) == localDay)
            .Where(session => topics is null || topics.Count == 0
                || session.Topics.Any(topic => topics.Contains(topic, StringComparer.OrdinalIgnoreCase)))
            .Where(session =>
            {
                var text = string.Join(' ', session.Title, session.Description,
                    string.Join(' ', session.Topics), string.Join(' ', session.SpeakerIds.Select(id => speakers[id])));
                return terms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(session => session.StartsAt)
            .ThenBy(session => session.Title, StringComparer.Ordinal);
    }

    public static DateOnly LocalDay(DateTimeOffset instant, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);

    /// <summary>
    /// A short month and day in the reader's culture, in its own order and form: "Nov 10" in English,
    /// "10 de nov" in Spanish, "11月10日" in Japanese.
    /// </summary>
    public static string MonthDay(DateOnly day, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return day.ToString(culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM"), culture);
    }

    /// <summary>
    /// A short duration such as "27 days", "2 h 15 min" or "40 min". It rounds up to the minute, so a countdown never
    /// shows less time than is left.
    /// </summary>
    public static string Duration(TimeSpan span)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(span.TotalMinutes));
        var (days, hours) = (minutes / (24 * 60), minutes / 60);
        if (days >= 2)
            return $"{days} days";
        if (days == 1)
            return hours % 24 == 0 ? "1 day" : $"1 day {hours % 24} h";
        if (hours >= 1)
            return minutes % 60 == 0 ? $"{hours} h" : $"{hours} h {minutes % 60} min";
        return $"{minutes} min";
    }

    /// <summary>
    /// The stretches of time in which at least two of the sessions run at once, merged where they touch.
    /// </summary>
    public static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> OverlapRanges(IEnumerable<Session> sessions)
    {
        var ranges = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        DateTimeOffset? reach = null;
        foreach (var session in sessions.OrderBy(session => session.StartsAt))
        {
            if (reach is { } end && session.StartsAt < end)
            {
                var overlapEnd = session.EndsAt < end ? session.EndsAt : end;
                if (ranges.Count > 0 && session.StartsAt <= ranges[^1].End)
                    ranges[^1] = (ranges[^1].Start, overlapEnd > ranges[^1].End ? overlapEnd : ranges[^1].End);
                else
                    ranges.Add((session.StartsAt, overlapEnd));
            }
            reach = reach is { } current && current > session.EndsAt ? current : session.EndsAt;
        }
        return ranges;
    }

    public static bool Overlaps(Session first, Session second) =>
        first.Id != second.Id && first.StartsAt < second.EndsAt && second.StartsAt < first.EndsAt;
}
