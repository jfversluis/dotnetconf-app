using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetConf.Core;

public static class MicrosoftAgendaJson
{
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    // These are the identities shipped in the original snapshot, not hashes of mutable display names.
    private static readonly Dictionary<string, string> SpeakerAliases = new(StringComparer.Ordinal)
    {
        ["speaker-alexandre-zollinger-chohfi"] = "d8517691-45a3-541c-8561-19cd33516b2a",
        ["speaker-andy-gocke"] = "5103459b-4a1c-5c05-9bfd-c6235f95f852",
        ["speaker-barry-dorrans"] = "8889dd11-13a9-53ca-a6fb-e1dea098e01b",
        ["speaker-bruno-capuano"] = "94360b77-3474-583d-b801-f43acafa2725",
        ["speaker-chet-husk"] = "25073f93-5b07-593e-aa13-bed4e851c866",
        ["speaker-damian-edwards"] = "39734651-893e-5672-b749-9d47d21fb224",
        ["speaker-daniel-roth"] = "15b37374-dd05-5089-9979-6e50ba60d7ad",
        ["speaker-dustin-campbell"] = "ab8935df-8c51-5d75-8058-a9a280d417f4",
        ["speaker-gerald-versluis"] = "7871d1fd-13e8-5c11-be64-fc52a0091913",
        ["speaker-harshada-hole"] = "7cffb9bd-ac02-5a8d-8f77-cb7d5a03b285",
        ["speaker-jason-chlus"] = "bc2d1688-316f-581d-ab3a-234256f184b7",
        ["speaker-jeremy-barton"] = "7be2ab4d-4642-5bb2-ad1e-cb2f9ca43cd8",
        ["speaker-jiri-cincura"] = "02700bec-df06-5bcd-8c3c-a9c6e492df4c",
        ["speaker-jonathan-peppers"] = "7630ca28-de3e-5976-b228-7da8ca048f05",
        ["speaker-jose-perez-rodriguez"] = "a118fad0-3ac6-504f-8b31-d55cfc0f38a9",
        ["speaker-klaus-loffelmann"] = "9830b785-e466-5b14-8108-abd86b40125e",
        ["speaker-luis-quintanilla"] = "664bcc26-6e57-5f48-8a9a-cd87bb757493",
        ["speaker-maddy-montaquila"] = "17ca67c0-2a3e-5a4c-90e1-e2ab2a9be74a",
        ["speaker-mads-kristensen"] = "0014dc28-8e17-5fea-bf57-266b77e175d8",
        ["speaker-mads-torgersen"] = "7e3d731b-e9fe-5e0e-a37a-646f9c1774cc",
        ["speaker-manodasan-wignarajah"] = "ac0ad33b-3e93-50d8-b287-249426194d3e",
        ["speaker-matt-soucoup"] = "1d438637-c125-56f9-b673-b9e35546651d",
        ["speaker-mika-dumont"] = "36adbaaf-0567-5514-af27-0d3278758b02",
        ["speaker-morten-nielsen"] = "3c3219cc-9c59-54a9-909d-c589cbed5ffe",
        ["speaker-niels-laute"] = "3d469f67-1e57-5368-b3cb-b01872ee34c0",
        ["speaker-paul-yuknewicz"] = "344cc785-2d59-5e03-ac0f-8e8ca845984b",
        ["speaker-rachel-kang"] = "ec2db9e8-88ef-5ef4-b1ea-c909b5d8c804",
        ["speaker-richard-lander"] = "73683d96-d255-5c41-ac8e-8870123e3446",
        ["speaker-scott-hanselman"] = "f02cb577-40a8-5307-b39c-223e1b08c7d5",
        ["speaker-sebastian-hernandez"] = "031327b2-2852-5a07-9298-1c9bdc6d60bc",
        ["speaker-sergio-pedri"] = "5c3461db-7904-5664-afa6-81dd89af2c67",
        ["speaker-stephen-halter"] = "afb89ca3-235b-5f5e-ba59-fb09fe16459d",
        ["speaker-tanner-gooding"] = "615a58ec-bcaa-5544-917f-1f840f2c9897",
        ["speaker-wendy-breiding"] = "6856fe48-5e3c-5eb1-831f-5a60a818283b"
    };

    public static async Task<ConferenceDocument> ReadAsync(
        Stream stream, ConferenceDocument previous, CancellationToken cancellationToken = default)
    {
        var data = await JsonSerializer.DeserializeAsync(
            stream, RemoteAgendaJsonContext.Default.MicrosoftAgenda, cancellationToken)
            ?? throw new InvalidDataException("The Microsoft agenda is null.");
        if (data.Sessions is null || data.Speakers is null || data.Sessions.Count == 0)
            throw new InvalidDataException("Microsoft sessions and speakers are required; an empty agenda is not an update.");

        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        var speakers = new List<Speaker>();
        foreach (var source in data.Speakers)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Id) || !sourceIds.Add(source.Id))
                throw new InvalidDataException("Microsoft speakers require unique IDs.");
            if (!RemoteProfiles.IsUseful(source.Name))
                continue;
            var id = SpeakerAliases.GetValueOrDefault(source.Id) ?? source.Id;
            speakers.Add(new Speaker
            {
                Id = id, Name = source.Name!,
                Bio = string.Join("\n\n", new[] { source.PrimaryBio, source.AdditionalBio }
                    .Where(RemoteProfiles.IsUseful)),
                ProfilePicture = RemoteProfiles.Photo(source.Avatar)
            });
        }
        var sessions = new List<Session>();
        foreach (var source in data.Sessions)
        {
            if (source is null || source.SpeakerIds is null || source.SpeakerIds.Any(string.IsNullOrWhiteSpace) ||
                source.Topics is null ||
                source.Day is < 1 or > 4 || source.Timezone != "PST")
                throw new InvalidDataException("Microsoft sessions require valid days, Pacific timezone, and references.");
            sessions.Add(new Session
            {
                Id = SessionId(source.Id), Title = source.Title ?? "",
                Description = RemoteProfiles.IsUseful(source.Abstract) ? source.Abstract! : "",
                StartsAt = Time(source.StartTime, source.Day), EndsAt = Time(source.EndTime, source.Day),
                // This sentinel represents an unanswered optional co-speaker, not a person.
                SpeakerIds = source.SpeakerIds.Where(id => id != "speaker-no-response")
                    .Select(id => SpeakerAliases.GetValueOrDefault(id) ?? id).ToList(),
                Topics = source.Topics
            });
        }
        var document = previous with { Sessions = sessions, WithdrawnSessions = [], Speakers = speakers };
        ConferenceJson.Validate(document);
        return RemoteProfiles.Retain(document, previous);
    }

    private static string SessionId(string? id)
    {
        if (id is null || id.Length != 6 ||
            !int.TryParse(id.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number == 0)
            throw new InvalidDataException("Invalid Microsoft session ID.");
        return id[..3] switch
        {
            "KEY" => (20260000 + number).ToString(CultureInfo.InvariantCulture),
            "BRK" => (20261000 + number).ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException("Unknown Microsoft session ID prefix.")
        };
    }

    private static DateTimeOffset Time(string? value, int day)
    {
        if (!TimeOnly.TryParseExact(value, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new InvalidDataException("Invalid Microsoft session time.");
        var local = new DateOnly(2026, 11, 9 + day).ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Pacific.GetUtcOffset(local));
    }
}

internal static class RemoteProfiles
{
    internal static bool IsUseful(string? value) => !string.IsNullOrWhiteSpace(value) &&
        !new[] { "TODO", "TBD", "Chet is" }.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase) &&
        !value.Contains("No response", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains("Get copilot to write me one", StringComparison.OrdinalIgnoreCase);

    internal static string Photo(string? value) => SpeakerPhotoUrl.Normalize(value);

    internal static ConferenceDocument Retain(ConferenceDocument current, ConferenceDocument previous)
    {
        var prior = previous.Speakers.ToDictionary(speaker => speaker.Id, StringComparer.Ordinal);
        return current with
        {
            Speakers = current.Speakers.Select(speaker =>
            {
                prior.TryGetValue(speaker.Id, out var old);
                var photo = Photo(speaker.ProfilePicture);
                return speaker with
                {
                    Bio = IsUseful(speaker.Bio) ? speaker.Bio : old?.Bio ?? "",
                    Tagline = IsUseful(speaker.Tagline) ? speaker.Tagline : old?.Tagline ?? "",
                    ProfilePicture = photo.Length > 0 ? photo : Photo(old?.ProfilePicture)
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
[JsonSerializable(typeof(ConferenceCache))]
public partial class RemoteAgendaJsonContext : JsonSerializerContext;
