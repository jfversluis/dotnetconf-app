using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using System.Text.RegularExpressions;
using DotNetConf.Core;

namespace DotNetConf.App.Services;

public sealed record PlannerInterpretation(string SpeakerNames, string AvailabilityText, string SearchQuery, string Quantity)
{
    public bool HasAvailability => !string.IsNullOrWhiteSpace(AvailabilityText)
        && !AvailabilityText.Equals("none", StringComparison.OrdinalIgnoreCase);

    public bool RequestsAllSpeakers(string request) => Quantity == "all" && HasAllQuantifier(request);

    public static bool HasAllQuantifier(string request) => Regex.IsMatch(request,
        @"\b(all|every|everything)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public (string[] Speakers, string Query) Ground(ConferenceDocument conference, string request)
    {
        var names = SpeakerNames.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? [] : SpeakerNames.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var inferred = SearchQuery.Equals("none", StringComparison.OrdinalIgnoreCase) ? "" : SearchQuery;
        return Agenda.GroundPlannerQuery(conference, request, names, inferred);
    }
}

public sealed record PlannerTopicRequest(string Text, string Quantity)
{
    public bool RequestsAll(string request) => Quantity == "all" && PlannerInterpretation.HasAllQuantifier(request);

    public bool HasInterest(ConferenceDocument conference, string request)
    {
        if (string.IsNullOrWhiteSpace(Text) || Text.Equals("none", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!request.Contains(Text, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The model's topic phrase was not present in the request.");
        // The model can copy a speaker request as its topic evidence. Names and
        // generic conference-request words alone cannot justify inferred topics.
        var names = conference.Speakers.SelectMany(s => s.Name.Split(' ')).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Regex.Escape);
        var remaining = Regex.Replace(Text, $@"\b(?:{string.Join('|', names)})\b", "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        remaining = Regex.Replace(remaining,
            @"\b(?:all|every|everything|sessions?|talks?|show|me|by|from|with|and|or|of|the|please|s)\b", "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return remaining.Any(char.IsLetterOrDigit);
    }
}

public sealed record PlannerSelection(string[] SessionIds);
public sealed record PlannerAvailability(PlannerWindow[] Windows)
{
    public AvailabilityWindow[] ToWindows(TimeZoneInfo zone)
    {
        if (Windows is not { Length: > 0 and <= 12 })
            throw new InvalidDataException("The model must return between 1 and 12 explicit availability windows.");
        return Windows.Select(w => w?.ToWindow(zone)
            ?? throw new InvalidDataException("The model returned an empty window.")).ToArray();
    }
}
public sealed record PlannerWindow(string Start, string End, string Zone)
{
    public AvailabilityWindow ToWindow(TimeZoneInfo? local = null)
    {
        if (Start is null || End is null || string.IsNullOrWhiteSpace(Zone))
            throw new InvalidDataException("The model returned an incomplete time window.");
        return new(Normalize(Start, local ?? TimeZoneInfo.Local), Normalize(End, local ?? TimeZoneInfo.Local), Zone);
    }

    private string Normalize(string value, TimeZoneInfo local)
    {
        if (Zone == "UTC" && value.EndsWith('Z'))
            value = value[..^1];
        if (Zone == "UTC" && value.EndsWith("+00:00", StringComparison.Ordinal))
            value = value[..^6];
        if (!DateTime.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
        {
            if (!DateTimeOffset.TryParseExact(value, ["yyyy-MM-dd'T'HH:mmzzz", "yyyy-MM-dd'T'HH:mm:sszzz"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
                throw new InvalidDataException("The model returned an invalid wall-clock time.");
            var zone = local;
            if (Zone != "local" && !TimeZoneInfo.TryFindSystemTimeZoneById(Zone, out zone))
                throw new InvalidDataException("The model returned an unknown time zone.");
            dateTime = withOffset.DateTime;
            if (withOffset.Offset != zone.GetUtcOffset(dateTime))
                throw new InvalidDataException("The model returned an offset inconsistent with its time zone.");
        }
        if (dateTime.Second != 0)
            throw new InvalidDataException("The model returned unsupported sub-minute precision.");
        return dateTime.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PlannerInterpretation))]
[JsonSerializable(typeof(PlannerTopicRequest))]
[JsonSerializable(typeof(PlannerSelection))]
[JsonSerializable(typeof(PlannerAvailability))]
internal partial class PlannerJsonContext : JsonSerializerContext;

internal static class PlannerSchemas
{
    private static JsonElement InterpretationTemplate { get; } = Parse(
        """
        {"title":"ConferenceRequest","type":"object","properties":{"speakerNames":{"type":"string","description":"Names of PEOPLE explicitly mentioned, copied verbatim and separated by semicolons. Use none if the request mentions no person. Topics and interests are NOT people."},"availabilityText":{"type":"string","description":"Copy the date/time restriction from the user's request. Use none if the request says nothing about dates or times. Never invent a restriction."},"quantity":{"type":"string","enum":["all","some"],"description":"Use all only if the user asks for all, every or everything by/from a person; otherwise some."},"searchQuery":{"type":"string","description":"Classify the attendee's goal, not generic words such as code or apps. Runtime speed, low latency and memory efficiency map to Performance; browser UI and sites map to Web; native phone/tablet development maps to Mobile. Prefer a topic literally mentioned in the request. Use none for speaker-only requests or no interests."}},"required":["speakerNames","availabilityText","quantity","searchQuery"],"additionalProperties":false}
        """);

    public static JsonElement Topic { get; } = Parse(
        """
        {"title":"ConferenceTopicRequest","type":"object","properties":{"text":{"type":"string","description":"Copy the exact words stating the user's TOPICAL interests, excluding speaker names and time restrictions. Use none for speaker-only requests. Never infer a topic from a person's expertise."},"quantity":{"type":"string","enum":["all","some"],"description":"Use all only when ALL or EVERY qualifies the requested TOPIC sessions. Otherwise some. Everything from a speaker does not mean all topic sessions."}},"required":["text","quantity"],"additionalProperties":false}
        """);

    private static JsonElement SelectionTemplate { get; } = Parse(
        """
        {"title":"ConferenceSelection","type":"object","properties":{"sessionIds":{"type":"array","minItems":0,"maxItems":12,"items":{"type":"string"},"description":"Select real IDs from the agenda function result, satisfying the user's request without overlapping sessions. Empty only if impossible."}},"required":["sessionIds"],"additionalProperties":false}
        """);

    public static JsonElement Availability { get; } = Parse(
        """
        {"title":"ConferenceAvailability","type":"object","properties":{"windows":{"type":"array","minItems":1,"maxItems":12,"items":{"title":"ConferenceTimeWindow","type":"object","properties":{"start":{"type":"string","description":"Requested start, yyyy-MM-ddTHH:mm, with no conversion or offset."},"end":{"type":"string","description":"Requested end, yyyy-MM-ddTHH:mm, with no conversion or offset."},"zone":{"type":"string","description":"Copy UTC if user says UTC. Use local if user says local or names no zone. Otherwise copy the explicit IANA zone ID. Never convert the clock times."}},"required":["start","end","zone"],"additionalProperties":false}}},"required":["windows"],"additionalProperties":false}
        """);

    public static JsonElement Interpretation(ConferenceDocument conference) =>
        WithChoices(InterpretationTemplate, "searchQuery",
            conference.Sessions.SelectMany(s => s.Topics).Prepend("none").Distinct());

    public static JsonElement Selection(IEnumerable<string> candidateIds) =>
        WithChoices(SelectionTemplate, "sessionIds", candidateIds);

    private static JsonElement WithChoices(JsonElement template, string propertyName, IEnumerable<string> choices)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var root in template.EnumerateObject())
            {
                if (root.Name != "properties")
                {
                    root.WriteTo(writer);
                    continue;
                }
                writer.WriteStartObject("properties");
                foreach (var property in root.Value.EnumerateObject())
                {
                    writer.WriteStartObject(property.Name);
                    foreach (var field in property.Value.EnumerateObject())
                        if (property.Name != propertyName || field.Name != "items")
                            field.WriteTo(writer);
                    if (property.Name == propertyName)
                    {
                        var array = property.Value.GetProperty("type").GetString() == "array";
                        if (array)
                        {
                            writer.WriteStartObject("items");
                            writer.WriteString("type", "string");
                        }
                        writer.WriteStartArray("enum");
                        foreach (var choice in choices)
                            writer.WriteStringValue(choice);
                        writer.WriteEndArray();
                        if (array)
                            writer.WriteEndObject();
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
