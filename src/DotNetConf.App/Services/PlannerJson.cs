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

    public bool RequiresTimeExtraction(string request) => HasAvailability || Regex.IsMatch(request,
        @"\b\d{1,2}(?::\d{2})?\s*(?:am|pm)\b|\b\d{1,2}:\d{2}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static bool HasExplicitDate(string request) => Regex.IsMatch(request,
        @"\b\d{4}-\d{2}-\d{2}\b|\b(?:January|February|March|April|May|June|July|August|September|October|November|December|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday|today|tomorrow)\b|\b\d{1,2}/\d{1,2}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string RequestedZone(string request)
    {
        var zones = Regex.Matches(request, @"\bUTC\b|\b[A-Za-z_]+/[A-Za-z_]+(?:/[A-Za-z_]+)?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
            .Select(m => m.Value.Equals("UTC", StringComparison.OrdinalIgnoreCase) ? "UTC" : m.Value)
            .Distinct(StringComparer.Ordinal).ToArray();
        return zones.Length switch
        {
            0 => "local", 1 => zones[0],
            _ => throw new InvalidDataException("Use one explicit time zone per request.")
        };
    }

    public bool RequestsAllSpeakers(string request) => Quantity == "all" && (HasAllQuantifier(request)
        || Regex.IsMatch(request, @"\b(don['’]?t|do not)\s+miss\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));

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

public sealed record PlannerTopicRequest(string Text, string[] Topics, string Quantity, string Match, string SpeakerScope)
{
    public bool RequestsAll(string request) => Quantity == "all" && PlannerInterpretation.HasAllQuantifier(request);

    public PlannerTopicFilter? ToFilter(ConferenceDocument conference, string request, bool hasSpeakers = false)
    {
        if (Text is null || Topics is null || Topics.Length > 4 || Topics.Any(t => t is null)
            || Quantity is not ("all" or "some") || Match is not ("any" or "all")
            || SpeakerScope is not ("independent" or "within-topics" or "unsupported"))
            throw new InvalidDataException("The model returned invalid topic fields.");
        if (hasSpeakers && SpeakerScope == "unsupported")
            throw new InvalidDataException("Separate different speaker/topic pairs into individual requests; this spike cannot safely combine them.");
        var literal = Agenda.MentionedTopics(conference, request);
        if (literal.Length == 0)
        {
            if (hasSpeakers && !HasInterest(conference, request))
                return null;
            if (!hasSpeakers && Topics.Length == 0 && (string.IsNullOrWhiteSpace(Text) || Text == "none"))
                return null;
        }
        var known = conference.Sessions.SelectMany(s => s.Topics).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var topics = Topics.Concat(literal).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (topics.Length is 0 or > 4 || topics.Any(t => !known.Contains(t)))
            throw new InvalidDataException("No supported agenda topics matched, or more than four were requested. Use up to four real agenda topics.");
        var topicPattern = string.Join('|', topics.Select(t => Regex.Escape(t.StartsWith(".NET ", StringComparison.Ordinal) ? t[5..] : t)));
        var scopedSpeakers = conference.Speakers.Where(s =>
        {
            var aliases = s.Name.Split(' ').Append(s.Name).Select(Regex.Escape);
            return Regex.IsMatch(request, $@"\b(?:{string.Join('|', aliases)})['’]s\s+(?:\.NET\s+)?(?:{topicPattern})(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }).Count();
        if (scopedSpeakers > 1 && topics.Length > 1)
            throw new InvalidDataException("Separate different speaker/topic pairs into individual requests; this spike cannot safely combine them.");
        var explicitOr = Regex.IsMatch(request, @"\bor\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
        var independentClause = Regex.IsMatch(request, @"\b(?:and|plus)\s+(?:everything|all|every|don['’]?t|do not|I must)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        // A list of interests is a union. Require source evidence that each talk
        // must cover the combination, rather than trusting the model's AND guess.
        var explicitAnd = Regex.IsMatch(request,
            @"\bboth\b|\b(?:all|each|every)\s+(?:(?:of\s+)?(?:these|the|requested)\s+)?topics\b|\b(?:same\s+(?:session|talk)|together|combin(?:e|es|ing|ation))\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return new(topics, explicitAnd && !explicitOr,
            hasSpeakers && (scopedSpeakers > 0 || (SpeakerScope == "within-topics" && !independentClause)), RequestsAll(request));
    }

    public bool HasInterest(ConferenceDocument conference, string request)
    {
        if (string.IsNullOrWhiteSpace(Text) || Text.Equals("none", StringComparison.OrdinalIgnoreCase))
            return false;
        // The model can copy a speaker request as its topic evidence. Names and
        // generic conference-request words alone cannot justify inferred topics.
        var names = conference.Speakers.SelectMany(s => s.Name.Split(' ')).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Regex.Escape);
        string Remaining(string value)
        {
            var remaining = Regex.Replace(value, @"\b\d{1,2}(?::\d{2})?\s*(?:am|pm)\b", "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            remaining = Regex.Replace(remaining, $@"\b(?:{string.Join('|', names)})\b", "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            return Regex.Replace(remaining,
                @"\b(?:all|every|everything|each|day|daily|sessions?|talks?|show|give|find|me|by|from|with|and|or|of|the|please|s|a|an|I|want|would|like|must|can|see|watch|only|just|available|availability|on|at|in|before|after|between|until|to|time|local|UTC|am|pm|don|t|do|not|miss|any|but|exclude|excluding|ending|ends?|finish(?:ing|es)?|they|are|for|no|matter|regardless|about|surprise|January|February|March|April|May|June|July|August|September|October|November|December|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\b", "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        if (!Remaining(request).Any(char.IsLetter))
            return false;
        return Remaining(Text).Any(char.IsLetter);
    }
}

public sealed record PlannerTopicIntent(string Quantity, string SpeakerScope);
public sealed record PlannerTopicClassification(string Topic);
public sealed record PlannerDailyAvailability(string DailyStart, string DailyEnd, string Zone)
{
    public PlannerDailyAvailability Ground(string request)
    {
        const string clock = @"(?<hour>\d{1,2})(?::(?<minute>\d{2}))?\s*(?<period>am|pm)?";
        var clocks = Regex.Matches(request, $@"\b{clock}(?=\s|[.,;!?]|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
            .Where(m => m.Groups["period"].Success || m.Groups["minute"].Success).ToArray();
        if (clocks.Length != 1)
            return this;
        var match = Regex.Match(request, $@"\b(?<operator>before|until|by|after|from)\s+{clock}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success)
            return this;
        var hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
        var minute = match.Groups["minute"].Success ? int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture) : 0;
        var period = match.Groups["period"].Value;
        if (minute > 59 || hour > (period.Length > 0 ? 12 : 23) || (period.Length > 0 && hour == 0))
            throw new InvalidDataException("The requested clock time is invalid.");
        if (period.Length > 0)
            hour = hour % 12 + (period.Equals("pm", StringComparison.OrdinalIgnoreCase) ? 12 : 0);
        var time = new TimeOnly(hour, minute).ToString("HH:mm", CultureInfo.InvariantCulture);
        var op = match.Groups["operator"].Value.ToLowerInvariant();
        var excludeAfter = op == "after" && Regex.IsMatch(request, @"\b(?:exclud(?:e|ing)|avoid|nothing|no\s+(?:sessions?|talks?)|not\s+after)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return op is "before" or "until" or "by" || excludeAfter
            ? this with { DailyStart = "none", DailyEnd = time }
            : this with { DailyStart = time, DailyEnd = "none" };
    }

    public AvailabilityWindow[] ToWindows(TimeZoneInfo zone, ConferenceDocument conference) =>
        new PlannerAvailability([], DailyStart, DailyEnd, Zone).ToWindows(zone, conference);
}

public sealed record PlannerAvailability(PlannerWindow[] Windows, string DailyStart = "none",
    string DailyEnd = "none", string Zone = "local")
{
    public PlannerAvailability InZone(string zone, TimeZoneInfo local) => this with
    {
        Windows = Windows?.Select(w =>
        {
            var normalized = w?.ToWindow(local) ?? throw new InvalidDataException("The model returned an empty window.");
            // Validate redundant model offsets first, then restore the user's
            // explicit zone without converting their requested clock times.
            return new PlannerWindow(normalized.Start, normalized.End, zone);
        }).ToArray()
            ?? throw new InvalidDataException("The model returned no windows.")
    };

    public AvailabilityWindow[] ToWindows(TimeZoneInfo zone, ConferenceDocument? conference = null)
    {
        if (DailyStart is null || DailyEnd is null || Zone is null || Windows is null)
            throw new InvalidDataException("The model returned incomplete availability.");
        if (DailyStart != "none" || DailyEnd != "none")
        {
            if (Windows.Length != 0 || conference is null)
                throw new InvalidDataException("Do not combine dated windows and recurring clock limits in one request.");
            var start = Clock(DailyStart, TimeOnly.MinValue);
            var end = Clock(DailyEnd, TimeOnly.MinValue);
            return SchedulePlanner.DailyAvailability(conference, zone, start, end, Zone);
        }
        if (Windows is not { Length: > 0 and <= 12 })
            throw new InvalidDataException("The model must return between 1 and 12 explicit availability windows.");
        return Windows.Select(w => w?.ToWindow(zone)
            ?? throw new InvalidDataException("The model returned an empty window.")).ToArray();
    }

    private static TimeOnly Clock(string value, TimeOnly unspecified) => value == "none" ? unspecified
        : TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time : throw new InvalidDataException("Use HH:mm for daily clock limits.");
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
[JsonSerializable(typeof(PlannerTopicClassification))]
[JsonSerializable(typeof(PlannerTopicIntent))]
[JsonSerializable(typeof(PlannerAvailability))]
[JsonSerializable(typeof(PlannerDailyAvailability))]
internal partial class PlannerJsonContext : JsonSerializerContext;

internal static class PlannerSchemas
{
    public static JsonElement TopicIntent { get; } = Parse(
        """
        {"title":"TopicIntent","type":"object","properties":{"quantity":{"type":"string","enum":["all","some"]},"speakerScope":{"type":"string","enum":["independent","within-topics","unsupported"]}},"required":["quantity","speakerScope"],"additionalProperties":false}
        """);
    public static JsonElement ClassifyTopic(ConferenceDocument conference) => WithChoices(Parse(
        """
        {"title":"ClosestAgendaTopic","type":"object","properties":{"topic":{"type":"string","description":"The single closest agenda topic for this interest. Correct typos and understand abbreviations. Use none if no topic is relevant."}},"required":["topic"],"additionalProperties":false}
        """), "topic", conference.Sessions.SelectMany(s => s.Topics).Prepend("none").Distinct());

    private static JsonElement InterpretationTemplate { get; } = Parse(
        """
        {"title":"ConferenceRequest","type":"object","properties":{"speakerNames":{"type":"string","description":"Names of PEOPLE explicitly mentioned, copied verbatim and separated by semicolons. Use none if no person. Topics and interests are NOT people."},"availabilityText":{"type":"string","description":"Copy explicit date/time restrictions, including daily clock cutoffs. Use none if no restriction, or if the user says no matter the time. Latest/newest about a topic does NOT restrict dates."},"quantity":{"type":"string","enum":["all","some"],"description":"Use all for all/every/everything by a person or don't miss a session from a person; otherwise some."},"searchQuery":{"type":"string","description":"Closest topic for stated interests. Faster code maps to Performance; browser apps to Web; phone apps to Mobile. Use none for speaker-only requests or no interests."}},"required":["speakerNames","availabilityText","quantity","searchQuery"],"additionalProperties":false}
        """);

    public static JsonElement Availability { get; } = Parse(
        """
        {"title":"ConferenceAvailability","type":"object","properties":{"windows":{"type":"array","minItems":1,"maxItems":12,"items":{"title":"ConferenceTimeWindow","type":"object","properties":{"start":{"type":"string","description":"Requested start yyyy-MM-ddTHH:mm; never convert."},"end":{"type":"string","description":"Requested end yyyy-MM-ddTHH:mm; never convert."},"zone":{"type":"string","description":"UTC if explicit, local if unspecified; otherwise explicit IANA zone."}},"required":["start","end","zone"],"additionalProperties":false}}},"required":["windows"],"additionalProperties":false}
        """);

    public static JsonElement DailyAvailability { get; } = Parse(
        """
        {"title":"ConferenceDailyClockLimits","type":"object","properties":{"dailyStart":{"type":"string","description":"Allowed start HH:mm, or none if not stated. Do not invent a morning limit."},"dailyEnd":{"type":"string","description":"Allowed finish HH:mm, or none. Exclude after 10pm means 22:00. The ENTIRE session must finish by this time."},"zone":{"type":"string","description":"local for for me or unspecified; UTC if explicit; otherwise explicit IANA zone. Never convert clock times."}},"required":["dailyStart","dailyEnd","zone"],"additionalProperties":false}
        """);

    public static JsonElement Interpretation(ConferenceDocument conference) =>
        WithChoices(InterpretationTemplate, "searchQuery",
            conference.Sessions.SelectMany(s => s.Topics).Prepend("none").Distinct());

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
