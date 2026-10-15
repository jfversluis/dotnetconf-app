using System.Text.Json;
using DotNetConf.Core;
using Microsoft.Extensions.AI;

namespace DotNetConf.App.Services;

public sealed class PlannerTools
{
    private ConferenceDocument? document;
    private TimeZoneInfo timeZone = TimeZoneInfo.Utc;
    private string[] savedIds = [];
    private int calls;

    public ScheduleProposal? Proposal { get; private set; }
    public ScheduleProposal? Preferences { get; private set; }
    public int CallCount => calls;

    public void Initialize(ConferenceDocument conference, TimeZoneInfo zone, string[] likedIds)
    {
        document = conference;
        timeZone = zone;
        savedIds = likedIds;
        calls = 0;
        Proposal = null;
        Preferences = null;
    }

    private ConferenceDocument GetDocument() =>
        document ?? throw new InvalidOperationException("Planner tools have not been initialized.");

    public string SearchSessions(string query, int skip)
    {
        var conference = GetDocument();
        return Format(Agenda.Search(conference, query, null, null, timeZone)
            .Where(IsEligible).Skip(Math.Clamp(skip, 0, 100)).Take(4));
    }

    public string SpeakerSessions(string name)
    {
        var conference = GetDocument();
        if (string.IsNullOrWhiteSpace(name))
            return "Supply a speaker name.";
        var speakers = conference.Speakers.Where(s =>
            s.Name.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase)).Take(4).ToArray();
        if (speakers.Length == 0)
            return "Speaker not found. Report the unsatisfied request; do not drop it.";
        var ids = speakers.Select(s => s.Id).ToHashSet();
        return Format(conference.Sessions.Where(s => s.SpeakerIds.Any(ids.Contains))
            .Where(IsEligible).OrderBy(s => s.StartsAt).Take(4));
    }

    public string CapturePreferences(string[] requiredSpeakers, AvailabilityWindow[] availability)
    {
        GetDocument();
        Proposal = null;
        Preferences = null;
        if (requiredSpeakers is null || availability is null || requiredSpeakers.Any(n => n is null)
            || availability.Any(w => w is null || w.Start is null || w.End is null || w.Zone is null))
            return "Invalid arguments: arrays and their items must not be null.";
        if (requiredSpeakers.Length > 8 || availability.Length > 12)
            return "Preferences exceed limits: 8 speakers and 12 windows.";
        if (requiredSpeakers.Any(n => n.Length > 100)
            || availability.Any(w => w.Start.Length > 20 || w.End.Length > 20 || w.Zone.Length > 100))
            return "Preference arguments are too long. Use speaker names and the specified time format.";
        Preferences = new([], requiredSpeakers, availability);
        return "Preferences captured for user review. They cannot be changed during agenda selection.";
    }

    public string SubmitProposal(string[] sessionIds)
    {
        var conference = GetDocument();
        Proposal = null;
        if (sessionIds is null || sessionIds.Any(id => id is null || id.Length > 100)
            || sessionIds.Length > SchedulePlanner.MaximumSessions)
            return "Invalid arguments: provide at most 12 real session IDs.";
        var preferences = Preferences ?? throw new InvalidOperationException("Capture preferences before proposing sessions.");
        Proposal = preferences with { SessionIds = sessionIds };
        var review = SchedulePlanner.Review(conference, Proposal, timeZone, savedIds);
        return review.CanApply ? "Preview ready. Ask the user to review interpreted constraints and confirm in the app."
            : string.Join("\n", review.Errors.Take(8));
    }

    private bool IsEligible(Session session)
    {
        var preferences = Preferences ?? throw new InvalidOperationException("Capture preferences before searching.");
        var candidate = preferences with { SessionIds = [session.Id], RequiredSpeakers = [] };
        return SchedulePlanner.Review(document ?? throw new InvalidOperationException("No agenda loaded."),
            candidate, timeZone, savedIds).CanApply;
    }

    private string Format(IEnumerable<Session> sessions)
    {
        var conference = document ?? throw new InvalidOperationException("Planner tools have not been initialized.");
        var rows = sessions.Select(s =>
            $"{s.Id} | {s.Title} | {TimeZoneInfo.ConvertTime(s.StartsAt, timeZone):yyyy-MM-ddTHH:mm} to " +
            $"{TimeZoneInfo.ConvertTime(s.EndsAt, timeZone):yyyy-MM-ddTHH:mm} | " +
            $"{string.Join(", ", conference.Speakers.Where(p => s.SpeakerIds.Contains(p.Id)).Select(p => p.Name))} | " +
            $"{string.Join(", ", s.Topics)}").ToArray();
        return rows.Length == 0 ? "No matching sessions." : string.Join("\n", rows);
    }

    public IReadOnlyList<AITool> CreateFunctions() =>
    [
        new PlannerFunction(this, "search_sessions",
            "Search real agenda titles, topics and descriptions. Returns at most 4 sessions. Empty query browses all. Use skip for the next page.",
            """
            {"type":"object","properties":{"query":{"type":"string","description":"Short search term, or empty to browse"},"skip":{"type":"integer","description":"Results to skip, starting at zero"}},"required":["query","skip"],"additionalProperties":false}
            """),
        new PlannerFunction(this, "speaker_sessions",
            "Find real sessions by speaker name. Returns at most 4 sessions or a not-found message. Never invent speakers.",
            """
            {"type":"object","properties":{"name":{"type":"string","description":"Speaker name as provided by the user"}},"required":["name"],"additionalProperties":false}
            """),
        new PlannerFunction(this, "submit_proposal",
            "Submit a PREVIEW, never save likes. Copy real session IDs exactly. C# enforces the previously captured preferences. Fix reported validation errors and resubmit without relaxing constraints.",
            """
            {"type":"object","properties":{"sessionIds":{"type":"array","items":{"type":"string"},"description":"1 to 12 real session IDs copied exactly from search results"}},"required":["sessionIds"],"additionalProperties":false}
            """)
    ];

    public AITool CreatePreferenceFunction() => new PlannerFunction(this, "capture_preferences",
        "Capture ALL required speakers and allowed time windows from the USER'S words only. Never substitute session times. Empty time arrays ONLY if unrestricted. Do not convert time zones.",
        """
        {"type":"object","properties":{"requiredSpeakers":{"type":"array","items":{"type":"string"},"description":"ALL must-see speaker names verbatim, even unknown names; empty only if none required"},"availabilityStarts":{"type":"array","items":{"type":"string"},"description":"Start of EACH user availability window as yyyy-MM-ddTHH:mm without offset"},"availabilityEnds":{"type":"array","items":{"type":"string"},"description":"End of EACH user availability window as yyyy-MM-ddTHH:mm without offset"},"availabilityZones":{"type":"array","items":{"type":"string"},"description":"Zone of EACH window: UTC if user said UTC, local if device time, otherwise IANA zone ID. Same count as starts and ends."}},"required":["requiredSpeakers","availabilityStarts","availabilityEnds","availabilityZones"],"additionalProperties":false}
        """);

    // Explicit schemas and argument readers avoid reflection-only JSON paths in
    // the current Attributes generator on NativeAOT.
    private sealed class PlannerFunction(PlannerTools tools, string name, string description, string schema) : AIFunction
    {
        private readonly JsonElement jsonSchema = ParseSchema(schema);
        private static readonly JsonElement StringSchema = ParseSchema("""{"type":"string"}""");
        public override string Name => name;
        public override string Description => description;
        public override JsonElement JsonSchema => jsonSchema;
        public override JsonElement? ReturnJsonSchema => StringSchema;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref tools.calls) > 10)
                throw new InvalidOperationException("Planner tool limit reached. Try a simpler request.");
            if (name == "capture_preferences")
            {
                tools.Preferences = null;
                tools.Proposal = null;
            }
            else if (name == "submit_proposal")
                tools.Proposal = null;
            try
            {
                object result = name switch
                {
                    "search_sessions" => tools.SearchSessions(ReadString(arguments, "query"), ReadInteger(arguments, "skip")),
                    "speaker_sessions" => tools.SpeakerSessions(ReadString(arguments, "name")),
                    "capture_preferences" => tools.CapturePreferences(
                        ReadArray(arguments, "requiredSpeakers", 8), ReadWindows(arguments)),
                    "submit_proposal" => tools.SubmitProposal(ReadArray(arguments, "sessionIds", 12)),
                    _ => throw new InvalidOperationException($"Unknown planner function: {name}.")
                };
                return ValueTask.FromResult<object?>(result);
            }
            catch (InvalidDataException exception)
            {
                return ValueTask.FromResult<object?>($"Invalid tool arguments: {exception.Message} Correct and retry. Nothing has been saved.");
            }
        }

        private static JsonElement ParseSchema(string value)
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }

        private static string ReadString(AIFunctionArguments arguments, string key) =>
            arguments.TryGetValue(key, out var value) ? value switch
            {
                string text when text.Length <= 100 => text,
                JsonElement { ValueKind: JsonValueKind.String } element when element.GetString() is { Length: <= 100 } text => text,
                _ => throw new InvalidDataException($"Tool argument {key} must be a string of at most 100 characters.")
            } : throw new InvalidDataException($"Missing tool argument: {key}.");

        private static int ReadInteger(AIFunctionArguments arguments, string key) =>
            arguments.TryGetValue(key, out var value) ? value switch
            {
                int number => number,
                JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var number) => number,
                _ => throw new InvalidDataException($"Tool argument {key} must be an integer.")
            } : throw new InvalidDataException($"Missing tool argument: {key}.");

        private static string[] ReadArray(AIFunctionArguments arguments, string key, int maximum)
        {
            if (!arguments.TryGetValue(key, out var value))
                throw new InvalidDataException($"Missing tool argument: {key}.");
            if (value is string[] array && array.Length <= maximum && array.All(s => s is { Length: <= 100 }))
                return array;
            if (value is JsonElement { ValueKind: JsonValueKind.Array } element && element.GetArrayLength() <= maximum)
                return element.EnumerateArray().Select(item =>
                    item.ValueKind == JsonValueKind.String && item.GetString() is { Length: <= 100 } text
                        ? text : throw new InvalidDataException($"Invalid string in tool argument: {key}.")).ToArray();
            throw new InvalidDataException($"Tool argument {key} must be an array of at most {maximum} short strings.");
        }

        private static AvailabilityWindow[] ReadWindows(AIFunctionArguments arguments)
        {
            var starts = ReadArray(arguments, "availabilityStarts", 12);
            var ends = ReadArray(arguments, "availabilityEnds", 12);
            var zones = ReadArray(arguments, "availabilityZones", 12);
            if (starts.Length != ends.Length || starts.Length != zones.Length)
                throw new InvalidDataException("Availability start, end and zone arrays must have the same length.");
            return starts.Select((start, index) => new AvailabilityWindow(start, ends[index], zones[index])).ToArray();
        }
    }
}
