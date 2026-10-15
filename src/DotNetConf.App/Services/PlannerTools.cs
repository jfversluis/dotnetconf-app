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
    private readonly HashSet<string> candidateIds = new(StringComparer.Ordinal);

    public ScheduleProposal? Proposal { get; private set; }
    public ScheduleProposal? Preferences { get; private set; }
    public int CallCount => calls;
    public IReadOnlySet<string> CandidateIds => candidateIds;

    public void Initialize(ConferenceDocument conference, TimeZoneInfo zone, string[] likedIds)
    {
        document = conference;
        timeZone = zone;
        savedIds = likedIds;
        calls = 0;
        Proposal = null;
        Preferences = null;
        candidateIds.Clear();
    }

    private ConferenceDocument GetDocument() =>
        document ?? throw new InvalidOperationException("Planner tools have not been initialized.");

    public string SearchSessions(string query, int skip)
    {
        var conference = GetDocument();
        return FormatPage(Agenda.Search(conference, query, null, null, timeZone).ToArray(), skip);
    }

    public string SpeakerSessions(string name, int skip = 0)
    {
        var conference = GetDocument();
        if (string.IsNullOrWhiteSpace(name))
            return "Supply a speaker name.";
        var speakers = Agenda.FindSpeakers(conference, name);
        if (speakers.Count == 0)
            return $"Speaker not found in this demo agenda: {name}. Do not invent participation.";
        if (speakers.Count > 1)
            return $"Ambiguous speaker '{name}'. Ask the user to choose: {string.Join(", ", speakers.Select(s => s.Name))}.";
        return $"Speaker: {speakers[0].Name}\n" + FormatPage(
            conference.Sessions.Where(s => s.SpeakerIds.Contains(speakers[0].Id)).OrderBy(s => s.StartsAt).ToArray(), skip);
    }

    public string CapturePreferences(string[] requiredSpeakers, AvailabilityWindow[] availability,
        bool allSessionsByRequiredSpeakers = false)
    {
        GetDocument();
        Proposal = null;
        Preferences = null;
        candidateIds.Clear();
        if (requiredSpeakers is null || availability is null || requiredSpeakers.Any(n => n is null)
            || availability.Any(w => w is null || w.Start is null || w.End is null || w.Zone is null))
            return "Invalid arguments: arrays and their items must not be null.";
        if (requiredSpeakers.Length > 8 || availability.Length > 12)
            return "Preferences exceed limits: 8 speakers and 12 windows.";
        if (requiredSpeakers.Any(n => n.Length > 100)
            || availability.Any(w => w.Start.Length > 20 || w.End.Length > 20 || w.Zone.Length > 100))
            return "Preference arguments are too long. Use speaker names and the specified time format.";
        var names = requiredSpeakers.Select(name =>
        {
            var matches = Agenda.FindSpeakers(GetDocument(), name);
            return matches.Count == 1 ? matches[0].Name : name;
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Preferences = new([], names, availability) { AllSessionsByRequiredSpeakers = allSessionsByRequiredSpeakers };
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
        var candidate = preferences with { SessionIds = [session.Id], RequiredSpeakers = [], AllSessionsByRequiredSpeakers = false };
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

    private string FormatPage(Session[] matching, int skip)
    {
        var eligible = matching.Where(IsEligible).ToArray();
        var offset = Math.Clamp(skip, 0, matching.Length);
        var page = eligible.Skip(offset).Take(4).ToArray();
        foreach (var session in page)
            candidateIds.Add(session.Id);
        var excluded = matching.Length - eligible.Length;
        return $"Matches: {matching.Length}; eligible: {eligible.Length}; excluded by availability or liked-session conflicts: {excluded}. " +
            (offset + page.Length < eligible.Length ? $"Next page: skip={offset + page.Length}.\n" : "End of results.\n") +
            Format(page);
    }

    public IReadOnlyList<AITool> CreateFunctions() =>
    [
        new PlannerFunction(this, "search_sessions",
            "Search agenda titles, topics, descriptions and speakers. Use a short topic keyword (Mobile, AI, Blazor), NOT a sentence. Words are ANDed. Empty query browses all. At most 4 rows per page; follow next-page skip.",
            """
            {"type":"object","properties":{"query":{"type":"string","description":"Short search term, or empty to browse"},"skip":{"type":"integer","description":"Results to skip, starting at zero"}},"required":["query","skip"],"additionalProperties":false}
            """),
        new PlannerFunction(this, "speaker_sessions",
            "Find sessions by full name or a UNIQUE first/last name. Ask for clarification if ambiguous. At most 4 rows per page; follow next-page skip to include ALL sessions.",
            """
            {"type":"object","properties":{"name":{"type":"string","description":"Full or partial speaker name"},"skip":{"type":"integer","description":"Results to skip, zero for the first page"}},"required":["name","skip"],"additionalProperties":false}
            """),
        new PlannerFunction(this, "submit_proposal",
            "Submit a PREVIEW, never save likes. Copy real session IDs exactly. C# enforces the previously captured preferences. Fix reported validation errors and resubmit without relaxing constraints.",
            """
            {"type":"object","properties":{"sessionIds":{"type":"array","items":{"type":"string"},"description":"1 to 12 real session IDs copied exactly from search results"}},"required":["sessionIds"],"additionalProperties":false}
            """)
    ];

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
            if (name == "submit_proposal")
                tools.Proposal = null;
            try
            {
                object result = name switch
                {
                    "search_sessions" => tools.SearchSessions(ReadString(arguments, "query"), ReadInteger(arguments, "skip")),
                    "speaker_sessions" => tools.SpeakerSessions(ReadString(arguments, "name"), ReadInteger(arguments, "skip")),
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

    }
}
