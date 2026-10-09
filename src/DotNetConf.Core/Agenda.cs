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

    public static bool Overlaps(Session first, Session second) =>
        first.Id != second.Id && first.StartsAt < second.EndsAt && second.StartsAt < first.EndsAt;
}
