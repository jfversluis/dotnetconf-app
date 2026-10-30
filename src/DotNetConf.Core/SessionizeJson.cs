using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetConf.Core;

public static class SessionizeJson
{
    public static async Task<ConferenceDocument> ReadAsync(
        Stream stream, ConferenceDocument conference, TimeZoneInfo conferenceTimeZone,
        CancellationToken cancellationToken = default)
    {
        var data = await JsonSerializer.DeserializeAsync(
            stream, SessionizeJsonContext.Default.SessionizeDocument, cancellationToken)
            ?? throw new InvalidDataException("The Sessionize document is empty.");
        if (data.Sessions is null || data.Speakers is null || data.Categories is null || data.Rooms is null)
            throw new InvalidDataException("Sessionize sessions, speakers, categories, and rooms are required.");

        var categories = new Dictionary<int, string>();
        foreach (var category in data.Categories)
        {
            if (category is null || category.Items is null)
                throw new InvalidDataException("Sessionize categories require items.");
            foreach (var item in category.Items)
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Name) || !categories.TryAdd(item.Id, item.Name))
                    throw new InvalidDataException("Sessionize category items require unique IDs and names.");
            }
        }

        var roomIds = new HashSet<int>();
        foreach (var room in data.Rooms)
        {
            if (room is null || string.IsNullOrWhiteSpace(room.Name) || !roomIds.Add(room.Id))
                throw new InvalidDataException("Sessionize rooms require unique IDs and names.");
        }

        var speakers = new List<Speaker>();
        foreach (var speaker in data.Speakers)
        {
            if (speaker is null)
                throw new InvalidDataException("Sessionize speakers cannot be null.");
            speakers.Add(new Speaker
            {
                Id = speaker.Id,
                Name = string.IsNullOrWhiteSpace(speaker.FullName)
                    ? $"{speaker.FirstName} {speaker.LastName}".Trim() : speaker.FullName,
                Tagline = speaker.TagLine ?? "",
                Bio = speaker.Bio ?? "",
                ProfilePicture = ParseProfilePicture(speaker.ProfilePicture)
            });
        }

        var sessionIds = new HashSet<string>(StringComparer.Ordinal);
        var speakerIds = speakers.Select(speaker => speaker.Id).ToHashSet(StringComparer.Ordinal);
        var sessions = new List<Session>();
        foreach (var session in data.Sessions)
        {
            if (session is null || string.IsNullOrWhiteSpace(session.Id) ||
                string.IsNullOrWhiteSpace(session.Title) || !sessionIds.Add(session.Id))
                throw new InvalidDataException("Sessionize sessions require unique IDs and titles.");
            if (session.Speakers is null || session.CategoryItems is null ||
                session.Speakers.Any(id => !speakerIds.Contains(id)) ||
                session.Speakers.Distinct(StringComparer.Ordinal).Count() != session.Speakers.Count ||
                session.CategoryItems.Any(id => !categories.ContainsKey(id)) ||
                (session.RoomId is int roomId && !roomIds.Contains(roomId)))
                throw new InvalidDataException($"Sessionize session '{session.Id}' has invalid references.");
            // Sessionize can publish accepted talks before their schedule is announced.
            if (session.StartsAt is null && session.EndsAt is null)
                continue;
            sessions.Add(new Session
            {
                Id = session.Id,
                Title = session.Title,
                Description = session.Description ?? "",
                StartsAt = ParseTime(session.StartsAt, conferenceTimeZone),
                EndsAt = ParseTime(session.EndsAt, conferenceTimeZone),
                SpeakerIds = session.Speakers,
                Topics = session.CategoryItems.Select(id => categories[id]).Distinct(StringComparer.Ordinal).ToList()
            });
        }

        var document = conference with { Sessions = sessions, WithdrawnSessions = [], Speakers = speakers };
        ConferenceJson.Validate(document);
        return document;
    }

    private static DateTimeOffset ParseTime(string? value, TimeZoneInfo conferenceTimeZone)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("Scheduled Sessionize sessions require both start and end times.");
        value = value.Trim();
        var separator = value.IndexOf('T');
        if (separator < 0)
            throw new InvalidDataException($"Invalid Sessionize timestamp '{value}'.");
        var hasOffset = value.EndsWith('Z') || value.EndsWith('z') || value.LastIndexOf('+') > separator ||
            value.LastIndexOf('-') > separator;
        if (hasOffset)
        {
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
                return instant;
        }
        else if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (!conferenceTimeZone.IsInvalidTime(local) && !conferenceTimeZone.IsAmbiguousTime(local))
                return new DateTimeOffset(local, conferenceTimeZone.GetUtcOffset(local));
        }
        throw new InvalidDataException($"Invalid or ambiguous Sessionize timestamp '{value}'.");
    }

    private static string ParseProfilePicture(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length != 0)
            throw new InvalidDataException("Sessionize profile pictures must use absolute HTTPS URLs.");
        return SpeakerPhotoUrl.Normalize(uri.AbsoluteUri);
    }
}

public sealed record SessionizeDocument
{
    public required List<SessionizeSession> Sessions { get; init; }
    public required List<SessionizeSpeaker> Speakers { get; init; }
    public required List<SessionizeCategory> Categories { get; init; }
    public required List<SessionizeRoom> Rooms { get; init; }
}

public sealed record SessionizeSession
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? StartsAt { get; init; }
    public string? EndsAt { get; init; }
    public required List<string> Speakers { get; init; }
    public required List<int> CategoryItems { get; init; }
    public int? RoomId { get; init; }
}

public sealed record SessionizeSpeaker
{
    public required string Id { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? FullName { get; init; }
    public string? Bio { get; init; }
    public string? TagLine { get; init; }
    public string? ProfilePicture { get; init; }
}

public sealed record SessionizeCategory
{
    public required int Id { get; init; }
    public required string Title { get; init; }
    public required List<SessionizeCategoryItem> Items { get; init; }
}

public sealed record SessionizeCategoryItem
{
    public required int Id { get; init; }
    public required string Name { get; init; }
}

public sealed record SessionizeRoom
{
    public required int Id { get; init; }
    public required string Name { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SessionizeDocument))]
public partial class SessionizeJsonContext : JsonSerializerContext;
