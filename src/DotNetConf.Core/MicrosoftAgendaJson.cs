using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetConf.Core;

public static class MicrosoftAgendaJson
{
    public static async Task<ConferenceDocument> ReadAsync(
        Stream stream, ConferenceDocument previous, CancellationToken cancellationToken = default)
    {
        if (previous.StartDate is not DateOnly firstDay)
            throw new InvalidDataException("The conference start date is required for the Microsoft agenda.");

        var data = await JsonSerializer.DeserializeAsync(
            stream, RemoteAgendaJsonContext.Default.MicrosoftAgenda, cancellationToken)
            ?? throw new InvalidDataException("The Microsoft agenda is null.");
        if (data.Sessions is null || data.Speakers is null || data.Sessions.Count == 0)
            throw new InvalidDataException("Microsoft sessions and speakers are required; an empty agenda is not an update.");
        var pacific = GetPacificTimeZone();

        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        var speakers = new List<Speaker>();
        foreach (var source in data.Speakers)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Id) || !sourceIds.Add(source.Id))
                throw new InvalidDataException("Microsoft speakers require unique IDs.");
            if (!IsUseful(source.Name))
                continue;
            speakers.Add(new Speaker
            {
                Id = source.Id,
                Name = source.Name!,
                Bio = string.Join("\n\n", new[] { source.PrimaryBio, source.AdditionalBio }
                    .Where(IsUseful)),
                ProfilePicture = SpeakerPhotoUrl.Normalize(source.Avatar)
            });
        }
        var sessions = new List<Session>();
        foreach (var source in data.Sessions)
        {
            if (source is null || source.SpeakerIds is null || source.SpeakerIds.Any(string.IsNullOrWhiteSpace) ||
                source.Topics is null || source.Day < 1 || source.Timezone != "PST")
                throw new InvalidDataException("Microsoft sessions require valid days, Pacific timezone, and references.");
            sessions.Add(new Session
            {
                Id = source.Id,
                Title = source.Title ?? "",
                Description = IsUseful(source.Abstract) ? source.Abstract! : "",
                StartsAt = Time(source.StartTime, source.Day, firstDay, source.Id, pacific),
                EndsAt = Time(source.EndTime, source.Day, firstDay, source.Id, pacific),
                // This sentinel represents an unanswered optional co-speaker, not a person.
                SpeakerIds = source.SpeakerIds.Where(id => id != "speaker-no-response").ToList(),
                Topics = source.Topics
            });
        }
        var document = previous with { Sessions = sessions, WithdrawnSessions = [], Speakers = speakers };
        ConferenceJson.Validate(document);
        return RetainCachedDetails(document, previous);
    }

    internal static TimeZoneInfo GetPacificTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidDataException("Pacific timezone data is unavailable. Install system timezone data to load the agenda.", exception);
        }
    }

    private static DateTimeOffset Time(string? value, int day, DateOnly firstDay, string? sessionId, TimeZoneInfo pacific)
    {
        if (!TimeOnly.TryParseExact(value, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new InvalidDataException("Invalid Microsoft session time.");
        DateOnly date;
        try
        {
            date = firstDay.AddDays(checked(day - 1));
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException(
                $"Microsoft session '{sessionId}' has a day outside the representable calendar.", exception);
        }
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (pacific.IsInvalidTime(local) || pacific.IsAmbiguousTime(local))
            throw new InvalidDataException(
                $"Microsoft session '{sessionId}' has an invalid or ambiguous Pacific time.");
        try { return new DateTimeOffset(local, pacific.GetUtcOffset(local)); }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException(
                $"Microsoft session '{sessionId}' resolves outside the representable UTC range.", exception);
        }
    }
    private static bool IsUseful(string? value) => !string.IsNullOrWhiteSpace(value) &&
        !new[] { "TODO", "TBD", "Chet is" }.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase) &&
        !value.Contains("No response", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains("Get copilot to write me one", StringComparison.OrdinalIgnoreCase);

    private static ConferenceDocument RetainCachedDetails(ConferenceDocument current, ConferenceDocument previous)
    {
        var prior = previous.Speakers.ToDictionary(speaker => speaker.Id, StringComparer.Ordinal);
        return current with
        {
            Speakers = current.Speakers.Select(speaker =>
            {
                prior.TryGetValue(speaker.Id, out var old);
                var photo = SpeakerPhotoUrl.Normalize(speaker.ProfilePicture);
                return speaker with
                {
                    Bio = IsUseful(speaker.Bio) ? speaker.Bio : old?.Bio ?? "",
                    ProfilePicture = photo.Length > 0 ? photo : SpeakerPhotoUrl.Normalize(old?.ProfilePicture)
                };
            }).ToList(),
            Sessions = current.Sessions.Select(session => session with
            {
                Description = IsUseful(session.Description) ? session.Description :
                    previous.Sessions.Concat(previous.WithdrawnSessions)
                        .FirstOrDefault(old => old.Id == session.Id)?.Description ?? ""
            }).ToList()
        };
    }
}

public sealed record MicrosoftAgenda
{
    public required List<MicrosoftSession> Sessions { get; init; }
    public required List<MicrosoftSpeaker> Speakers { get; init; }
}

public sealed record MicrosoftSession
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required int Day { get; init; }
    public required string StartTime { get; init; }
    public required string EndTime { get; init; }
    public required string Timezone { get; init; }
    public required List<string> SpeakerIds { get; init; }
    public required List<string> Topics { get; init; }
    public string? Abstract { get; init; }
}

public sealed record MicrosoftSpeaker
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Avatar { get; init; }
    public string? PrimaryBio { get; init; }
    public string? AdditionalBio { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MicrosoftAgenda))]
public partial class RemoteAgendaJsonContext : JsonSerializerContext;
