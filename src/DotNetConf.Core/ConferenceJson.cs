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
        if (document.Speakers is null || document.Sessions is null)
            throw new InvalidDataException("Session and speaker collections are required.");

        var speakerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var speaker in document.Speakers)
        {
            if (speaker is null || string.IsNullOrWhiteSpace(speaker.Id) || string.IsNullOrWhiteSpace(speaker.Name) ||
                !speakerIds.Add(speaker.Id))
                throw new InvalidDataException("Speakers require unique IDs and non-empty names.");
        }

        var sessionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in document.Sessions)
        {
            if (session is null || string.IsNullOrWhiteSpace(session.Id) || string.IsNullOrWhiteSpace(session.Title) ||
                !sessionIds.Add(session.Id))
                throw new InvalidDataException("Sessions require unique IDs and non-empty titles.");
            if (session.StartsAt == default || session.EndsAt <= session.StartsAt)
                throw new InvalidDataException($"Session '{session.Id}' has invalid start/end times.");
            if (session.SpeakerIds is null || session.Topics is null ||
                session.SpeakerIds.Any(id => !speakerIds.Contains(id)) ||
                session.SpeakerIds.Distinct(StringComparer.Ordinal).Count() != session.SpeakerIds.Count ||
                session.Topics.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"Session '{session.Id}' has invalid speaker references or topics.");
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ConferenceDocument))]
[JsonSerializable(typeof(string[]))]
public partial class ConferenceJsonContext : JsonSerializerContext;
