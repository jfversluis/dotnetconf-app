namespace DotNetConf.Core;

public static partial class SchedulePlanner
{
    public static ScheduleProposal Discover(ConferenceDocument conference, TimeZoneInfo zone,
        IReadOnlyCollection<string> savedIds, bool fillGaps = false, Random? random = null, int count = 6,
        AvailabilityWindow[]? availability = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        random ??= Random.Shared;
        var preferences = new ScheduleProposal([], [], availability?.ToArray() ?? [])
        {
            GapAnchorIds = fillGaps ? savedIds.Order(StringComparer.Ordinal).ToArray() : null
        };
        if (fillGaps)
            ScheduleGaps.Find(conference, savedIds, zone);
        var saved = savedIds.ToHashSet(StringComparer.Ordinal);
        var eligible = conference.Sessions.Where(session => !saved.Contains(session.Id)
                && Review(conference, preferences with { SessionIds = [session.Id] }, zone, savedIds).CanApply)
            .OrderBy(session => session.Id, StringComparer.Ordinal).ToArray();
        if (eligible.Length == 0)
            return preferences;

        var byId = eligible.ToDictionary(session => session.Id, StringComparer.Ordinal);
        var best = Recommend(conference, preferences, byId.Keys, zone, savedIds, count)
            .Select(id => byId[id]).ToArray();
        var bestScore = DiscoveryScore(best, zone);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var remaining = eligible.ToArray();
            random.Shuffle(remaining);
            List<Session> selected = [];
            HashSet<string> topics = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> speakers = new(StringComparer.Ordinal);
            HashSet<DateOnly> days = [];
            while (remaining.Length > 0 && selected.Count < count)
            {
                var next = attempt % 2 == 0
                    ? remaining.OrderByDescending(session => session.Topics.Count(topic => !topics.Contains(topic)))
                        .ThenByDescending(session => session.SpeakerIds.Count(speaker => !speakers.Contains(speaker)))
                        .ThenByDescending(session => !days.Contains(Agenda.LocalDay(session.StartsAt, zone))).First()
                    : remaining[0];
                selected.Add(next);
                topics.UnionWith(next.Topics);
                speakers.UnionWith(next.SpeakerIds);
                days.Add(Agenda.LocalDay(next.StartsAt, zone));
                remaining = remaining.Where(session => session.Id != next.Id && !Agenda.Overlaps(session, next))
                    .ToArray();
            }
            var score = DiscoveryScore(selected, zone);
            if (score.CompareTo(bestScore) > 0 || score == bestScore && random.Next(2) == 0)
            {
                best = selected.ToArray();
                bestScore = score;
            }
        }
        var ids = best.OrderBy(session => session.StartsAt).ThenBy(session => session.Id, StringComparer.Ordinal)
            .Select(session => session.Id).ToArray();
        return preferences with { SessionIds = ids, MatchedSessionIds = ids.ToArray() };
    }

    private static (int Count, int Topics, int Speakers, int Days) DiscoveryScore(
        IEnumerable<Session> sessions, TimeZoneInfo zone)
    {
        var selected = sessions.ToArray();
        return (selected.Length,
            selected.SelectMany(session => session.Topics).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            selected.SelectMany(session => session.SpeakerIds).Distinct(StringComparer.Ordinal).Count(),
            selected.Select(session => Agenda.LocalDay(session.StartsAt, zone)).Distinct().Count());
    }
}
