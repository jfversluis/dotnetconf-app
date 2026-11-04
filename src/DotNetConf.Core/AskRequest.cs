using System.Globalization;
using System.Text.RegularExpressions;

namespace DotNetConf.Core;

public enum AskAction { FindSessions, OpenSession, OpenSpeaker, SaveSession, RemoveSessions }

public sealed record AskIntent(AskAction Action, string Query, string Day, string Speaker, bool AllMatches = false)
{
    public bool ExactTitle { get; init; }
}
public sealed record AskChoice(string Id, string Title, string Detail);
public sealed record AskResolution(AskIntent Intent, AskChoice[] Choices, string Message, bool ChooseDay = false);

/// <summary>Grounds an action against the actual agenda, without changing application state.</summary>
public static class AskRequest
{
    public const int MaximumLength = 500;
    private const RegexOptions MatchOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private const string CommandPrefixPattern =
        @"^\s*(?:(?:please\s*,?|can\s+you|could\s+you|would\s+you|will\s+you|i\s+(?:want|need)\s+(?:you\s+)?to|i(?:['’]d|\s+would)\s+like\s+(?:you\s+)?to)\s+)*";
    private const string BulkSavePattern =
        @"\b(?:save|add|bookmark|heart|favou?rite)\s+(?:me\s+)?(?:all(?:\s+of)?(?:\s+the)?|every|everything)\b(?<scope>.*)";
    private const string TitleSyntaxPattern =
        @"\b(?:(?<preposition>by)|(?:sessions?|talks?)\s+(?<preposition>with|for)|(?<day>Sunday|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday)|(?<day>\d{4}-\d{2}-\d{2}))\b";
    private const string SearchTermPattern = @"(?<![\p{L}\p{N}])\.?[\p{L}\p{N}]+(?:[./_-][\p{L}\p{N}]+)*[+#]*";
    private const string ScheduleReferencePattern =
        @"\b(?<preposition>to|into|in|on)\s+(?:my|the)\s+(?:(?:saved|conference)\s+)*(?:schedule|agenda|favou?rites|bookmarks?)\b";
    private const string ConferenceAgendaLocationPattern = @"(?:on|in)\s+the\s+conference\s+agenda";
    private const string PossessiveSuffixPattern = @"^(?:[""”’'](?=['’]))?['’]s\b";

    private static readonly HashSet<string> IgnoredScopeTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "this", "all", "every", "everything", "my", "saved",
        "talk", "talks", "session", "sessions", "conference", "agenda", "about", "on",
        "for", "of", "to", "in", "into", "me", "please", "thanks", "and", "or", "s"
    };
    private static readonly HashSet<string> SingleSaveIgnoredTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "this", "talk", "talks", "session", "sessions", "about", "on", "for", "of", "me", "please", "thanks"
    };

    public static void Validate(string request)
    {
        if (string.IsNullOrWhiteSpace(request) || request.Length > MaximumLength)
            throw new InvalidDataException($"Enter a request of 1 to {MaximumLength} characters.");
        if (Matches(request, @"(?:\band\b|\bthen\b|\balso\b|[;\r\n])\s+(?:(?:please|also)\s+)*(?:save|add|bookmark|heart|favou?rite|remove|delete|unsave|open|show|find|search|take)\b"))
            throw new InvalidDataException("Ask can do one thing at a time. Split this into separate requests.");
        if (Matches(request, @"\b(?:UTC|GMT|PST|PDT|EST|EDT|CET|CEST)\b|[A-Za-z]+/[A-Za-z_]+"))
            throw new InvalidDataException("Ask currently uses your device's local time. Use the local conference date instead.");
        if (Matches(request, @"\b(?:except|without|excluding|avoid|before|after|between|unless|never)\b|\bdon['’]?t\b|\bdo\s+not\b"))
            throw new InvalidDataException("Ask supports a search and a conference day, not exclusions or time windows. Use filters or the schedule planner for more detail.");
    }

    public static void ValidateIntent(string request, AskIntent intent) => ValidateIntent(null, request, intent, false);

    public static void ValidateIntent(ConferenceDocument document, string request, AskIntent intent) =>
        ValidateIntent(document, request, intent, false);

    public static void ValidateLookup(ConferenceDocument document, string request, AskIntent intent) =>
        ValidateIntent(document, request, intent, true);

    private static void ValidateIntent(ConferenceDocument? document, string request, AskIntent intent, bool readOnlyLookup)
    {
        Validate(request);
        ValidateSavedScheduleRestriction(document, request, intent);
        foreach (var value in new[] { intent.Query, intent.Day, intent.Speaker })
            if (value is null || value.Length > 100)
                throw new InvalidDataException("The model returned an invalid search.");
        var unfilteredLookup = readOnlyLookup && intent.Query.Length == 0 && intent.Speaker.Length == 0;
        foreach (var term in (intent.Query + " " + intent.Speaker).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!request.Contains(term, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The model added a search term you didn't request. Try a short title, topic or speaker name.");
        if (intent.Day.Length > 0 && !request.Contains(intent.Day, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Use the weekday or date exactly as written in your request.");
        var speakerClause = SpeakerClause(request, intent.ExactTitle ? intent.Query : null);
        if (!unfilteredLookup && speakerClause.Success)
        {
            var speaker = ExplicitSpeaker(request, intent.Day, intent.ExactTitle ? intent.Query : null,
                intent.Action == AskAction.RemoveSessions);
            if (intent.Speaker.Length == 0 || !intent.Speaker.Equals(speaker, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The model didn't preserve your speaker filter. Try 'by' followed by the speaker's full name.");
        }
        if (!unfilteredLookup && !speakerClause.Success && HasSplitSpeakerScope(request, intent))
            throw new InvalidDataException("Use 'by' followed by the speaker's full name so Ask can keep the right scope.");
        if (!unfilteredLookup)
            ValidateRequestedScope(document, request, intent, speakerClause);
        if (!readOnlyLookup && intent.Action == AskAction.SaveSession && !intent.AllMatches)
            ValidateSingleSaveScope(request, intent);
        if (!unfilteredLookup && intent.Query.Length == 0 && intent.Speaker.Length == 0)
        {
            if (intent.Action is AskAction.OpenSession or AskAction.OpenSpeaker ||
                intent.Action == AskAction.SaveSession && !intent.AllMatches)
                throw new InvalidDataException("Name a session, topic, or speaker so Ask can find the right match.");
            var withoutDay = RemoveDay(request, intent.Day);
            if (intent.Action == AskAction.FindSessions && !Matches(withoutDay,
                @"^\s*(?:(?:please|find|show|search|browse|open|me|the|all|every|everything|sessions|talks|conference|agenda|on|for)\b[\s?.!,]*)+$"))
                throw new InvalidDataException("The model didn't preserve your search. Try a short topic, title, or speaker name.");
        }
        if (intent.Action == AskAction.SaveSession && !Matches(request, @"\b(?:save|add|bookmark|heart|favou?rite)\b"))
            throw new InvalidDataException("To save a session, explicitly ask to save or add it.");
        if (intent.Action is AskAction.SaveSession or AskAction.RemoveSessions &&
            Matches(request, @"\b(?:not|never|unless|instead|if)\b|\bdon['’]?t\b"))
            throw new InvalidDataException("Ask can't safely apply a conditional or negated schedule change. Make one explicit request.");
        if (intent.Action == AskAction.RemoveSessions)
        {
            if (!Matches(request, @"\b(?:remove|unsave|delete|clear)\b"))
                throw new InvalidDataException("To remove saved sessions, explicitly ask to remove them.");
            if (!unfilteredLookup && intent.Query.Length == 0 && intent.Speaker.Length == 0 &&
                !HasAllRemovalScope(request, intent.Day))
                throw new InvalidDataException("Name the saved sessions to remove, or explicitly ask to remove everything on a day.");
        }
    }

    public static AskIntent GroundDay(string request, AskIntent intent) => GroundDay(null, request, intent);

    private static AskIntent GroundDay(ConferenceDocument? document, string request, AskIntent intent)
    {
        var parsedRequest = MaskExactTitle(request, intent.ExactTitle ? intent.Query : null);
        var dates = Regex.Matches(parsedRequest, @"\b\d{4}-\d{2}-\d{2}\b",
            RegexOptions.CultureInvariant, RegexTimeout).Select(m => m.Value).Distinct().ToArray();
        var clause = SpeakerClause(request, intent.ExactTitle ? intent.Query : null);
        var speakerStart = clause.Success ? clause.Groups["speaker"].Index : request.Length;
        const string weekdayPattern = @"\b(?:Sunday|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday)\b";
        var speakerNames = document is null ? [] : SpeakerMentions(document, request);
        var weekdayMatches = MatchAll(parsedRequest, weekdayPattern).ToArray();
        bool InSpeakerName(Match match) => speakerNames.Any(name =>
            match.Index >= name.Start && match.Index + match.Length <= name.End);
        if (document is not null && (intent.Action is AskAction.SaveSession or AskAction.RemoveSessions) && !clause.Success &&
            weekdayMatches.Any(match => !InSpeakerName(match) &&
                !Matches(parsedRequest[..match.Index], @"\b(?:on|for)\s+$") &&
                document.Speakers.Concat(document.WithdrawnSpeakers).Any(speaker =>
                    speaker.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Any(part => part.Equals(match.Value, StringComparison.OrdinalIgnoreCase)))))
            throw new InvalidDataException("That could be a day or a speaker. Use 'on' for a day or 'by' for a speaker.");
        var weekdays = weekdayMatches
            .Where(match => !InSpeakerName(match) &&
                (Matches(parsedRequest[..match.Index], @"\b(?:on|for)\s+$") || match.Index < speakerStart))
            .Select(match => match.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (dates.Length > 1 || weekdays.Length > 1)
            throw new InvalidDataException("Ask supports one conference day at a time.");
        if (dates.Length == 1 && weekdays.Length == 1 &&
            (!DateOnly.TryParseExact(dates[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var requestedDate) ||
             !requestedDate.DayOfWeek.ToString().Equals(weekdays[0], StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The weekday and date don't match. Use one conference day and try again.");
        var literal = dates.SingleOrDefault() ?? weekdays.SingleOrDefault();
        if (literal is null)
            return intent.Day.Length > 0 &&
                (clause.Success && clause.Groups["speaker"].Value.Contains(intent.Day, StringComparison.OrdinalIgnoreCase) ||
                 intent.Day.Length >= 3 && weekdayMatches.Any(match => InSpeakerName(match) &&
                     match.Value.StartsWith(intent.Day, StringComparison.OrdinalIgnoreCase)))
                ? intent with { Day = "" } : intent;
        if (intent.Day.Length > 0 && !intent.Day.Equals(literal, StringComparison.OrdinalIgnoreCase) &&
            !(DateOnly.TryParseExact(intent.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
              && weekdays.Length == 1 && date.DayOfWeek.ToString().Equals(weekdays[0], StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The model changed your requested conference day. Try again.");
        // Keep the user's weekday, rather than letting a model choose one of several matching dates.
        return intent with { Day = literal };
    }

    public static AskIntent Ground(ConferenceDocument document, string request, AskIntent intent)
    {
        intent = MarkExactTitle(document, request, intent);
        ValidateLiteralTitleSyntax(document, request, intent);
        var grounded = MarkExactTitle(document, request, GroundCandidate(document, request, intent));
        ValidateForOrOfScope(document, request, grounded);
        ValidateMentionedSpeakers(document, request, grounded);
        ValidateUnqualifiedSpeakerParts(document, request, grounded);
        return grounded;
    }

    public static AskIntent GroundLookup(ConferenceDocument document, string request, AskIntent intent) =>
        intent.Query is { Length: 0 } && intent.Speaker is { Length: 0 }
            ? MarkExactTitle(document, request, GroundCandidate(document, request, intent))
            : Ground(document, request, intent);

    private static AskIntent GroundCandidate(ConferenceDocument document, string request, AskIntent intent)
    {
        foreach (var value in new[] { intent.Query, intent.Day, intent.Speaker })
            if (value is null || value.Length > 100)
                throw new InvalidDataException("The model returned an invalid search.");
        ValidateScheduleReferences(document, request, intent);
        intent = MarkExactTitle(document, request, intent);
        intent = GroundDay(document, request, intent);
        if (intent.Action == AskAction.SaveSession)
            intent = GroundSaveScope(document, request, intent);
        if (intent.Action == AskAction.RemoveSessions && HasAllRemovalScope(request, intent.Day))
            intent = intent with { Query = "", Speaker = "" };
        if (intent.Action == AskAction.OpenSpeaker && intent.Speaker.Length == 0 &&
            document.Speakers.Any(s => s.Name.Equals(intent.Query, StringComparison.OrdinalIgnoreCase)))
            intent = intent with { Speaker = intent.Query, Query = "" };
        if (intent.Speaker.Length > 0 && ExplicitSpeaker(request, intent.Day, intent.ExactTitle ? intent.Query : null,
            intent.Action == AskAction.RemoveSessions) is { Length: > 0 } explicitSpeaker &&
            (explicitSpeaker.StartsWith(intent.Speaker, StringComparison.OrdinalIgnoreCase) ||
             explicitSpeaker.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part.StartsWith(intent.Speaker, StringComparison.OrdinalIgnoreCase))))
            intent = intent with { Speaker = explicitSpeaker };
        intent = GroundTopicDaySearch(document, request, intent);
        intent = GroundExplicitSpeakerSearch(request, intent);
        if (intent.Speaker.Length == 0 || SpeakerClause(request, intent.ExactTitle ? intent.Query : null).Success)
            return intent;
        if (IsTopicAlias(document, intent.Speaker))
        {
            if (document.Speakers.Any(s => s.Name.Equals(intent.Speaker, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("That name is ambiguous. Ask for sessions by a specific speaker.");
            return GroundTopicDaySearch(document, request, intent with
            {
                Query = intent.Query.Contains(intent.Speaker, StringComparison.OrdinalIgnoreCase)
                    ? intent.Query : string.Join(" ", new[] { intent.Query, intent.Speaker }.Where(s => s.Length > 0)),
                Speaker = ""
            });
        }
        if (FindSpeakers(document, intent.Speaker).Count > 0)
            return intent;
        // A lookup session ID is not a speaker filter and must not choose one of several matching talks.
        if (intent.Query.Length > 0 && !Matches(MaskExactTitle(request, intent.ExactTitle ? intent.Query : null), @"\bby\s+\S") &&
            !request.Contains(intent.Speaker, StringComparison.OrdinalIgnoreCase) &&
            Agenda.Search(document, intent.Query, null, null, TimeZoneInfo.Utc).Any(s => s.Id == intent.Speaker))
            return intent with { Speaker = "" };
        if (intent.Day.Length > 0 && intent.Speaker.Equals(intent.Day, StringComparison.OrdinalIgnoreCase))
            return GroundTopicDaySearch(document, request, intent with { Speaker = "" });
        return intent;
    }

    private static AskIntent GroundTopicDaySearch(ConferenceDocument document, string request, AskIntent intent)
    {
        if (intent.Action != AskAction.FindSessions || intent.Day.Length == 0 ||
            intent.Speaker.Length > 0 && FindSpeakers(document, intent.Speaker).Count > 0)
            return intent;
        var topicAndDay = Match(request,
            $@"^\s*(?:(?:please|find|show|search|browse)\s+)*(?<phrase>(?<topic>.+?)(?:\s+(?:talks?|sessions?))?\s+(?:on|for)\s+{Regex.Escape(intent.Day)})\s*[?.!,]*\s*$");
        var topic = topicAndDay.Groups["topic"].Value;
        var phrase = topicAndDay.Groups["phrase"].Value;
        var query = intent.Query.Trim().TrimEnd('.', '!', '?', ',').TrimEnd();
        var words = topic.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var terms = words.Where(term => !term.Equals("and", StringComparison.OrdinalIgnoreCase) &&
            !term.Equals("or", StringComparison.OrdinalIgnoreCase)).ToArray();
        var singleTopic = IsTopicAlias(document, topic);
        var exactTopic = document.Sessions.Concat(document.WithdrawnSessions).SelectMany(session => session.Topics)
            .Any(name => name.Equals(topic, StringComparison.OrdinalIgnoreCase));
        if (!exactTopic && words.Length > terms.Length && terms.Length > 1 &&
            terms.All(term => IsTopicAlias(document, term)))
            throw new InvalidDataException("Ask can't safely combine these topics in one search. Try them separately.");
        var knownTopic = singleTopic || terms.Length > 1 &&
            terms.All(term => IsTopicAlias(document, term));
        var partialTopic = query.Length > 0 && query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(term => terms.Contains(term, StringComparer.OrdinalIgnoreCase));
        if (!topicAndDay.Success ||
            query.Length > 0 && !query.Equals(topic, StringComparison.OrdinalIgnoreCase) &&
                !query.Equals(phrase, StringComparison.OrdinalIgnoreCase) && !partialTopic ||
            !knownTopic || terms.Length > 1 && terms.Any(term => FindSpeakers(document, term).Count > 0) ||
            document.Speakers.Any(s => s.Name.Equals(topic, StringComparison.OrdinalIgnoreCase)))
            return intent;
        return intent with { Query = topic, Speaker = "" };
    }

    private static AskIntent GroundExplicitSpeakerSearch(string request, AskIntent intent)
    {
        if (intent.Action != AskAction.FindSessions || intent.Speaker.Length > 0 ||
            ExplicitSpeaker(request, intent.Day, intent.ExactTitle ? intent.Query : null) is not { Length: > 0 } speaker)
            return intent;
        var clause = SpeakerClause(request, intent.ExactTitle ? intent.Query : null);
        if (!clause.Groups["preposition"].Value.Equals("by", StringComparison.OrdinalIgnoreCase))
            return intent;
        var prefix = request[..clause.Groups["preposition"].Index];
        if (intent.Day.Length > 0)
            prefix = Replace(prefix, $@"\b{Regex.Escape(intent.Day)}\b", "");
        if (!Matches(prefix,
            @"^\s*(?:(?:please|find|show|search|browse|open|me|the|all|every|sessions?|talks?|conference|agenda|on|for)\b[\s?.!,]*)*$"))
            return intent;
        return intent with { Query = "", Speaker = speaker };
    }

    public static AskResolution Resolve(ConferenceDocument document, AskIntent intent, TimeZoneInfo zone,
        IReadOnlyCollection<string> savedIds)
    {
        DateOnly? day = null;
        if (intent.Day.Length > 0)
        {
            var days = document.Sessions.Concat(document.WithdrawnSessions)
                .Select(s => Agenda.LocalDay(s.StartsAt, zone)).Distinct().Order().ToArray();
            var matchingDays = days.Where(d => MatchesDay(d, intent.Day)).ToArray();
            if (matchingDays.Length == 0)
                return new(intent, [], "That day isn't in the downloaded agenda. Try a conference weekday or yyyy-MM-dd date.");
            if (matchingDays.Length > 1)
                return new(intent, matchingDays.Select(d => new AskChoice(
                    d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), d.ToString("dddd, MMMM d"), "Device-local date")).ToArray(),
                    "Which conference date?", true);
            day = matchingDays[0];
        }

        var source = intent.Action == AskAction.RemoveSessions
            ? document with { Sessions = document.Sessions.Concat(document.WithdrawnSessions).DistinctBy(s => s.Id).ToList(),
                Speakers = document.Speakers.Concat(document.WithdrawnSpeakers).DistinctBy(s => s.Id).ToList() }
            : document;
        var speakers = intent.Speaker.Length == 0 ? [] : FindSpeakers(source, intent.Speaker).ToArray();
        if (intent.Speaker.Length > 0 && speakers.Length == 0)
            return new(intent, [], $"That speaker isn't in the downloaded agenda: {intent.Speaker}. Nothing changed.");
        var includeSpeakerNames = intent.Speaker.Length == 0 &&
            intent.Action is not (AskAction.SaveSession or AskAction.RemoveSessions);
        var sessions = Search(source, intent.Query, day, null, zone, includeSpeakerNames)
            .Where(s => !intent.ExactTitle || s.Title.Equals(intent.Query, StringComparison.OrdinalIgnoreCase))
            .Where(s => intent.Speaker.Length == 0 || s.SpeakerIds.Any(id => speakers.Any(p => p.Id == id)))
            .Where(s => intent.Action != AskAction.RemoveSessions || savedIds.Contains(s.Id)).ToArray();
        if (intent.Action == AskAction.OpenSpeaker)
        {
            var people = intent.Speaker.Length > 0 && intent.Query.Length == 0 && day is null
                ? speakers
                : source.Speakers.Where(p => sessions.Any(s => s.SpeakerIds.Contains(p.Id))).ToArray();
            return new(intent, people.Select(p => new AskChoice(p.Id, p.Name, p.Tagline)).ToArray(),
                people.Length == 0 ? "No matching speaker in the downloaded agenda. Nothing changed." : "Choose a speaker.");
        }
        return new(intent, sessions.Select(s => new AskChoice(s.Id, s.Title,
            $"{TimeZoneInfo.ConvertTime(s.StartsAt, zone):ddd, MMM d HH:mm} - {TimeZoneInfo.ConvertTime(s.EndsAt, zone):HH:mm}")).ToArray(),
            sessions.Length == 0 ? intent.Action == AskAction.RemoveSessions
                ? "No matching saved sessions. Your schedule is unchanged."
                : "No matching sessions in the downloaded agenda. Nothing changed."
            : intent.Action == AskAction.RemoveSessions ? $"Remove {sessions.Length} saved session(s)?"
            : "Choose a session.");
    }

    public static bool CanOfferAction(string request, AskAction action)
    {
        var command = Match(request,
            CommandPrefixPattern + @"(?<verb>remove|unsave|delete|clear|save|add|bookmark|heart|favou?rite|who|giving|open|details|profile|take|find|search|browse|show)\b")
            .Groups["verb"].Value.ToLowerInvariant();
        return command switch
        {
            "remove" or "unsave" or "delete" or "clear" => action == AskAction.RemoveSessions,
            "save" or "add" or "bookmark" or "heart" or "favorite" or "favourite" => action == AskAction.SaveSession,
            "who" or "giving" => action == AskAction.OpenSpeaker,
            "open" or "details" or "profile" or "take" => action is AskAction.OpenSession or AskAction.OpenSpeaker,
            _ => action == AskAction.FindSessions
        };
    }

    public static DateOnly? ResolveDay(ConferenceDocument document, AskIntent intent, TimeZoneInfo zone) =>
        intent.Day.Length == 0 ? null : document.Sessions.Select(s => Agenda.LocalDay(s.StartsAt, zone))
            .Distinct().Single(d => MatchesDay(d, intent.Day));

    private static bool MatchesDay(DateOnly day, string value) =>
        day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture).Equals(value, StringComparison.OrdinalIgnoreCase) ||
        day.ToString("dddd", CultureInfo.InvariantCulture).Equals(value, StringComparison.OrdinalIgnoreCase) ||
        day.ToString("ddd", CultureInfo.InvariantCulture).Equals(value, StringComparison.OrdinalIgnoreCase) ||
        day.ToString("MMMM d", CultureInfo.InvariantCulture).Equals(value, StringComparison.OrdinalIgnoreCase) ||
        day.ToString("MMM d", CultureInfo.InvariantCulture).Equals(value, StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<Session> Search(ConferenceDocument document, string query, DateOnly? day,
        IReadOnlyCollection<string>? topics, TimeZoneInfo zone, bool includeSpeakerNames = true)
    {
        var results = Agenda.Search(document, query, day, topics, zone);
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(term => (Pattern: WholeTermPattern(term), IsTopic: IsTopicAlias(document, term))).ToArray();
        if (terms.Length == 0)
            return results;
        var speakers = document.Speakers.ToDictionary(speaker => speaker.Id, speaker => speaker.Name);
        return results.Where(session =>
        {
            var text = string.Join(' ', session.Title, session.Description, string.Join(' ', session.Topics));
            var searchable = string.Join(' ', text, string.Join(' ', session.SpeakerIds.Select(id => speakers[id])));
            return terms.All(term => Matches(term.IsTopic || !includeSpeakerNames ? text : searchable, term.Pattern));
        });
    }

    public static IReadOnlyList<Speaker> FindSpeakers(ConferenceDocument document, string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return [];
        var fullName = string.Join(' ', parts);
        var exact = document.Speakers.Where(speaker =>
            string.Join(' ', speaker.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Equals(fullName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length > 0 || parts.Length > 1)
            return exact;
        return document.Speakers.Where(speaker => speaker.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(part => part.Equals(fullName, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    private static bool HasAllRemovalScope(string request, string day) =>
        Matches(TrimPoliteSuffix(RemoveDay(request, day)),
            CommandPrefixPattern + @"(?:remove|unsave|delete|clear)\s+(?:everything|all|(?:all(?:\s+of)?|every)(?:\s+(?:my|the))?(?:\s+saved)?\s+(?:sessions?|talks?))\s*$");

    private static AskIntent MarkExactTitle(ConferenceDocument document, string request, AskIntent intent)
    {
        if (intent.Query is null || intent.Query.Length > 100 || intent.Speaker is null || intent.Speaker.Length > 100)
            throw new InvalidDataException("The model returned an invalid search.");
        var explicitDay = Matches(intent.Query,
            @"^(?:Sunday|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|\d{4}-\d{2}-\d{2})$") &&
            Matches(request, $@"\b(?:on|for)\s+{Regex.Escape(intent.Query)}(?![\p{{L}}\p{{N}}])");
        var outsideTitle = MaskExactTitle(request, intent.Query);
        var speakerClause = SpeakerClause(outsideTitle);
        var removalScope = speakerClause.Success ? outsideTitle[..speakerClause.Groups["preposition"].Index] : outsideTitle;
        if (!speakerClause.Success && intent.Speaker.Length > 0)
        {
            foreach (var mention in IntentSpeakerMentions(removalScope, intent.Speaker, requireMarkedGeneric: true).Reverse())
                removalScope = removalScope.Remove(mention.Index, mention.Length);
        }
        var keywordSearch = !Matches(intent.Query, TitleSyntaxPattern) && SpeakerMentions(document, intent.Query).Length == 0 &&
            (intent.Action == AskAction.FindSessions ||
             intent.Action == AskAction.SaveSession && Matches(request, BulkSavePattern) ||
             intent.Action == AskAction.RemoveSessions &&
                Matches(removalScope, @"\b(?:all|every|sessions|talks)\b"));
        var exact = intent.Query.Length > 0 && !explicitDay && !keywordSearch && !IsTopicAlias(document, intent.Query) &&
            document.Sessions.Concat(document.WithdrawnSessions)
                .Any(session => session.Title.Equals(intent.Query, StringComparison.OrdinalIgnoreCase)) &&
            Matches(request, WholeTermPattern(intent.Query));
        return intent with { ExactTitle = exact };
    }

    private static string MaskExactTitle(string request, string? exactTitle)
    {
        if (string.IsNullOrEmpty(exactTitle))
            return request;
        var match = Match(request, WholeTermPattern(exactTitle));
        return match.Success
            ? request[..match.Index] + new string(' ', match.Length) + request[(match.Index + match.Length)..]
            : request;
    }

    private static bool IsQuotedLiteral(string text, Match match)
    {
        var start = match.Index - 1;
        var end = match.Index + match.Length;
        while (start >= 0 && char.IsWhiteSpace(text[start]))
            start--;
        while (end < text.Length && char.IsWhiteSpace(text[end]))
            end++;
        if (start < 0 || end >= text.Length ||
            start > 0 && char.IsLetterOrDigit(text[start - 1]) ||
            end + 1 < text.Length && char.IsLetterOrDigit(text[end + 1]))
            return false;
        return (text[start], text[end]) is ('"', '"') or ('“', '”') or ('\'', '\'') or ('‘', '’');
    }

    private static Match[] IntentSpeakerMentions(string text, string speaker, bool requireMarkedGeneric)
    {
        var matches = MatchAll(text, WholeTermPattern(speaker)).ToArray();
        if (!IgnoredScopeTerms.Contains(speaker))
            return matches;
        // A surname like "Sessions" must not consume the separate plural noun in the request.
        var marked = matches.Where(match =>
            Matches(text[..match.Index], @"\b(?:for|of|from)\s+[""“‘'(\[]?\s*$") ||
            Matches(text[(match.Index + match.Length)..], @"^(?:[""”’'](?=['’]))?['’](?:s\b)?")).ToArray();
        return marked.Length > 0 || requireMarkedGeneric ? marked : matches;
    }

    private static void ValidateLiteralTitleSyntax(ConferenceDocument document, string request, AskIntent intent)
    {
        var titles = GroundedTitleMatches(document, request);
        if (titles.Length == 0)
            return;
        if (intent.ExactTitle && intent.Action is AskAction.FindSessions or AskAction.SaveSession or AskAction.RemoveSessions &&
            titles.Any(title => title.Title.Equals(intent.Query, StringComparison.OrdinalIgnoreCase) &&
                IsQuotedLiteral(request, title.Match) &&
                Matches(request[..title.Match.Index], @"\b(?:sessions|talks)\s+(?:about|on|for)\b[^\p{L}\p{N}]*$")))
            throw new InvalidDataException("That quoted phrase could match multiple sessions. Use normal search and hearts to choose them.");
        var markers = MatchAll(request, TitleSyntaxPattern);
        foreach (Match marker in markers)
        {
            var containing = titles.Where(title => marker.Index >= title.Match.Index &&
                marker.Index + marker.Length <= title.Match.Index + title.Match.Length).ToArray();
            if (containing.Length == 0)
                continue;
            if (marker.Groups["day"].Success &&
                containing.All(title => title.Title.Equals(marker.Value, StringComparison.OrdinalIgnoreCase)) &&
                Matches(request[..marker.Index], @"\b(?:on|for)\s+$"))
                continue;
            var exact = containing.FirstOrDefault(title => intent.ExactTitle &&
                title.Title.Equals(intent.Query, StringComparison.OrdinalIgnoreCase));
            if (exact.Match is null)
                throw new InvalidDataException("That request could be a title or a separate filter. Use the full title or an explicit day or speaker.");
            if (IsQuotedLiteral(request, exact.Match))
                continue;
            if (intent.Action is AskAction.SaveSession or AskAction.RemoveSessions)
                throw new InvalidDataException("That title could also be a day or speaker filter. Use normal search and hearts to choose the intended session.");
            if (marker.Groups["preposition"].Success &&
                CouldBeSpeaker(document, marker.Groups["preposition"].Value,
                    request[(marker.Index + marker.Length)..(exact.Match.Index + exact.Match.Length)]))
                throw new InvalidDataException("That title could also be a speaker filter. Use the full title in quotes or normal search.");
        }
    }

    private static (string Title, Match Match)[] GroundedTitleMatches(ConferenceDocument document, string request) =>
        document.Sessions.Concat(document.WithdrawnSessions)
            .Select(session => session.Title).Where(title => title.Length > 0 &&
                request.Contains(title, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(title => MatchAll(request, WholeTermPattern(title))
                .Select(match => (Title: title, Match: match))).ToArray();

    private static bool CouldBeSpeaker(ConferenceDocument document, string preposition, string text)
    {
        if (SpeakerMentions(document, text).Length > 0)
            return true;
        var terms = TokenMatches(text).ToArray();
        if (terms.Length == 0)
            return false;
        if (terms[0].Value.Equals("a", StringComparison.OrdinalIgnoreCase) ||
            terms[0].Value.Equals("an", StringComparison.OrdinalIgnoreCase))
            return true;
        if (terms[0].Value.Equals("the", StringComparison.OrdinalIgnoreCase))
            return terms.Length > 1 && char.IsLetter(terms[1].Value[0]);
        if (FindSpeakers(document, terms[0].Value).Count > 0)
            return true;
        return preposition.Equals("by", StringComparison.OrdinalIgnoreCase) && terms.Length == 1 ||
            char.IsUpper(terms[0].Value[0]) || terms.Length > 1 &&
            text[(terms[0].Index + terms[0].Length)..terms[1].Index].All(char.IsWhiteSpace) &&
            !terms[0].Value.EndsWith("ing", StringComparison.OrdinalIgnoreCase) &&
            !terms[1].Value.EndsWith("ing", StringComparison.OrdinalIgnoreCase) &&
            !new[] { "on", "in", "with", "for", "of", "to" }.Contains(terms[1].Value, StringComparer.OrdinalIgnoreCase);
    }

    private static Match SpeakerClause(string request, string? exactTitle = null) => Match(MaskExactTitle(request, exactTitle),
        @"\b(?:(?<preposition>by)|(?:sessions?|talks?)\s+(?<preposition>with))\s+(?<speaker>.+)$");

    private static string? ExplicitSpeaker(string request, string day, string? exactTitle = null,
        bool includeSavedLocation = false)
    {
        var clause = SpeakerClause(request, exactTitle);
        if (!clause.Success)
            return null;
        var speaker = TrimPoliteSuffix(clause.Groups["speaker"].Value);
        speaker = TrimScheduleDestination(speaker, includeSavedLocation);
        if (day.Length > 0)
            speaker = Replace(speaker, $@"\s+(?:on|for)\s+{DayPattern(day)}\s*$", "");
        return TrimScheduleDestination(speaker, includeSavedLocation).Trim().TrimEnd('.', '!', '?', ',').TrimEnd();
    }

    private static void ValidateRequestedScope(ConferenceDocument? document, string request, AskIntent intent, Match speakerClause)
    {
        var scope = speakerClause.Success ? request[..speakerClause.Groups["preposition"].Index] : request;
        scope = Replace(scope,
            CommandPrefixPattern + @"(?:(?:find|show|search|browse|open|take|save|add|bookmark|heart|favou?rite|remove|unsave|delete|clear)\b\s*|who(?:['’]s|\s+is)\b\s*(?:giving\s+)?)?", "");
        scope = TrimPoliteSuffix(scope);
        var mentions = intent.Speaker.Length > 0
            ? IntentSpeakerMentions(scope, intent.Speaker, requireMarkedGeneric: false) : [];
        var literalScope = MaskExactTitle(scope, intent.ExactTitle ? intent.Query : null);
        foreach (Match possessive in MatchAll(literalScope, @"(?<![\p{L}\p{N}])(?<name>[\p{L}\p{N}]+)(?:[""”’'](?=['’]))?['’](?:s\b)?"))
        {
            var name = possessive.Groups["name"];
            if (IgnoredScopeTerms.Contains(name.Value) && !mentions.Any(mention =>
                name.Index >= mention.Index && name.Index + name.Length <= mention.Index + mention.Length))
                throw new InvalidDataException("The model didn't preserve your speaker filter. Try 'by' followed by the speaker's full name.");
        }
        var relationalScope = RemoveDay(literalScope, intent.Day);
        var relatedMentions = intent.Speaker.Length > 0
            ? IntentSpeakerMentions(relationalScope, intent.Speaker, requireMarkedGeneric: false) : [];
        foreach (Match relation in MatchAll(relationalScope, @"\b(?<preposition>for|of|from)\s+[""“‘'(\[]?\s*(?<name>[\p{L}\p{N}]+)"))
        {
            var name = relation.Groups["name"];
            var partitive = relation.Groups["preposition"].Value.Equals("of", StringComparison.OrdinalIgnoreCase) &&
                (name.Value.Equals("my", StringComparison.OrdinalIgnoreCase) || name.Value.Equals("the", StringComparison.OrdinalIgnoreCase)) &&
                Matches(relationalScope[..relation.Index], @"\ball\s+$");
            var titleOrTopic = document is not null &&
                (relation.Groups["preposition"].Value.Equals("of", StringComparison.OrdinalIgnoreCase) ||
                 relation.Groups["preposition"].Value.Equals("from", StringComparison.OrdinalIgnoreCase)) &&
                document.Sessions.Concat(document.WithdrawnSessions).Any(session =>
                    session.Title.Equals(intent.Query, StringComparison.OrdinalIgnoreCase) ||
                    session.Topics.Any(topic => topic.Equals(intent.Query, StringComparison.OrdinalIgnoreCase))) &&
                MatchAll(relationalScope, WholeTermPattern(intent.Query))
                    .Any(match => relation.Index >= match.Index && relation.Index + relation.Length <= match.Index + match.Length &&
                        (name.Value.Equals("the", StringComparison.OrdinalIgnoreCase) || IsQuotedLiteral(relationalScope, match)));
            if (IgnoredScopeTerms.Contains(name.Value) && !name.Value.Equals("me", StringComparison.OrdinalIgnoreCase) &&
                !partitive && !titleOrTopic && !relatedMentions.Any(mention =>
                    name.Index >= mention.Index && name.Index + name.Length <= mention.Index + mention.Length))
                throw new InvalidDataException("The model didn't preserve your speaker filter. Try 'by' followed by the speaker's full name.");
        }
        if (!speakerClause.Success && intent.Speaker.Length > 0)
        {
            var mention = mentions.LastOrDefault();
            if (mention is not null)
            {
                var end = mention.Index + mention.Length;
                if (TokenMatches(scope)
                    .Any(term => term.Index < end && term.Index + term.Length > mention.Index &&
                        (term.Index < mention.Index || term.Index + term.Length > end)))
                    throw new InvalidDataException("A speaker name cannot split a topic or title. Use 'by' followed by the speaker's full name.");
                scope = scope.Remove(mention.Index, mention.Length);
            }
        }
        scope = TrimScheduleDestination(RemoveDay(scope, intent.Day), intent.Action == AskAction.RemoveSessions);
        var requested = SearchTerms(scope).Where(term => !IgnoredScopeTerms.Contains(term));
        var actual = SearchTerms(intent.Query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (document is not null && GroundedTitleMatches(document, request)
            .Where(title => IsQuotedLiteral(request, title.Match))
            .Any(title =>
            {
                var terms = SearchTerms(title.Title).ToArray();
                return terms.Length > 0 && terms.All(IgnoredScopeTerms.Contains) && terms.Any(term => !actual.Contains(term));
            }))
            throw new InvalidDataException("The model didn't preserve your quoted title. Try the full title or normal search.");
        if (requested.Any(term => !actual.Contains(term)))
            throw new InvalidDataException("The model didn't preserve your search. Try a short title, topic or speaker name.");
    }

    private static IEnumerable<string> SearchTerms(string value) =>
        TokenMatches(value).Select(match => match.Value);

    private static bool HasSplitSpeakerScope(string request, AskIntent intent)
    {
        if (intent.Query.Length == 0 || intent.Speaker.Length == 0)
            return false;
        var queryTerms = SearchTerms(intent.Query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var speakerTerms = SearchTerms(intent.Speaker).ToArray();
        var speakerSet = speakerTerms.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var speakerPairs = speakerTerms.Zip(speakerTerms.Skip(1), (left, right) => left + "\0" + right)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requested = TokenMatches(request).ToArray();
        var speakerMentions = IntentSpeakerMentions(request, intent.Speaker, requireMarkedGeneric: false);
        bool IsSpeakerPart(Match term) => !IgnoredScopeTerms.Contains(term.Value) || speakerMentions.Any(mention =>
            term.Index >= mention.Index && term.Index + term.Length <= mention.Index + mention.Length);
        for (var index = 0; index + 1 < requested.Length; index++)
        {
            var left = requested[index];
            var right = requested[index + 1];
            if ((queryTerms.Contains(left.Value) && speakerSet.Contains(right.Value) && IsSpeakerPart(right) ||
                 speakerSet.Contains(left.Value) && IsSpeakerPart(left) && queryTerms.Contains(right.Value)) &&
                !speakerPairs.Contains(left.Value + "\0" + right.Value))
                return true;
        }
        return false;
    }

    private static void ValidateMentionedSpeakers(ConferenceDocument document, string request, AskIntent intent)
    {
        if (SpeakerClause(request, intent.ExactTitle ? intent.Query : null).Success)
            return;
        var speaker = string.Join(' ', intent.Speaker.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var title = intent.Speaker.Length == 0 && intent.ExactTitle
            ? MatchAll(request, WholeTermPattern(intent.Query)).ToArray()
            : [];
        foreach (var mention in SpeakerMentions(document, request))
        {
            if (speaker.Equals(mention.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!intent.Query.Equals(mention.Name, StringComparison.OrdinalIgnoreCase) &&
                title.Any(match => mention.Start >= match.Index && mention.End <= match.Index + match.Length))
                continue;
            throw new InvalidDataException("Use 'by' followed by the speaker's full name so Ask can keep the right scope.");
        }
    }

    private static void ValidateForOrOfScope(ConferenceDocument document, string request, AskIntent intent)
    {
        if (SpeakerClause(request, intent.ExactTitle ? intent.Query : null).Success)
            return;
        var dayBefore = intent.Day.Length == 0 ? "" : $@"(?:(?:on|for)\s+{DayPattern(intent.Day)}\s+)?";
        var match = Match(request, $@"\b(?:sessions?|talks?)\s+{dayBefore}(?<preposition>for|of|from)\s+(?<scope>.+)$");
        if (!match.Success)
            return;
        var parsedRequest = MaskExactTitle(request, intent.ExactTitle ? intent.Query : null);
        if (parsedRequest[match.Groups["preposition"].Index] == ' ')
            return;
        var scope = TrimScheduleDestination(TrimPoliteSuffix(match.Groups["scope"].Value),
            intent.Action == AskAction.RemoveSessions);
        if (scope.Equals(intent.Day, StringComparison.OrdinalIgnoreCase))
            return;
        if (intent.Day.Length > 0)
            scope = Replace(scope, $@"\s+(?:on|for)\s+{DayPattern(intent.Day)}\s*$", "").Trim();
        var speaker = string.Join(' ', intent.Speaker.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var target = string.Join(' ', scope.Trim().Trim('"', '\'', '“', '”', '‘', '’', '(', ')', '[', ']')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (speaker.Equals(target, StringComparison.OrdinalIgnoreCase))
            return;
        var isTopic = IsTopicAlias(document, target);
        var isSpeaker = FindSpeakers(document, target).Count > 0 ||
            document.WithdrawnSpeakers.Count > 0 &&
            FindSpeakers(document with { Speakers = document.WithdrawnSpeakers }, target).Count > 0;
        if (speaker.Length == 0 && !isSpeaker && isTopic &&
            Matches(intent.Query, WholeTermPattern(target)))
            return;
        throw new InvalidDataException("Use 'by' followed by the speaker's full name, or a specific topic or title.");
    }

    private static void ValidateUnqualifiedSpeakerParts(ConferenceDocument document, string request, AskIntent intent)
    {
        if (intent.Action != AskAction.RemoveSessions || intent.Speaker.Length > 0 ||
            SpeakerClause(request, intent.ExactTitle ? intent.Query : null).Success)
            return;
        var source = document with
        {
            Speakers = document.Speakers.Concat(document.WithdrawnSpeakers).DistinctBy(speaker => speaker.Id).ToList()
        };
        if (FindSpeakers(source, intent.Query).Count > 0 && !IsTopicAlias(document, intent.Query) ||
            !intent.ExactTitle && SearchTerms(intent.Query).Any(term =>
                !IsTopicAlias(document, term) && FindSpeakers(source, term).Count > 0))
            throw new InvalidDataException("Use 'by' followed by the speaker's full name to remove a speaker's sessions.");
    }

    private readonly record struct SpeakerMention(string Name, int Start, int End);

    private static SpeakerMention[] SpeakerMentions(ConferenceDocument document, string text)
    {
        var mentions = new List<SpeakerMention>();
        foreach (var speaker in document.Speakers.Concat(document.WithdrawnSpeakers))
        {
            var parts = speaker.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || !text.Contains(parts[0], StringComparison.OrdinalIgnoreCase) ||
                !text.Contains(parts[^1], StringComparison.OrdinalIgnoreCase))
                continue;
            var pattern = $@"(?<![\p{{L}}\p{{N}}]){string.Join(@"\s+", parts.Select(Regex.Escape))}(?![\p{{L}}\p{{N}}])";
            foreach (Match match in MatchAll(text, pattern))
                mentions.Add(new(string.Join(' ', parts), match.Index, match.Index + match.Length));
        }
        return mentions.ToArray();
    }

    private static void ValidateSingleSaveScope(string request, AskIntent intent)
    {
        var scope = Match(request, @"\b(?:save|add|bookmark|heart|favou?rite)\s+(?:me\s+)?(?<scope>.*)$");
        if (!scope.Success)
            return;
        var text = TrimPoliteSuffix(scope.Groups["scope"].Value);
        var clause = SpeakerClause(text, intent.ExactTitle ? intent.Query : null);
        if (clause.Success)
            text = text[..clause.Groups["preposition"].Index];
        else
        {
            var possessive = Match(text, @"^\s*(?<speaker>.+?)['’]s\b");
            if (possessive.Success)
            {
                if (!possessive.Groups["speaker"].Value.Trim().Equals(intent.Speaker, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The model didn't preserve your speaker filter. Try 'by' followed by the speaker's full name.");
                text = text[possessive.Length..];
            }
        }
        text = TrimScheduleDestination(RemoveDay(text, intent.Day));
        var requested = TokenMatches(text, @"[\p{L}\p{N}]+").Select(match => match.Value)
            .Where(term => !SingleSaveIgnoredTerms.Contains(term));
        var actual = TokenMatches(intent.Query, @"[\p{L}\p{N}]+").Select(match => match.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requested.Any(term => !actual.Contains(term)))
            throw new InvalidDataException("The model didn't preserve your session search. Try a short title, topic or speaker name.");
    }

    private static AskIntent GroundSaveScope(ConferenceDocument document, string request, AskIntent intent)
    {
        var bulk = Match(request, BulkSavePattern);
        if (!bulk.Success)
            return intent with { AllMatches = false };
        var scope = TrimPoliteSuffix(bulk.Groups["scope"].Value);
        var clause = SpeakerClause(scope, intent.ExactTitle ? intent.Query : null);
        var speaker = clause.Success ? ExplicitSpeaker(scope, intent.Day, intent.ExactTitle ? intent.Query : null) ?? "" : "";
        if (clause.Success)
            scope = scope[..clause.Groups["preposition"].Index];
        scope = TrimScheduleDestination(RemoveDay(scope, intent.Day, document));
        scope = Replace(scope.Trim(), @"^(?:sessions?|talks?)\b(?:\s+(?:about|on|for)\b)?\s*|\s+(?:sessions?|talks?)$", "").Trim();
        if (scope.Length == 0 && speaker.Length == 0 && !HasAllSaveScope(document, request, intent.Day))
            throw new InvalidDataException("Name the sessions to save, or explicitly ask to save all sessions on a day.");
        if (!clause.Success && scope.Length > 0)
        {
            var matchingSpeakers = FindSpeakers(document, scope);
            var isTopic = IsTopicAlias(document, scope);
            var isTitle = document.Sessions.Any(session => session.Title.Equals(scope, StringComparison.OrdinalIgnoreCase));
            var matchesContent = matchingSpeakers.Count > 0 &&
                Search(document, scope, null, null, TimeZoneInfo.Utc, includeSpeakerNames: false).Any();
            if (!isTopic && !isTitle && matchingSpeakers.Count == 0 &&
                !SearchTerms(scope).Any(term => !IgnoredScopeTerms.Contains(term)))
                throw new InvalidDataException("Use a specific topic, title or speaker for a bulk save.");
            if (matchingSpeakers.Count > 0 && (matchingSpeakers.Count > 1 || isTopic || isTitle || matchesContent))
                throw new InvalidDataException("That could be a topic, title, or speaker. Use 'by' for a speaker, or normal search and hearts for a topic or title.");
            if (matchingSpeakers.Count == 1 || !isTopic && !isTitle &&
                scope.Equals(intent.Speaker, StringComparison.OrdinalIgnoreCase))
            {
                speaker = scope;
                scope = "";
            }
            else if (!isTopic && !isTitle && intent.Speaker.Length > 0 &&
                request.Contains(intent.Speaker, StringComparison.OrdinalIgnoreCase) &&
                FindSpeakers(document, intent.Speaker).Count > 0)
                throw new InvalidDataException("Use 'by' and the speaker's full name to save a speaker's sessions.");
        }
        if (Matches(scope, @"\b(?:and|or)\b|,") &&
            !document.Sessions.Any(session => session.Title.Equals(scope, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("For bulk saves, use one topic, title or speaker per request. Save different topics separately.");
        return intent with { Query = scope, Speaker = speaker, AllMatches = true };
    }

    private static bool HasAllSaveScope(ConferenceDocument document, string request, string day)
    {
        var scope = TrimScheduleDestination(RemoveDay(TrimPoliteSuffix(request), day, document));
        return Matches(scope, CommandPrefixPattern + @"(?:save|add|bookmark|heart|favou?rite)\s+(?:me\s+)?(?:everything|all|(?:all(?:\s+of)?|every)(?:\s+(?:the|my))?\s+(?:sessions?|talks?))\s*$");
    }

    private static bool IsTopicAlias(ConferenceDocument document, string value) =>
        value.Length > 0 && !value.Equals("and", StringComparison.OrdinalIgnoreCase) &&
        !value.Equals("or", StringComparison.OrdinalIgnoreCase) && document.Sessions.Concat(document.WithdrawnSessions)
            .SelectMany(session => session.Topics).Any(topic =>
            Matches(topic, WholeTermPattern(value)));

    private static string WholeTermPattern(string value)
    {
        var following = value.Length == 1 || value.EndsWith('#') || value.EndsWith('+')
            ? @"[\p{L}\p{N}+#]" : @"[\p{L}\p{N}]";
        return $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(value)}(?!{following})";
    }

    private static string DayPattern(string day) =>
        DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? $@"(?:{date.DayOfWeek}\s*,?\s*)?{Regex.Escape(day)}"
            : Regex.Escape(day);

    private static string RemoveDay(string text, string day, ConferenceDocument? document = null)
    {
        if (day.Length == 0)
            return text;
        var matches = MatchAll(text,
            $@"(?<![\p{{L}}\p{{N}}])(?:(?:on|for)\s+)?(?<day>{DayPattern(day)})(?![\p{{L}}\p{{N}}])");
        var names = document is null ? [] : SpeakerMentions(document, text);
        var match = matches.Cast<Match>().LastOrDefault(candidate => !names.Any(name =>
            candidate.Groups["day"].Index >= name.Start &&
            candidate.Groups["day"].Index + candidate.Groups["day"].Length <= name.End));
        return match is null ? text : text.Remove(match.Index, match.Length);
    }

    private static string TrimPoliteSuffix(string text) => Replace(
        text.Trim().TrimEnd('.', '!', '?', ',').TrimEnd(), @"(?:\s*,\s*|\s+)(?:please|thanks)\s*$", "").TrimEnd();

    private static void ValidateSavedScheduleRestriction(ConferenceDocument? document, string request, AskIntent intent)
    {
        if (intent.Action == AskAction.RemoveSessions)
            return;
        var exactTitle = intent.ExactTitle ? intent.Query : null;
        var personalScope = MaskQuotedGroundedTitles(document, request, intent, out var quotedPersonalScope);
        var scope = MaskExactTitle(personalScope, exactTitle);
        var withoutDestinations = RemoveScheduleDestinations(scope);
        var withoutPersonalDestinations = RemoveScheduleDestinations(personalScope);
        var withoutBookmarkTopics = Replace(withoutDestinations, @"\b(?:sessions?|talks?)\s+about\s+bookmarks\b", "");
        if (MatchAll(scope, ScheduleReferencePattern)
            .Any(match => (match.Groups["preposition"].Value.Equals("in", StringComparison.OrdinalIgnoreCase) ||
                match.Groups["preposition"].Value.Equals("on", StringComparison.OrdinalIgnoreCase)) &&
                !Matches(match.Value, $@"^{ConferenceAgendaLocationPattern}$")) ||
            quotedPersonalScope ||
            Matches(withoutPersonalDestinations, @"\bmy\b[^;\r\n]{0,500}\b(?:sessions?|talks?)\b") ||
            Matches(withoutBookmarkTopics, @"\b(?:saved|bookmarked|favou?rited|bookmarks)\b|\b(?:my|the)\s+(?:favou?rites?|bookmarks?)\b") ||
            exactTitle is not null && Matches(personalScope, WholeTermPattern(exactTitle)) && Matches(exactTitle,
                @"^\s*(?:(?:my|the|a|an)\s+)?(?:saved|bookmarked|favou?rited)\b|\b(?:my|the)\s+(?:saved|bookmarked|favou?rited|favou?rites?|bookmarks?)\b"))
        {
            if (document is not null && SpeakerMentions(document, request).Any(speaker =>
                Matches(speaker.Name, @"\bmy\b") &&
                (Matches(request[speaker.End..], PossessiveSuffixPattern) ||
                 string.Equals(intent.Speaker, speaker.Name, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("That could refer to a speaker or your saved sessions. Try 'by' with the speaker's full name, normal search, or My Schedule.");
            throw new InvalidDataException("Ask can't filter by your existing schedule here. Use My Schedule to review saved sessions.");
        }
    }

    private static string MaskQuotedGroundedTitles(ConferenceDocument? document, string request, AskIntent intent,
        out bool personalScope)
    {
        personalScope = false;
        if (document is null)
            return request;
        var matches = GroundedTitleMatches(document, request)
            .Where(title => IsQuotedLiteral(request, title.Match)).ToArray();
        foreach (var (_, match) in matches)
            request = request[..match.Index] + new string(' ', match.Length) + request[(match.Index + match.Length)..];
        foreach (var speaker in SpeakerMentions(document, request))
            if (Matches(request[speaker.End..], PossessiveSuffixPattern) &&
                (!Matches(speaker.Name, @"\bmy\b") ||
                 !Matches(request[speaker.Start..], @"\b(?:sessions?|talks?)\b") &&
                 (intent.Action == AskAction.OpenSession || !matches.Any(title => title.Match.Index > speaker.End))))
                request = request[..speaker.Start] + new string(' ', speaker.End - speaker.Start) + request[speaker.End..];
        personalScope = matches.Any(title => Matches(RemoveScheduleDestinations(request[..title.Match.Index]),
            @"\bmy\b[^;\r\n]{0,500}[""“‘']\s*$"));
        return request;
    }

    private static string RemoveScheduleDestinations(string text) => Regex.Replace(text, ScheduleReferencePattern,
        match => match.Groups["preposition"].Value.Equals("to", StringComparison.OrdinalIgnoreCase) ||
            match.Groups["preposition"].Value.Equals("into", StringComparison.OrdinalIgnoreCase) ? "" : match.Value,
        MatchOptions, RegexTimeout);

    private static void ValidateScheduleReferences(ConferenceDocument document, string request, AskIntent intent)
    {
        ValidateSavedScheduleRestriction(document, request, intent);
        var references = MatchAll(request, ScheduleReferencePattern).ToArray();
        if (references.Length == 0)
            return;
        foreach (var title in document.Sessions.Concat(document.WithdrawnSessions).Select(session => session.Title)
            .SelectMany(title => new[] { title, title.TrimEnd('.', '!', '?', ',', '…', ')', ']', '}', '"', '\'', '”', '’').TrimEnd() })
            .Where(title => title.Length > 0 && request.Contains(title, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (Match match in MatchAll(request, WholeTermPattern(title)))
                if (references.Any(reference => reference.Index >= match.Index &&
                    reference.Index + reference.Length <= match.Index + match.Length))
                    throw new InvalidDataException("That schedule phrase could be part of a session title. Use normal search and hearts to choose the intended session.");
        }
    }

    private static string TrimScheduleDestination(string text, bool includeSavedLocation = false)
    {
        var location = includeSavedLocation
            ? @"(?:on|in)\s+(?:my|the)\s+(?:(?:saved|conference)\s+)*(?:schedule|agenda|favou?rites|bookmarks?)"
            : ConferenceAgendaLocationPattern;
        var suffix = $@"(?:\s*,)?\s+(?:(?:to|into)\s+(?:my|the)\s+(?:(?:saved|conference)\s+)*(?:schedule|agenda|favou?rites|bookmarks?)|{location})\s*$";
        var result = text.Trim();
        while (true)
        {
            var trimmed = Replace(result, suffix, "").TrimEnd();
            if (trimmed.Length == result.Length)
                return result;
            result = trimmed;
        }
    }

    private static bool Matches(string text, string pattern) => Regex.IsMatch(text, pattern,
        MatchOptions, RegexTimeout);

    private static Match Match(string text, string pattern) =>
        Regex.Match(text, pattern, MatchOptions, RegexTimeout);

    private static MatchCollection MatchAll(string text, string pattern) =>
        Regex.Matches(text, pattern, MatchOptions, RegexTimeout);

    private static MatchCollection TokenMatches(string text, string pattern = SearchTermPattern) =>
        Regex.Matches(text, pattern, RegexOptions.CultureInvariant, RegexTimeout);

    private static string Replace(string text, string pattern, string replacement) =>
        Regex.Replace(text, pattern, replacement, MatchOptions, RegexTimeout);
}
