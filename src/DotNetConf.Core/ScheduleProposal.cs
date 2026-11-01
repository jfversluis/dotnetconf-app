using System.Globalization;

namespace DotNetConf.Core;

public sealed record ScheduleProposal(
    string[] SessionIds, string[] RequiredSpeakers, AvailabilityWindow[] Availability)
{
    public bool AllSessionsByRequiredSpeakers { get; init; }
    public PlannerTopicFilter? TopicFilter { get; init; }
    public string[] MatchedSessionIds { get; init; } = [];
    public string[]? GapAnchorIds { get; init; }
}

public sealed record PlannerTopicFilter(string[] Topics, bool MatchAll, bool RestrictToRequiredSpeakers, bool IncludeAll);

public sealed record AvailabilityWindow(string Start, string End, string Zone);

public enum ProposalIssueCode
{
    SessionCount, ConstraintCount, UnknownSession, DuplicateSession, InvalidAvailability, InvalidTopics,
    MissingSpeakerScope, OutsideAvailability, OverlappingProposals, SavedConflict,
    UnknownSpeaker, AmbiguousSpeaker, MissingSpeakerSession, IncompleteSpeakerSessions, EmptyTopicMatch, IncompleteTopicSessions,
    InvalidGapSchedule, OutsideScheduleGap
}

public sealed record ProposalIssue(ProposalIssueCode Code, string Message);

public sealed record ProposalReview(IReadOnlyList<Session> Sessions, IReadOnlyList<ProposalIssue> Issues)
{
    public IReadOnlyList<string> Errors => Issues.Select(issue => issue.Message).ToArray();
    public bool CanApply => Sessions.Count > 0 && Issues.Count == 0;
}

public static class SchedulePlanner
{
    public const int MaximumSessions = 12;

    public static bool HasSameAgenda(ConferenceDocument reviewed, ConferenceDocument current) =>
        reviewed.IsMock == current.IsMock && reviewed.StartDate == current.StartDate
        && reviewed.Speakers.OrderBy(s => s.Id, StringComparer.Ordinal)
            .SequenceEqual(current.Speakers.OrderBy(s => s.Id, StringComparer.Ordinal))
        && reviewed.Sessions.Count == current.Sessions.Count
        && reviewed.Sessions.OrderBy(s => s.Id, StringComparer.Ordinal)
            .Zip(current.Sessions.OrderBy(s => s.Id, StringComparer.Ordinal)).All(pair =>
                pair.First.Id == pair.Second.Id && pair.First.Title == pair.Second.Title
                && pair.First.Description == pair.Second.Description
                && pair.First.StartsAt == pair.Second.StartsAt && pair.First.EndsAt == pair.Second.EndsAt
                && pair.First.SpeakerIds.SequenceEqual(pair.Second.SpeakerIds)
                && pair.First.Topics.SequenceEqual(pair.Second.Topics));

    public static string[] Recommend(ConferenceDocument conference, ScheduleProposal preferences,
        IEnumerable<string> candidateIds, TimeZoneInfo zone, IReadOnlyCollection<string> savedIds)
    {
        var ids = candidateIds.ToHashSet(StringComparer.Ordinal);
        var eligible = conference.Sessions.Where(s => ids.Contains(s.Id)
            && Review(conference, new([s.Id], [], preferences.Availability)
                { GapAnchorIds = preferences.GapAnchorIds }, zone, savedIds).CanApply)
            .OrderBy(s => s.EndsAt).ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
        var speakers = preferences.RequiredSpeakers.Select(n => Agenda.FindSpeakers(conference, n))
            .Select(matches => matches.Count == 1 ? matches[0].Id : "").ToArray();
        if (speakers.Length > 8)
            throw new InvalidDataException("Use at most eight required speakers.");
        var states = new Dictionary<(int Mask, int Count), string[]>[eligible.Length + 1];
        states[0] = new() { [(0, 0)] = [] };
        for (var i = 0; i < eligible.Length; i++)
        {
            var session = eligible[i];
            var previous = i;
            while (previous > 0 && eligible[previous - 1].EndsAt > session.StartsAt)
                previous--;
            var next = new Dictionary<(int Mask, int Count), string[]>(states[i]);
            var speakerMask = 0;
            for (var index = 0; index < speakers.Length; index++)
                if (session.SpeakerIds.Contains(speakers[index]))
                    speakerMask |= 1 << index;
            foreach (var (key, selected) in states[previous])
            {
                if (selected.Length >= MaximumSessions)
                    continue;
                var combined = (key.Mask | speakerMask, selected.Length + 1);
                next.TryAdd(combined, [.. selected, session.Id]);
            }
            states[i + 1] = next;
        }
        return states[^1].Where(s => s.Key.Mask == (1 << speakers.Length) - 1)
            .OrderByDescending(s => s.Key.Count).Select(s => s.Value).FirstOrDefault() ?? [];
    }

    public static ScheduleProposal Arrange(
        ConferenceDocument conference, ScheduleProposal candidates, TimeZoneInfo timeZone,
        IReadOnlyCollection<string> savedIds)
    {
        var review = Review(conference, candidates, timeZone, savedIds);
        if (candidates.AllSessionsByRequiredSpeakers || candidates.TopicFilter?.IncludeAll == true || review.Errors.Count == 0
            || review.Issues.Any(issue => issue.Code != ProposalIssueCode.OverlappingProposals))
            return candidates;

        // At most 12 model-selected alternatives: search subsets, not a greedy
        // choice that could discard the only talk by a required speaker.
        for (var count = candidates.SessionIds.Length - 1; count > 0; count--)
            for (var mask = 1; mask < (1 << candidates.SessionIds.Length); mask++)
            {
                if (System.Numerics.BitOperations.PopCount((uint)mask) != count)
                    continue;
                var subset = candidates with
                {
                    SessionIds = candidates.SessionIds.Where((_, index) => (mask & (1 << index)) != 0).ToArray()
                };
                if (Review(conference, subset, timeZone, savedIds).CanApply)
                    return subset;
            }
        return candidates;
    }

    public static ProposalReview Review(
        ConferenceDocument conference, ScheduleProposal proposal, TimeZoneInfo timeZone,
        IReadOnlyCollection<string> savedIds)
    {
        List<ProposalIssue> errors = [];
        List<Session> sessions = [];
        IReadOnlyList<ScheduleGap>? gaps = null;
        if (proposal.GapAnchorIds is { } anchors)
        {
            try
            {
                if (!anchors.ToHashSet(StringComparer.Ordinal).SetEquals(savedIds))
                    throw new InvalidDataException("Your saved schedule changed. Generate a new gap proposal before saving.");
                gaps = ScheduleGaps.Find(conference, savedIds, timeZone);
            }
            catch (InvalidDataException exception)
            {
                errors.Add(new(ProposalIssueCode.InvalidGapSchedule, exception.Message));
                gaps = [];
            }
        }
        if (proposal.SessionIds.Length is 0 or > MaximumSessions)
            errors.Add(new(ProposalIssueCode.SessionCount, $"Propose between 1 and {MaximumSessions} sessions."));
        if (proposal.RequiredSpeakers.Length > 8 || proposal.Availability.Length > 12)
            errors.Add(new(ProposalIssueCode.ConstraintCount, "Too many constraints. Use at most 8 required speakers and 12 availability windows."));
        foreach (var id in proposal.SessionIds)
        {
            var session = conference.Sessions.Find(s => s.Id == id);
            if (session is null)
                errors.Add(new(ProposalIssueCode.UnknownSession, $"Unknown session ID: {id}."));
            else if (sessions.Any(s => s.Id == id))
                errors.Add(new(ProposalIssueCode.DuplicateSession, $"Duplicate session: {session.Title}."));
            else
                sessions.Add(session);
        }

        List<(DateTimeOffset Start, DateTimeOffset End)> windows = [];
        foreach (var value in proposal.Availability.Take(12))
        {
            if (!TryTimeZone(value.Zone, timeZone, out var zone)
                || !TryLocalInstant(value.Start, zone, out var start)
                || !TryLocalInstant(value.End, zone, out var end) || start >= end)
                errors.Add(new(ProposalIssueCode.InvalidAvailability, $"Invalid or ambiguous availability: {value.Start} to {value.End} ({value.Zone}). Use yyyy-MM-ddTHH:mm and UTC or an IANA time zone."));
            else
                windows.Add((start, end));
        }
        bool FitsAvailability(Session session) => (gaps is null || ScheduleGaps.Fits(session, gaps))
            && (proposal.Availability.Length == 0 || windows.Any(w => session.StartsAt >= w.Start && session.EndsAt <= w.End));
        if (proposal.TopicFilter is { } topicFilter)
        {
            var known = conference.Sessions.SelectMany(s => s.Topics).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (topicFilter.Topics.Length is 0 or > 4 || topicFilter.Topics.Any(t => !known.Contains(t)))
                errors.Add(new(ProposalIssueCode.InvalidTopics, "Choose between 1 and 4 real agenda topics."));
            if (topicFilter.RestrictToRequiredSpeakers && proposal.RequiredSpeakers.Length == 0)
                errors.Add(new(ProposalIssueCode.MissingSpeakerScope, "Specify a speaker when restricting topic sessions to speakers."));
        }

        foreach (var session in sessions)
        {
            if (gaps is not null && (savedIds.Contains(session.Id) || !ScheduleGaps.Fits(session, gaps)))
                errors.Add(new(ProposalIssueCode.OutsideScheduleGap, $"Not in a free slot in your saved schedule: {session.Title}."));
            if (!FitsAvailability(session))
                errors.Add(new(ProposalIssueCode.OutsideAvailability, $"Outside your availability: {session.Title}."));
            foreach (var other in sessions.Where(s => string.CompareOrdinal(s.Id, session.Id) > 0))
                if (Agenda.Overlaps(session, other))
                    errors.Add(new(ProposalIssueCode.OverlappingProposals, $"Overlapping proposals: {session.Title} / {other.Title}."));
            if (!savedIds.Contains(session.Id))
                foreach (var saved in conference.Sessions.Concat(conference.WithdrawnSessions).Where(s => savedIds.Contains(s.Id)))
                    if (Agenda.Overlaps(session, saved))
                        errors.Add(new(ProposalIssueCode.SavedConflict, $"Conflicts with a liked session: {session.Title} / {saved.Title}."));
        }

        foreach (var name in proposal.RequiredSpeakers.Take(8))
        {
            var matches = Agenda.FindSpeakers(conference, name);
            if (matches.Count == 0)
                errors.Add(new(ProposalIssueCode.UnknownSpeaker, $"Speaker not found in this agenda: {name}."));
            else if (matches.Count > 1)
                errors.Add(new(ProposalIssueCode.AmbiguousSpeaker, $"Ambiguous speaker '{name}'. Choose {string.Join(" or ", matches.Select(s => s.Name))}."));
            else if (!sessions.Any(s => s.SpeakerIds.Contains(matches[0].Id)))
                errors.Add(new(ProposalIssueCode.MissingSpeakerSession, $"Required speaker has no proposed session: {matches[0].Name}."));
            if (matches.Count == 1 && proposal.AllSessionsByRequiredSpeakers)
            {
                var missing = conference.Sessions.Where(s => s.SpeakerIds.Contains(matches[0].Id)
                    && (proposal.TopicFilter is not { RestrictToRequiredSpeakers: true } scoped
                        || Agenda.MatchesTopics(s, scoped))
                    && (gaps is null || FitsAvailability(s))
                    && !proposal.SessionIds.Contains(s.Id)).ToArray();
                if (missing.Length > 0)
                    errors.Add(new(ProposalIssueCode.IncompleteSpeakerSessions, $"You requested all sessions by {matches[0].Name}. Missing: {string.Join(", ", missing.Select(s => s.Title))}. Change your request if you only want a subset."));
            }
        }
        if (proposal.AllSessionsByRequiredSpeakers && proposal.RequiredSpeakers.Length == 0)
            errors.Add(new(ProposalIssueCode.MissingSpeakerScope, "Specify a speaker when requesting all of their sessions."));
        if (proposal.TopicFilter is { IncludeAll: true } filter)
        {
            var matching = TopicMatches(conference, proposal).Where(FitsAvailability).ToArray();
            var label = string.Join(filter.MatchAll ? " AND " : " OR ", filter.Topics);
            if (matching.Length == 0)
                errors.Add(new(ProposalIssueCode.EmptyTopicMatch, $"No sessions match the requested topics within your availability: {label}."));
            var missing = matching.Where(s => !proposal.SessionIds.Contains(s.Id)).ToArray();
            if (missing.Length > 0)
                errors.Add(new(ProposalIssueCode.IncompleteTopicSessions, $"You requested all sessions matching '{label}' within your availability. Missing: {string.Join(", ", missing.Select(s => s.Title))}. Change your request if you only want a subset."));
        }
        return new(sessions.OrderBy(s => s.StartsAt).ToArray(), errors);
    }

    public static IEnumerable<Session> TopicMatches(ConferenceDocument conference, ScheduleProposal proposal)
    {
        if (proposal.TopicFilter is not { } filter)
            return [];
        var speakerIds = proposal.RequiredSpeakers.SelectMany(n => Agenda.FindSpeakers(conference, n))
            .Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        return conference.Sessions.Where(s => Agenda.MatchesTopics(s, filter)
            && (!filter.RestrictToRequiredSpeakers || s.SpeakerIds.Any(speakerIds.Contains)))
            .OrderBy(s => s.StartsAt).ThenBy(s => s.Id, StringComparer.Ordinal);
    }

    public static AvailabilityWindow[] DailyAvailability(ConferenceDocument conference, TimeZoneInfo local,
        TimeOnly start, TimeOnly end, string zoneId)
    {
        if (!TryTimeZone(zoneId, local, out var zone))
            throw new InvalidDataException("The model returned an unknown time zone.");
        var first = Agenda.LocalDay(conference.Sessions.Min(s => s.StartsAt), zone);
        var last = Agenda.LocalDay(conference.Sessions.Max(s => s.EndsAt), zone);
        if (last.DayNumber - first.DayNumber >= 12)
            throw new InvalidDataException("Recurring availability spans more than twelve days. Specify a narrower date range.");
        return Enumerable.Range(0, last.DayNumber - first.DayNumber + 1).Select(offset =>
        {
            var day = first.AddDays(offset);
            return new AvailabilityWindow(day.ToDateTime(start).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture),
                (end <= start ? day.AddDays(1) : day).ToDateTime(end).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture),
                zoneId);
        }).ToArray();
    }

    private static bool TryTimeZone(string id, TimeZoneInfo local, out TimeZoneInfo zone)
    {
        zone = local;
        if (id == "local" || id == local.Id)
            return true;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static bool TryLocalInstant(string text, TimeZoneInfo zone, out DateTimeOffset instant)
    {
        instant = default;
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var local))
            return false;
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            return false;
        instant = new DateTimeOffset(local, zone.GetUtcOffset(local));
        return true;
    }
}
