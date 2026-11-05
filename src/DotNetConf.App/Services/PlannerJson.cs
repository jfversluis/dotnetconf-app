using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Globalization;
using System.Text.RegularExpressions;
using DotNetConf.Core;

namespace DotNetConf.App.Services;

internal static class PlannerJson
{
    public static T? DeserializeResponse<T>(string text, JsonTypeInfo<T> typeInfo)
    {
        var json = text.AsSpan().Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = json.IndexOf('\n');
            if (firstLineEnd < 0)
                throw new JsonException("The model returned an incomplete JSON code block.");
            var language = json[3..firstLineEnd].Trim();
            if (!language.IsEmpty && !language.Equals("json", StringComparison.OrdinalIgnoreCase))
                throw new JsonException("The model returned a code block that is not JSON.");
            json = json[(firstLineEnd + 1)..];
            var lastLineStart = json.LastIndexOf('\n');
            if (lastLineStart < 0 || !json[(lastLineStart + 1)..].Trim().SequenceEqual("```"))
                throw new JsonException("The model returned an incomplete JSON code block or extra text.");
            json = json[..lastLineStart];
        }
        return JsonSerializer.Deserialize(json, typeInfo);
    }
}

public sealed record PlannerInterpretation(string SpeakerNames, string AvailabilityText, string SearchQuery, string Quantity)
{
    private const string FullMonths = @"January|February|March|April|May|June|July|August|September|October|November|December";
    private const string Months = @"Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?";
    private const string Weekdays = @"Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday";
    internal const string DatePattern = @"\b(?:(?<iso>\d{4}-\d{2}-\d{2})|(?<month>" + Months +
        @")\.?\s+(?<day>\d{1,2})(?:st|nd|rd|th)?(?:,?\s+(?<year>\d{4}))?)\b";
    private const string DayParts = @"(?:morning|afternoon|evening|night)s?|(?:early|late)(?!-)|lunchtime";
    private const string AdditionalTimeHints = @"\b(?:(?:morning|afternoon|evening|night)s?|lunchtime|lunch|breakfast|dinner|" +
        @"(?:early|late)\s+(?:morning|afternoon|evening|night|day|hours?)|" +
        @"(?:before|after|during)[ -](?:my\s+)?(?:work|school|commute)|(?:during|while)\s+(?:my|our)\s+\p{L}+|" +
        @"(?:business|working|office)\s+hours|daytime|midday|tonight|(?:early|late)\s+in\s+the\s+day|during\s+the\s+day)\b";
    private const string UnrestrictedTime = @"\b(?:no\s+matter\s+(?:the\s+)?time|regardless\s+of\s+(?:the\s+)?time|any\s*time)\b";
    internal const string ClockPattern = @"(?:(?<named>noon|midnight)|(?<hour>\d{1,2})(?::(?<minute>\d{2}))?\s*(?<period>am|pm)?)";
    internal const string ClockRangeSeparator = @"(?:\s+(?:to|until|through)\s+|\s*[-–—]\s*)";
    internal const string QuantityNoun = @"(?:(?:of|out\s+of)\s+(?:(?:the|these|those)\s+)?(?:\d+\s+)?)?(?:(?!(?:for|about|on|at|in|of|with|during|before|after|from|between)\b)[\p{L}+#.-]+\s+){0,3}(?:sessions?|talks?|workshops?|keynotes?)\b";
    internal const string QuantityRangeSeparator = @"(?:to\b|and\b|or\b|through\b|[-–—])";
    public bool HasAvailability => !string.IsNullOrWhiteSpace(AvailabilityText)
        && !AvailabilityText.Equals("none", StringComparison.OrdinalIgnoreCase);

    public bool RequiresTimeExtraction(string request)
    {
        if (HasExplicitDate(request) || Regex.IsMatch(request,
            @"\b\d{1,2}(?::\d{2})?\s*(?:am|pm)\b|\b\d{1,2}:\d{2}\b|\b(?:before|after|until|by|from|between)\s+(?:\d{1,2}|noon|midnight)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            return true;
        if (HasAdditionalTimeHint(request))
            return true;
        if (Regex.IsMatch(request, UnrestrictedTime,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            return false;
        return HasAvailability && Regex.IsMatch(request,
            $@"\b(?:{DayParts}|noon|midnight|weekends?|weekdays?|hours?|before|after|until|between|during)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    public static bool HasAdditionalTimeHint(string request)
    {
        if (Regex.IsMatch(request, UnrestrictedTime,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            request = Regex.Replace(request, $@"\bincluding\s+(?:the\s+)?{AdditionalTimeHints}",
                match => Agenda.IsNegatedTimeConstraint(request[..match.Index])
                    ? throw new InvalidDataException("Excluding a time period isn't supported yet. Request the hours you can watch.")
                    : "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return Regex.IsMatch(request, AdditionalTimeHints,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private static bool HasClockHint(string request) => Regex.IsMatch(request,
        @"\b(?:\d{1,2}:\d{2}|\d{1,2}\s*(?:am|pm)|noon|midnight)\b|\b(?:before|after|until|by|from)\s+(?:1[3-9]|2[0-3])\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string WithoutTimeHints(string request) => Regex.Replace(request, AdditionalTimeHints, "",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string WithoutClockLimits(string request) => Regex.Replace(request,
        $@"\b(?:before|until|by|after|from|between|earlier than|later than)\s+{ClockPattern}(?:(?:\s+and\s+|{ClockRangeSeparator}){ClockPattern})?\b",
        "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string WithoutCalendarDates(string request)
    {
        var dates = Regex.Matches(request, DatePattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Cast<Match>().ToArray();
        foreach (var date in dates.Reverse())
        {
            var end = date.Index + date.Length;
            end += DateLabelLength(request, end);
            while (DateContinuation(request, end) is { Success: true } continuation)
                end += continuation.Length;
            var prefix = Regex.Match(request[..date.Index],
                $@"\b(?:(?:on|before|after|until|from|since|starting|beginning|between|and|to|through|till|by)\s+)?(?:{Weekdays}),?\s*$|" +
                @"\b(?:on\s+or\s+(?:before|after)|on|before|after|until|from|since|starting|beginning|between|and|to|through|till|by)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var start = prefix.Success ? prefix.Index : date.Index;
            request = request.Remove(start, end - start);
        }
        return request;
    }

    private static DateOnly ReadDate(Match date, int conferenceYear)
    {
        if (date.Groups["iso"].Success)
            return DateOnly.TryParseExact(date.Groups["iso"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var iso) ? iso : throw new InvalidDataException("The requested date is invalid.");
        var year = date.Groups["year"].Success ? int.Parse(date.Groups["year"].Value, CultureInfo.InvariantCulture) : conferenceYear;
        var month = DateTime.ParseExact(date.Groups["month"].Value, ["MMM", "MMMM"], CultureInfo.InvariantCulture,
            DateTimeStyles.None).Month;
        try { return new(year, month, int.Parse(date.Groups["day"].Value, CultureInfo.InvariantCulture)); }
        catch (ArgumentOutOfRangeException exception) { throw new InvalidDataException("The requested date is invalid.", exception); }
    }

    private static Match DateContinuation(string request, int index) => Regex.Match(request[index..],
        @"^\s*(?<separator>,|&|and\b|or\b|plus\b|to\b|through\b|until\b|till\b|[-–—])\s*(?<day>\d{1,2})(?:st|nd|rd|th)?\b(?!\s*(?::\d|am\b|pm\b))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static int DateLabelLength(string request, int index) => Regex.Match(request[index..],
        $@"^\s*(?:\(\s*(?:{Weekdays})\s*\)|,\s*(?:{Weekdays})\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Length;

    private sealed record SourceDates(DateOnly[] Days, bool IsRange, string? Boundary)
    {
        public DateOnly First => Days[0];
        public DateOnly Last => Days[^1];

        public bool Contains(DateOnly day) => Boundary switch
        {
            "before" => day < First,
            "after" => day > First,
            "until" => day <= First,
            not null => day >= First,
            _ => IsRange ? day >= First && day <= Last : Days.Contains(day)
        };
    }

    private static SourceDates? ReadSourceDates(string request, int conferenceYear)
    {
        var matches = Regex.Matches(request, DatePattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Cast<Match>().ToArray();
        if (matches.Length == 0)
            return null;
        var labels = Regex.Matches(request, $@"\b(?:{Weekdays}|today|tomorrow)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var labelledDates = new HashSet<int>();
        foreach (Match label in labels)
        {
            if (!Enum.TryParse<DayOfWeek>(label.Value, true, out var day))
                throw new InvalidDataException("Use matching weekdays and calendar dates, or name each day explicitly.");
            var date = matches.FirstOrDefault(match => !labelledDates.Contains(match.Index)
                && ReadDate(match, conferenceYear).DayOfWeek == day
                && (label.Index + label.Length <= match.Index && request[(label.Index + label.Length)..match.Index].Trim() is "" or ","
                    || match.Index + match.Length <= label.Index && request[(match.Index + match.Length)..label.Index].Trim() is "" or "(" or ","));
            if (date is null)
                throw new InvalidDataException("Use matching weekdays and calendar dates, or name each day explicitly.");
            labelledDates.Add(date.Index);
        }
        foreach (var match in matches)
        {
            var beforeDate = Regex.Replace(request[..match.Index], $@"\b(?:{Weekdays}),?\s*$", "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            beforeDate = Regex.Replace(beforeDate, @"\b(?:on|during|before|after|until|from|since)\s*$", "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (Regex.IsMatch(beforeDate, @"\b(?:skip(?:ping)?|ignor(?:e|ing)|other\s+than|apart\s+from)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                throw new InvalidDataException("Excluding dates isn't supported yet. List the days you can watch.");
            if (Agenda.IsNegatedTimeConstraint(beforeDate))
            {
                var clock = Regex.Match(beforeDate, $@"\b(?:before|after|until|by|from)\s+{ClockPattern}\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                var clockAfterDate = Regex.IsMatch(request[(match.Index + match.Length)..],
                    $@"^\s*,?\s*(?:before|after|until|by|from)\s+{ClockPattern}\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                if ((!clock.Success || !Agenda.IsNegatedTimeConstraint(beforeDate[..clock.Index])) && !clockAfterDate)
                    throw new InvalidDataException("Excluding dates isn't supported yet. List the days you can watch.");
            }
        }
        for (var index = 0; index < matches.Length - 1; index++)
        {
            var betweenDates = request[(matches[index].Index + matches[index].Length)..matches[index + 1].Index];
            if (HasAdditionalTimeHint(betweenDates) || HasClockHint(betweenDates) || Regex.IsMatch(betweenDates,
                @"\b(?:before|after|since|starting|beginning)\s*$|\b(?:and|but|or)\s+(?:until|from)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                throw new InvalidDataException("For different dates or time limits, use separate planner requests.");
        }
        var dates = matches.Select(match => ReadDate(match, conferenceYear)).ToHashSet();
        var leading = Regex.Replace(request[..matches[0].Index], $@"\b(?:{Weekdays}),?\s*$", "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var boundary = Regex.Match(leading,
            @"\b(?<direction>on\s+or\s+after|on\s+or\s+before|by|through|from|after|since|starting|beginning|before|until)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var direction = boundary.Groups["direction"].Value.ToLowerInvariant() switch
        {
            var value when value.StartsWith("on", StringComparison.Ordinal) =>
                value.EndsWith("after", StringComparison.Ordinal) ? "from" : "until",
            "by" or "through" => "until",
            var value => value
        };
        var between = Regex.IsMatch(request[..matches[0].Index], @"\bbetween\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var continuationRange = false;
        foreach (var match in matches.Where(match => match.Groups["month"].Success))
        {
            var firstDate = ReadDate(match, conferenceYear);
            var previousDate = firstDate;
            var index = match.Index + match.Length;
            index += DateLabelLength(request, index);
            var firstContinuation = index;
            while (DateContinuation(request, index) is { Success: true } continuation)
            {
                var separator = continuation.Groups["separator"].Value.ToLowerInvariant();
                var list = separator is "," or "&" or "and" or "or" or "plus";
                if (list && index == firstContinuation && Regex.IsMatch(request[(index + continuation.Length)..],
                    $@"^\s*(?:{QuantityRangeSeparator}\s*\d{{1,2}}\s+)?{QuantityNoun}",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                    throw new InvalidDataException("That number could be a day or a session count. Repeat the month for each date or remove the session count.");
                continuationRange |= !list;
                try
                {
                    var date = new DateOnly(firstDate.Year, firstDate.Month, int.Parse(continuation.Groups["day"].Value, CultureInfo.InvariantCulture));
                    if ((!list || between) && date < previousDate)
                        throw new InvalidDataException("The date range ends before it starts.");
                    dates.Add(date);
                    previousDate = date;
                }
                catch (ArgumentOutOfRangeException exception) { throw new InvalidDataException("The requested date is invalid.", exception); }
                index += continuation.Length;
            }
            if (Regex.IsMatch(request[index..],
                @"^\s*,?\s*(?:(?:and|but)\s+)?(?:not|except|exclud(?:e|ing)|skip(?:ping)?)\s+(?:the\s+)?\d{1,2}(?:st|nd|rd|th)?\b(?!\s*(?::\d|am\b|pm\b))",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                throw new InvalidDataException("Excluding dates isn't supported yet. List the days you can watch.");
            if (!boundary.Success && Regex.IsMatch(request[index..],
                @"^\s*(?:(?:,\s*)?(?:and|or)\s+(?:after|later|before|earlier)\b(?!\s+(?:\d|noon\b|midnight\b))|onwards?\b)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                throw new InvalidDataException("Put 'from', 'after' or 'until' ahead of the date.");
        }
        var beforeFirstDate = request[..matches[0].Index];
        var afterLastDate = request[(matches[^1].Index + matches[^1].Length)..];
        if (dates.Count > 1 && (HasAdditionalTimeHint(beforeFirstDate) || HasClockHint(beforeFirstDate))
            && (HasAdditionalTimeHint(afterLastDate) || HasClockHint(afterLastDate)))
            throw new InvalidDataException("For different dates or time limits, use separate planner requests.");
        var namedRange = Enumerable.Range(0, matches.Length - 1).Any(index =>
            Regex.IsMatch(request[(matches[index].Index + matches[index].Length)..matches[index + 1].Index],
                @"(?:\b(?:to|through|until|till)|[-–—])\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
        if ((namedRange || between) && matches.Length == 2
            && ReadDate(matches[0], conferenceYear) > ReadDate(matches[1], conferenceYear))
            throw new InvalidDataException("The date range ends before it starts.");
        if ((continuationRange || namedRange || between) && dates.Count > 2)
            throw new InvalidDataException("List date ranges separately from individual days.");
        if (dates.Count > 1 && boundary.Success
            && (direction != "from" || !(continuationRange || namedRange || between)))
            throw new InvalidDataException("For different date boundaries, use separate planner requests.");
        return new(dates.Order().ToArray(), continuationRange || namedRange || dates.Count == 2 && between,
            dates.Count == 1 && boundary.Success ? direction : null);
    }

    public static bool TryCalendarDates(string request, int conferenceYear, string zone, out AvailabilityWindow[] windows,
        out bool openEnded, ConferenceDocument? conference = null, TimeZoneInfo? local = null)
    {
        windows = [];
        var scope = ReadSourceDates(request, conferenceYear);
        openEnded = scope?.Boundary is not null;
        if (scope is null)
            return false;
        if (openEnded)
        {
            if (conference is null || local is null)
                return false;
            windows = SchedulePlanner.DailyAvailability(conference, local, TimeOnly.MinValue, TimeOnly.MinValue, zone)
                .Where(window => scope.Contains(DateOnly.ParseExact(window.Start[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture)))
                .ToArray();
            if (windows.Length == 0)
                throw new InvalidDataException("No conference dates fit the requested date limit.");
            return true;
        }
        if (scope.Days.Length > 12 || scope.IsRange && scope.Last.DayNumber - scope.First.DayNumber >= 12)
            throw new InvalidDataException("Specify at most twelve conference days.");
        var days = scope.IsRange
            ? Enumerable.Range(0, scope.Last.DayNumber - scope.First.DayNumber + 1).Select(scope.First.AddDays)
            : scope.Days;
        windows = days.Select(day =>
        {
            if (day == DateOnly.MaxValue)
                throw new InvalidDataException("The requested date is too late to form an availability window.");
            return new AvailabilityWindow($"{day:yyyy-MM-dd}T00:00", $"{day.AddDays(1):yyyy-MM-dd}T00:00", zone);
        }).ToArray();
        return true;
    }

    public static bool HasExplicitDate(string request) => Regex.IsMatch(request,
        DatePattern + @"|\b(?:" + FullMonths + "|" + Weekdays + @"|today|tomorrow)\b|\b\d{1,2}/\d{1,2}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string RequestedZone(string request)
    {
        var zones = Regex.Matches(request, @"\bUTC\b|\b[A-Za-z_]+/[A-Za-z_][A-Za-z0-9_+.-]*(?:/[A-Za-z_][A-Za-z0-9_+.-]*)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
            .Select(m => m.Value.TrimEnd('.'))
            .Select(value => value.Equals("UTC", StringComparison.OrdinalIgnoreCase) ? "UTC" : value)
            .Distinct(StringComparer.Ordinal).ToArray();
        return zones.Length switch
        {
            0 => "local", 1 => zones[0],
            _ => throw new InvalidDataException("Use one explicit time zone per request.")
        };
    }

    public bool RequestsAllSpeakers(string request) =>
        Regex.IsMatch(request, @"\b(?:don['’]?t|do not)\s+(?:want\s+to\s+)?miss\s+(?:any|a(?:\s+single)?|one)\s+(?:sessions?|talks?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
        || Quantity == "all" && (HasAllQuantifier(request)
            || Regex.IsMatch(request, @"\b(don['’]?t|do not)\s+miss\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));

    public bool RequestsAllSpeakers(string request, string[] speakers) =>
        Clauses(request).Any(clause => (HasAllQuantifier(clause) || RequestsAllSpeakers(clause))
            && speakers.Any(name => Regex.IsMatch(clause, $@"\b{Regex.Escape(name)}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))));

    public static string[] Clauses(string request) => Regex.Split(request, @"\b(?:and|plus)\b|[,;]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

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
            throw new InvalidDataException("Please ask about each speaker and topic combination separately.");
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
            throw new InvalidDataException("Please ask about each speaker and topic combination separately.");
        var explicitOr = Regex.IsMatch(request, @"\bor\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
        var independentClause = Regex.IsMatch(request, @"\b(?:and|plus)\s+(?:everything|all|every|don['’]?t|do not|I must)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        // A list of interests is a union. Require source evidence that each talk
        // must cover the combination, rather than trusting the model's AND guess.
        var explicitAnd = Regex.IsMatch(request,
            @"\bboth\b|\b(?:all|each|every)\s+(?:(?:of\s+)?(?:these|the|requested)\s+)?topics\b|\b(?:same\s+(?:session|talk)|together|combin(?:e|es|ing|ation))\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var topicClauses = PlannerInterpretation.Clauses(request)
            .Where(clause => Agenda.MentionedTopics(conference, clause).Intersect(topics, StringComparer.OrdinalIgnoreCase).Any()).ToArray();
        var includeAll = hasSpeakers && topicClauses.Length > 0
            ? topicClauses.Any(PlannerInterpretation.HasAllQuantifier) : RequestsAll(request);
        return new(topics, explicitAnd && !explicitOr,
            hasSpeakers && (scopedSpeakers > 0 || (SpeakerScope == "within-topics" && !independentClause)), includeAll);
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
            var remaining = Regex.Replace(PlannerInterpretation.WithoutClockLimits(PlannerInterpretation.WithoutTimeHints(value)),
                @"\b(?:(?:a\s+)?single\s+(?:session|talk)|nothing\s+too\s+[\p{L}\p{M}-]+|(?:not\s+|without\s+)?including\s+overnight)\b", "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            remaining = Regex.Replace(remaining, @"\b\d{1,2}(?::\d{2})?\s*(?:am|pm)\b", "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            remaining = Regex.Replace(remaining, $@"\b(?:{string.Join('|', names)})\b", "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            remaining = Regex.Replace(remaining, @"\b(?:gaps?|fill|fits?|fitting|here|my|schedule|free|slots?|spare|have|what|could|around|commitments|saved|help|suggest|something|attend)\b", "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            return Regex.Replace(remaining,
                @"\b(?:all|every|everything|each|day|daily|sessions?|talks?|show|give|find|me|by|from|with|and|or|of|the|please|s|a|an|I|want|would|like|must|can|see|watch|only|just|available|unavailable|unable|able|availability|on|at|in|before|after|between|until|to|time|local|UTC|am|pm|noon|midnight|don|won|will|t|do|not|nothing|without|avoid|avoiding|except|miss|any|but|exclude|excluding|including|ending|ends?|finish(?:ing|es)?|they|are|for|no|matter|regardless|about|surprise|January|February|March|April|May|June|July|August|September|October|November|December|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\b", "",
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
    internal const string NeedsExplicitTime = "Use explicit start and finish times for this time preference.";
    public string Assumption { get; init; } = "";

    public PlannerDailyAvailability Complete(PlannerWatchingHours hours)
    {
        var (start, end) = hours.Complete(
            PlannerAvailability.ReadDailyLimit(DailyStart), PlannerAvailability.ReadDailyLimit(DailyEnd));
        var startText = start.ToString("HH:mm", CultureInfo.InvariantCulture);
        var endText = end.ToString("HH:mm", CultureInfo.InvariantCulture);
        var zone = Zone == "local" ? "" : $" ({Zone})";
        var assumption = DailyStart == "none" ? $"Assumed daily start: {startText}{zone}."
            : DailyEnd == "none" ? $"Assumed daily finish: {(end == TimeOnly.MinValue ? "midnight" : endText)}{zone}." : "";
        return this with
        {
            DailyStart = startText, DailyEnd = endText,
            Assumption = assumption
        };
    }

    public AvailabilityWindow[] RestrictDatedWindows(AvailabilityWindow[] dated, TimeZoneInfo zone,
        ConferenceDocument conference)
    {
        var limits = ToWindows(zone, conference);
        var result = dated.SelectMany(window => limits.Select(limit =>
        {
            if (window.Zone != limit.Zone)
                throw new InvalidDataException("Use one time zone for dated clock limits.");
            var start = string.CompareOrdinal(window.Start, limit.Start) > 0 ? window.Start : limit.Start;
            var end = string.CompareOrdinal(window.End, limit.End) < 0 ? window.End : limit.End;
            return new AvailabilityWindow(start, end, window.Zone);
        })).Where(window => string.CompareOrdinal(window.Start, window.End) < 0).Distinct().ToArray();
        return result.Length > 0 ? result
            : throw new InvalidDataException("No conference dates fit the requested dates and clock limits.");
    }

    public static AvailabilityWindow CalendarDates(AvailabilityWindow window)
    {
        const string format = "yyyy-MM-dd'T'HH:mm";
        if (!DateTime.TryParseExact(window.Start, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !DateTime.TryParseExact(window.End, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
            || end.Date < start.Date || (end.Date == DateTime.MaxValue.Date
                && (end.Date == start.Date || end.TimeOfDay != TimeSpan.Zero)))
            throw new InvalidDataException("The model returned an invalid dated availability window.");
        if (start.Date != end.Date && end.TimeOfDay != TimeSpan.Zero)
            throw new InvalidDataException("The date span was ambiguous. Try listing the days separately.");
        var endDate = end.Date > start.Date && end.TimeOfDay == TimeSpan.Zero ? end.Date : end.Date.AddDays(1);
        return new(start.Date.ToString(format, CultureInfo.InvariantCulture),
            endDate.ToString(format, CultureInfo.InvariantCulture), window.Zone);
    }

    public PlannerDailyAvailability Ground(string request, bool sourceOnly = false)
    {
        request = Regex.Replace(PlannerInterpretation.WithoutCalendarDates(request), @"\b\d{1,2}/\d{1,2}(?:/\d{2,4})?\b", "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        request = Regex.Replace(request,
            $@"\b(?<minimum>\d{{1,2}})\s*{PlannerInterpretation.QuantityRangeSeparator}\s*(?<maximum>\d{{1,2}})\b(?=\s+{PlannerInterpretation.QuantityNoun})",
            match => int.Parse(match.Groups["minimum"].Value, CultureInfo.InvariantCulture)
                    <= int.Parse(match.Groups["maximum"].Value, CultureInfo.InvariantCulture)
                ? "" : match.Value,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var bareCutoffs = Regex.Matches(request,
            @"\b(?:before|until|by|after|from|between)\s+(?<hour>\d{1,2})\b(?!:\d|\s*(?:am|pm)\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (bareCutoffs.Any(m => int.Parse(m.Groups["hour"].Value, CultureInfo.InvariantCulture) <= 12))
            throw new InvalidDataException("Clarify the clock cutoff with am/pm or HH:mm, such as 9am, 9pm or 09:00.");
        const string clock = PlannerInterpretation.ClockPattern;
        const string separator = PlannerInterpretation.ClockRangeSeparator;
        var range = Regex.Match(request,
            $@"\b(?:(?:from|between)\s+{clock}(?:\s+and\s+|{separator}){clock}|{clock}{separator}{clock})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (range.Success && Agenda.IsNegatedTimeConstraint(request[..range.Index]))
            throw new InvalidDataException("Excluding a time range isn't supported yet. Request the hours you can watch.");
        if (range.Success && !Regex.IsMatch(range.Value, @"^\s*(?:from|between)\b|:|(?:am|pm)\b|\b(?:noon|midnight)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new InvalidDataException("Clarify the clock range with am/pm or HH:mm, such as 9am to 5pm.");
        var bareRangeEnd = Regex.Match(request, $@"\b{clock}(?:\s+and\s+|{separator})(?<bareEnd>\d{{1,2}})\b(?!:\d|\s*(?:am|pm)\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (bareRangeEnd.Success && int.Parse(bareRangeEnd.Groups["bareEnd"].Value, CultureInfo.InvariantCulture) <= 12)
            throw new InvalidDataException("Clarify the clock cutoff with am/pm or HH:mm, such as 9am, 9pm or 09:00.");
        var cutoffs = Regex.Matches(request, $@"\b(?<operator>before|until|by|after|from|earlier than|later than)\s+{clock}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Cast<Match>().ToArray();
        var clocks = Regex.Matches(request, $@"\b{clock}(?=\s|[.,;!?\-–—]|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
            .Where(m => m.Groups["named"].Success || m.Groups["period"].Success || m.Groups["minute"].Success
                || int.Parse(m.Groups["hour"].Value, CultureInfo.InvariantCulture) > 12
                    && (range.Success && m.Index >= range.Index && m.Index + m.Length <= range.Index + range.Length
                        || cutoffs.Any(cutoff => m.Index >= cutoff.Index && m.Index + m.Length <= cutoff.Index + cutoff.Length))).ToArray();
        TimeOnly ReadClock(Match value)
        {
            if (value.Groups["named"].Success)
                return value.Groups["named"].Value.Equals("noon", StringComparison.OrdinalIgnoreCase) ? new TimeOnly(12, 0) : TimeOnly.MinValue;
            var hour = int.Parse(value.Groups["hour"].Value, CultureInfo.InvariantCulture);
            var minute = value.Groups["minute"].Success ? int.Parse(value.Groups["minute"].Value, CultureInfo.InvariantCulture) : 0;
            var period = value.Groups["period"].Value;
            if (minute > 59 || hour > (period.Length > 0 ? 12 : 23) || (period.Length > 0 && hour == 0))
                throw new InvalidDataException("The requested clock time is invalid.");
            if (period.Length > 0)
                hour = hour % 12 + (period.Equals("pm", StringComparison.OrdinalIgnoreCase) ? 12 : 0);
            return new TimeOnly(hour, minute);
        }
        var qualitative = !sourceOnly && PlannerInterpretation.HasAdditionalTimeHint(request);
        PlannerDailyAvailability IntersectQualitative(TimeOnly? lower, TimeOnly? upper)
        {
            var start = PlannerAvailability.ReadDailyLimit(DailyStart);
            var end = PlannerAvailability.ReadDailyLimit(DailyEnd);
            var duration = end?.ToTimeSpan() - start?.ToTimeSpan();
            if (duration is not null && duration <= TimeSpan.Zero)
                duration += TimeSpan.FromDays(1);
            if (start is null || end is null || duration >= TimeSpan.FromMinutes(1439))
                throw new InvalidDataException(NeedsExplicitTime);
            if (lower is null && upper is null)
                return this;
            if (end != TimeOnly.MinValue && start > end)
            {
                if (lower is not null && lower <= end
                    || upper == start
                    || lower is not null && upper is not null && lower > end && upper <= start && upper > lower
                    || lower is not null && upper is not null && lower > start && upper > start && upper <= lower)
                    throw new InvalidDataException("For an overnight time preference, state just the explicit start and finish times.");
                if (upper is not null && (upper <= end || upper > start))
                    end = upper;
                if (lower is not null && lower > start)
                    start = lower;
            }
            else
            {
                if (lower is not null && upper is not null && upper != TimeOnly.MinValue && lower >= upper)
                    throw new InvalidDataException("For an overnight time preference, state just the explicit start and finish times.");
                if (lower is not null && lower > start)
                    start = lower;
                if (upper is not null && (end == TimeOnly.MinValue || upper != TimeOnly.MinValue && upper < end))
                    end = upper;
                if (end != TimeOnly.MinValue && start >= end)
                    throw new InvalidDataException("The requested time preferences don't overlap. State both the start and finish times.");
            }
            return this with
            {
                DailyStart = start.Value.ToString("HH:mm", CultureInfo.InvariantCulture),
                DailyEnd = end.Value.ToString("HH:mm", CultureInfo.InvariantCulture)
            };
        }
        if (clocks.Length == 2 && range.Success)
        {
            var start = ReadClock(clocks[0]);
            var end = ReadClock(clocks[1]);
            if (start == end)
                throw new InvalidDataException("Choose different start and finish times for the requested range.");
            return qualitative ? IntersectQualitative(start, end)
                : this with { DailyStart = start.ToString("HH:mm", CultureInfo.InvariantCulture),
                    DailyEnd = end.ToString("HH:mm", CultureInfo.InvariantCulture) };
        }
        if (cutoffs.Length != clocks.Length)
            return qualitative || sourceOnly
                ? throw new InvalidDataException("Use an explicit start or finish for each clock time.")
                : this;
        if (cutoffs.Length == 0)
            return qualitative ? IntersectQualitative(null, null) : this;
        TimeOnly? lower = null;
        TimeOnly? upper = null;
        var previousExcluded = false;
        var previousEnd = 0;
        foreach (var cutoff in cutoffs)
        {
            var time = ReadClock(cutoff);
            var op = cutoff.Groups["operator"].Value.ToLowerInvariant();
            var clause = request[previousEnd..cutoff.Index];
            var excluded = Agenda.IsNegatedTimeConstraint(clause)
                || previousExcluded && Regex.IsMatch(clause, @"^[\s,]*(?:and|or)\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var finish = op is "before" or "until" or "by" or "earlier than";
            if (finish != excluded)
                upper = upper is null || upper == TimeOnly.MinValue || time != TimeOnly.MinValue && time < upper ? time : upper;
            else
                lower = lower is null || time > lower ? time : lower;
            previousExcluded = excluded;
            previousEnd = cutoff.Index + cutoff.Length;
        }
        if (qualitative)
            return IntersectQualitative(lower, upper);
        if (lower is not null && upper is not null && upper != TimeOnly.MinValue && lower >= upper)
            throw new InvalidDataException("For an overnight time preference, state the start and finish as one range.");
        var unrestrictedEdge = Regex.Matches(request, @"\b(?:any\s*time|all\s+hours|around\s+the\s+clock|including\s+overnight)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
            .Any(unrestricted => !Agenda.IsNegatedTimeConstraint(request[..unrestricted.Index])) ? "00:00" : "none";
        return this with
        {
            DailyStart = lower?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? unrestrictedEdge,
            DailyEnd = upper?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? unrestrictedEdge
        };
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
            var start = ReadDailyLimit(DailyStart) ?? TimeOnly.MinValue;
            var end = ReadDailyLimit(DailyEnd) ?? TimeOnly.MinValue;
            return SchedulePlanner.DailyAvailability(conference, zone, start, end, Zone);
        }
        if (Windows is not { Length: > 0 and <= 12 })
            throw new InvalidDataException("The model must return between 1 and 12 explicit availability windows.");
        return Windows.Select(w => w?.ToWindow(zone)
            ?? throw new InvalidDataException("The model returned an empty window.")).ToArray();
    }

    internal static TimeOnly? ReadDailyLimit(string value) => value == "none" ? null
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
                throw new InvalidDataException(SchedulePlanner.UnrecognizedTimeZoneMessage);
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
[JsonSerializable(typeof(PlannerStartingPointTopics))]
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
        {"title":"ConferenceDailyClockLimits","type":"object","properties":{"dailyStart":{"type":"string","description":"Allowed start HH:mm. Infer a start for a named period, otherwise use none if unspecified. Do not invent a morning limit from a finish time."},"dailyEnd":{"type":"string","description":"Allowed finish HH:mm. Infer a finish for a named period, otherwise use none if unspecified. The ENTIRE session must finish by this time."},"zone":{"type":"string","description":"local for an unspecified time zone; UTC if explicit; otherwise an explicit IANA zone. Never convert clock times."}},"required":["dailyStart","dailyEnd","zone"],"additionalProperties":false}
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
