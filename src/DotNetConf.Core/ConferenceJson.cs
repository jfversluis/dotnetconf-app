using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetConf.Core;

public static class ConferenceJson
{
    public static async Task<ConferenceDocument> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var document = await JsonSerializer.DeserializeAsync(
            stream, ConferenceJsonContext.Default.ConferenceDocument, cancellationToken)
            ?? throw new InvalidDataException("The conference document is empty.");
        Validate(document);
        return document;
    }

    public static void Validate(ConferenceDocument document)
    {
        if (document.SchemaVersion != 1)
            throw new InvalidDataException("Unsupported conference schema version.");
        if (string.IsNullOrWhiteSpace(document.Name) || string.IsNullOrWhiteSpace(document.Description))
            throw new InvalidDataException("Conference name and description are required.");
        if (!Uri.TryCreate(document.WebsiteUrl, UriKind.Absolute, out var website) || website.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("The conference website must be an absolute HTTPS URL.");
        if (document.Speakers is null || document.WithdrawnSpeakers is null ||
            document.Sessions is null || document.WithdrawnSessions is null ||
            document.CommunityEvents is null || document.SessionAliases is null)
            throw new InvalidDataException("Session, speaker, and community event collections are required.");

        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var communityEvent in document.CommunityEvents)
        {
            if (communityEvent is null || string.IsNullOrWhiteSpace(communityEvent.Id) ||
                string.IsNullOrWhiteSpace(communityEvent.Name) || !eventIds.Add(communityEvent.Id) ||
                string.IsNullOrWhiteSpace(communityEvent.Venue) || string.IsNullOrWhiteSpace(communityEvent.City) ||
                string.IsNullOrWhiteSpace(communityEvent.Country) ||
                communityEvent.Kind is not ("watch-party" or "community-conference"))
                throw new InvalidDataException("Community events require unique IDs, names, locations, and a supported kind.");
            if (!double.IsFinite(communityEvent.Latitude) || communityEvent.Latitude is < -90 or > 90 ||
                !double.IsFinite(communityEvent.Longitude) || communityEvent.Longitude is < -180 or > 180)
                throw new InvalidDataException($"Community event '{communityEvent.Id}' has invalid coordinates.");
            if (!Uri.TryCreate(communityEvent.WebsiteUrl, UriKind.Absolute, out var eventWebsite) ||
                eventWebsite.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException($"Community event '{communityEvent.Id}' requires an absolute HTTPS website.");
        }

        var speakerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var speaker in document.Speakers.Concat(document.WithdrawnSpeakers))
        {
            if (speaker is null || string.IsNullOrWhiteSpace(speaker.Id) || string.IsNullOrWhiteSpace(speaker.Name) ||
                !speakerIds.Add(speaker.Id))
                throw new InvalidDataException("Speakers require unique IDs and non-empty names.");
        }

        var currentSpeakerIds = document.Speakers.Select(speaker => speaker.Id).ToHashSet(StringComparer.Ordinal);
        var sessionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (sessions, allowedSpeakerIds) in new[]
        {
            (document.Sessions, currentSpeakerIds), (document.WithdrawnSessions, speakerIds)
        })
        foreach (var session in sessions)
        {
            if (session is null || string.IsNullOrWhiteSpace(session.Id) || string.IsNullOrWhiteSpace(session.Title) ||
                !sessionIds.Add(session.Id))
                throw new InvalidDataException("Sessions require unique IDs and non-empty titles.");
            if (session.StartsAt == default || session.EndsAt <= session.StartsAt)
                throw new InvalidDataException($"Session '{session.Id}' has invalid start/end times.");
            if (session.SpeakerIds is null || session.Topics is null ||
                session.SpeakerIds.Any(id => !allowedSpeakerIds.Contains(id)) ||
                session.SpeakerIds.Distinct(StringComparer.Ordinal).Count() != session.SpeakerIds.Count ||
                session.Topics.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"Session '{session.Id}' has invalid speaker references or topics.");
        }

        foreach (var (alias, canonical) in document.SessionAliases)
        {
            if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(canonical) ||
                sessionIds.Contains(alias) || !sessionIds.Contains(canonical) ||
                document.SessionAliases.ContainsKey(canonical))
                throw new InvalidDataException("Session aliases must map retired IDs directly to existing sessions.");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ConferenceDocument))]
[JsonSerializable(typeof(string[]))]
public partial class ConferenceJsonContext : JsonSerializerContext;
