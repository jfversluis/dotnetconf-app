namespace DotNetConf.Core;

public sealed record ScheduleGap(DateTimeOffset StartsAt, DateTimeOffset EndsAt);

public static class ScheduleGaps
{
    // Only days with saved commitments are planned. Include time before/after
    // them inside the real agenda's bounds; do not assume a nine-to-five day.
    public static IReadOnlyList<ScheduleGap> Find(ConferenceDocument conference,
        IReadOnlyCollection<string> savedIds, TimeZoneInfo zone)
    {
        if (savedIds.Count == 0)
            throw new InvalidDataException("Save a session first, or use the personal planner to start your schedule.");
        var ids = savedIds.ToHashSet(StringComparer.Ordinal);
        var retained = conference.Sessions.Concat(conference.WithdrawnSessions).ToArray();
        var known = retained.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Any(id => !known.Contains(id)))
            throw new InvalidDataException("Some saved sessions are missing from the current agenda. Refresh the agenda and review My Schedule before filling gaps.");
        var saved = retained.Where(s => ids.Contains(s.Id)).ToArray();
        List<ScheduleGap> result = [];
        foreach (var day in conference.Sessions.GroupBy(s => Agenda.LocalDay(s.StartsAt, zone)).OrderBy(g => g.Key))
        {
            var start = day.Min(s => s.StartsAt);
            var end = day.Max(s => s.EndsAt);
            var commitments = saved.Where(s => Agenda.LocalDay(s.StartsAt, zone) == day.Key
                    || s.StartsAt < end && s.EndsAt > start)
                .OrderBy(s => s.StartsAt).ThenBy(s => s.EndsAt).ToArray();
            if (commitments.Length == 0)
                continue;
            var cursor = start;
            foreach (var session in commitments)
            {
                var freeEnd = session.StartsAt < end ? session.StartsAt : end;
                if (freeEnd > cursor)
                    result.Add(new(cursor, freeEnd));
                if (session.EndsAt > cursor)
                    cursor = session.EndsAt;
            }
            if (cursor < end)
                result.Add(new(cursor, end));
        }
        return result;
    }

    public static bool Fits(Session session, IReadOnlyList<ScheduleGap> gaps) =>
        gaps.Any(g => session.StartsAt >= g.StartsAt && session.EndsAt <= g.EndsAt);
}
