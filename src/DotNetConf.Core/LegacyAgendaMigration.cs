using System.Text;

namespace DotNetConf.Core;

/// <summary>Projects compatible cached agenda records into the current single-source format.</summary>
internal static class LegacyAgendaMigration
{
    internal static ConferenceCache Prepare(ConferenceCache cache) => cache with
    {
        Microsoft = SeparateProfiles(cache.Microsoft) with { RequiresProfileRefresh = true },
        Community = SeparateProfiles(cache.Community!)
    };

    private static CachedAgenda SeparateProfiles(CachedAgenda agenda)
    {
        var activeIds = agenda.Sessions.SelectMany(session => session.SpeakerIds).ToHashSet(StringComparer.Ordinal);
        var withdrawnIds = agenda.WithdrawnSessions.SelectMany(session => session.SpeakerIds).ToHashSet(StringComparer.Ordinal);
        var speakers = agenda.Speakers.Concat(agenda.WithdrawnSpeakers).ToList();
        var archived = speakers.Where(speaker => !activeIds.Contains(speaker.Id) && withdrawnIds.Contains(speaker.Id)).ToList();
        return agenda with
        {
            Speakers = speakers.Where(speaker => activeIds.Contains(speaker.Id) || !withdrawnIds.Contains(speaker.Id)).ToList(),
            WithdrawnSpeakers = archived,
            // Older caches mixed upstream and archived profiles; a full parse resolves ambiguous membership.
            RequiresProfileRefresh = archived.Count != 0
        };
    }

    internal static ConferenceCache Upgrade(ConferenceCache cache)
    {
        if (cache.Community is not { PayloadHash.Length: > 0, LastCheckedAt: not null } community)
            return cache with { Community = null };

        var microsoft = cache.Microsoft;
        var ids = microsoft.Sessions.Concat(microsoft.WithdrawnSessions)
            .Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
        var retired = community.Sessions.Concat(community.WithdrawnSessions).ToList();
        if (retired.Any(session => !ids.Add(session.Id)))
            throw new InvalidDataException("Legacy source session IDs collide with Microsoft session IDs.");
        var activeSpeakerIds = microsoft.Speakers.Select(speaker => speaker.Id).ToHashSet(StringComparer.Ordinal);
        var profiles = microsoft.WithdrawnSpeakers.Concat(community.Speakers).Concat(community.WithdrawnSpeakers)
            .Where(speaker => !activeSpeakerIds.Contains(speaker.Id)).DistinctBy(speaker => speaker.Id, StringComparer.Ordinal);
        var withdrawn = microsoft.WithdrawnSessions.Concat(retired).ToList();
        var neededSpeakers = withdrawn.SelectMany(session => session.SpeakerIds).ToHashSet(StringComparer.Ordinal);
        return Resolve(cache with
        {
            Community = null,
            LegacyPayloadHash = community.PayloadHash,
            LegacySessionIds = retired.Select(session => session.Id).ToList(),
            LegacyActiveSessionIds = community.Sessions.Select(session => session.Id).ToList(),
            Microsoft = microsoft with
            {
                WithdrawnSessions = withdrawn,
                WithdrawnSpeakers = profiles.Where(speaker => neededSpeakers.Contains(speaker.Id)).ToList()
            }
        });
    }

    internal static ConferenceCache Resolve(ConferenceCache cache, bool retireUnmatched = false)
    {
        if (cache.LegacySessionIds.Count == 0)
            return cache;

        var agenda = cache.Microsoft;
        var speakers = agenda.Speakers.Concat(agenda.WithdrawnSpeakers)
            .ToDictionary(speaker => speaker.Id, StringComparer.Ordinal);
        var legacyIds = cache.LegacySessionIds.ToHashSet(StringComparer.Ordinal);
        var candidates = agenda.Sessions.Concat(agenda.WithdrawnSessions)
            .Where(session => !legacyIds.Contains(session.Id))
            .GroupBy(session => Key(session, speakers)).ToDictionary(group => group.Key, group => group.ToArray());
        var retired = agenda.WithdrawnSessions.Where(session => legacyIds.Contains(session.Id))
            .GroupBy(session => Key(session, speakers));
        var aliases = new Dictionary<string, string>(cache.SessionAliases, StringComparer.Ordinal);
        var resolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in retired)
        {
            // Empty speakers and repeated presentations do not establish an unambiguous cross-source identity.
            if (group.Key.Speakers.Length == 0 || group.Count() != 1 ||
                !candidates.TryGetValue(group.Key, out var matches) || matches.Length != 1)
                continue;
            var source = group.Single();
            aliases.Add(source.Id, matches[0].Id);
            resolved.Add(source.Id);
        }

        var remaining = agenda.WithdrawnSessions.Where(session => !resolved.Contains(session.Id)).ToList();
        var neededSpeakers = remaining.SelectMany(session => session.SpeakerIds).ToHashSet(StringComparer.Ordinal);
        return cache with
        {
            SessionAliases = aliases,
            LegacySessionIds = cache.LegacySessionIds.Where(id => !resolved.Contains(id)).ToList(),
            LegacyActiveSessionIds = retireUnmatched ? [] :
                cache.LegacyActiveSessionIds.Where(id => !resolved.Contains(id)).ToList(),
            Microsoft = agenda with
            {
                WithdrawnSessions = remaining,
                WithdrawnSpeakers = agenda.WithdrawnSpeakers.Where(speaker => neededSpeakers.Contains(speaker.Id)).ToList()
            }
        };
    }

    private static SessionKey Key(Session session, IReadOnlyDictionary<string, Speaker> speakers)
    {
        var names = new List<string>();
        foreach (var id in session.SpeakerIds)
        {
            if (!speakers.TryGetValue(id, out var speaker))
                throw new InvalidDataException($"Legacy session '{session.Id}' has an unknown speaker reference.");
            names.Add(Normalize(speaker.Name));
        }
        return new(Normalize(session.Title), session.StartsAt, session.EndsAt,
            string.Join("\n", names.Order(StringComparer.Ordinal)));
    }

    private static string Normalize(string value) =>
        string.Join(" ", value.Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();

    private sealed record SessionKey(string Title, DateTimeOffset Start, DateTimeOffset End, string Speakers);
}
