using System.Globalization;

namespace DotNetConf.Core;

public sealed record ScheduleProposal(
    string[] SessionIds, string[] RequiredSpeakers, AvailabilityWindow[] Availability);

public sealed record AvailabilityWindow(string Start, string End, string Zone);

public sealed record ProposalReview(IReadOnlyList<Session> Sessions, IReadOnlyList<string> Errors)
{
    public bool CanApply => Sessions.Count > 0 && Errors.Count == 0;
}

public static class SchedulePlanner
{
    public const int MaximumSessions = 12;

    public static ProposalReview Review(
        ConferenceDocument conference, ScheduleProposal proposal, TimeZoneInfo timeZone,
        IReadOnlyCollection<string> savedIds)
    {
        List<string> errors = [];
        List<Session> sessions = [];
        if (proposal.SessionIds.Length is 0 or > MaximumSessions)
            errors.Add($"Propose between 1 and {MaximumSessions} sessions.");
        if (proposal.RequiredSpeakers.Length > 8 || proposal.Availability.Length > 12)
            errors.Add("Too many constraints. Use at most 8 required speakers and 12 availability windows.");
        foreach (var id in proposal.SessionIds.Take(MaximumSessions))
        {
            var session = conference.Sessions.Find(s => s.Id == id);
            if (session is null)
                errors.Add($"Unknown session ID: {id}.");
            else if (sessions.Any(s => s.Id == id))
                errors.Add($"Duplicate session: {session.Title}.");
            else
                sessions.Add(session);
        }

        List<(DateTimeOffset Start, DateTimeOffset End)> windows = [];
        foreach (var value in proposal.Availability.Take(12))
        {
            if (!TryTimeZone(value.Zone, timeZone, out var zone)
                || !TryLocalInstant(value.Start, zone, out var start)
                || !TryLocalInstant(value.End, zone, out var end) || start >= end)
                errors.Add($"Invalid or ambiguous availability: {value.Start} to {value.End} ({value.Zone}). Use yyyy-MM-ddTHH:mm and UTC or an IANA time zone.");
            else
                windows.Add((start, end));
        }

        foreach (var session in sessions)
        {
            if (proposal.Availability.Length > 0
                && !windows.Any(w => session.StartsAt >= w.Start && session.EndsAt <= w.End))
                errors.Add($"Outside your availability: {session.Title}.");
            foreach (var other in sessions.Where(s => string.CompareOrdinal(s.Id, session.Id) > 0))
                if (Agenda.Overlaps(session, other))
                    errors.Add($"Overlapping proposals: {session.Title} / {other.Title}.");
            if (!savedIds.Contains(session.Id))
                foreach (var saved in conference.Sessions.Where(s => savedIds.Contains(s.Id)))
                    if (Agenda.Overlaps(session, saved))
                        errors.Add($"Conflicts with a liked session: {session.Title} / {saved.Title}.");
        }

        foreach (var name in proposal.RequiredSpeakers.Take(8))
        {
            var matches = conference.Speakers.Where(s =>
                string.Equals(s.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                errors.Add($"Required speaker not uniquely found: {name}.");
            else if (!sessions.Any(s => s.SpeakerIds.Contains(matches[0].Id)))
                errors.Add($"Required speaker has no proposed session: {matches[0].Name}.");
        }
        return new(sessions.OrderBy(s => s.StartsAt).ToArray(), errors);
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
