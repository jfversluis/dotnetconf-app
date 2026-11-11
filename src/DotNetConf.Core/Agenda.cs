using System.Globalization;
using System.Text.RegularExpressions;

namespace DotNetConf.Core;

public static class Agenda
{
    private const string UnsupportedExclusion = "Excluding speakers or topics isn't supported yet. Request only the speakers and topics you want; nothing has been saved.";
    private const string PlannerExclusions = @"\b(?:except|exclud(?:e|es|ing)|avoid(?:ing)?)\b|\b(?:don['’]?t|do not|not)\s+(?!want\s+(?:to\s+)?miss\b)(?:want|include|show|suggest|recommend|see|watch|attend|like|interested)\b|\b(?:no|not|without)\s+(?:any\s+)?(?:sessions?|talks?)\b|\b(?:but|and)\s+not\b";
    private const string TemporalExclusionSubject = @"(?:(?:any\s+)?(?:sessions?|talks?)\s+)?(?:(?:if|that|which)\s+(?:(?:they\s+)?(?:are|start|end|finish)\s+)?|(?:starting|ending|finishing)\s+)?(?:(?:available|free)\s+)?";
    public static bool MatchesTopics(Session session, PlannerTopicFilter filter) => filter.MatchAll
        ? filter.Topics.All(t => session.Topics.Contains(t, StringComparer.OrdinalIgnoreCase))
        : filter.Topics.Any(t => session.Topics.Contains(t, StringComparer.OrdinalIgnoreCase));

    public static bool HasTopicContent(ConferenceDocument conference, Session session, string topic) =>
        TopicContentPattern(conference, topic).IsMatch($"{session.Title}\n{session.Description}");

    public static IEnumerable<Session> TopicContentMatches(ConferenceDocument conference, string topic)
    {
        var pattern = TopicContentPattern(conference, topic);
        return conference.Sessions.Where(s => pattern.IsMatch($"{s.Title}\n{s.Description}"));
    }

    private static Regex TopicContentPattern(ConferenceDocument conference, string topic)
    {
        var topics = conference.Sessions.SelectMany(s => s.Topics).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new($$"""(?<![\p{L}\p{N}])(?:{{TopicPattern(topic, topics)}})(?![\p{L}\p{N}])""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    public static string[] MentionedTopics(ConferenceDocument conference, string request)
    {
        var topics = conference.Sessions.SelectMany(s => s.Topics).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var matches = topics
            .Where(t => Regex.IsMatch(request,
                $$"""(?<![\p{L}\p{N}])(?:{{TopicPattern(t, topics)}})(?![\p{L}\p{N}])""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
                || FuzzyTopic(request, t)).ToArray();
        return matches.Where(t => t != ".NET" || !matches.Any(other => other.StartsWith(".NET ", StringComparison.Ordinal))).ToArray();
    }

    private static bool FuzzyTopic(string request, string topic)
    {
        var label = topic.StartsWith(".NET ", StringComparison.Ordinal) ? topic[5..] : topic;
        var name = label.ToLowerInvariant();
        if (name.Length < 4 || (name.Length == 4 && !label.All(char.IsUpper)) || name.Any(c => !char.IsLetter(c)))
            return false;
        return Regex.Matches(request.ToLowerInvariant(), @"\b\p{L}{4,}\b",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Any(match =>
        {
            var word = match.Value;
            if (name.StartsWith(word, StringComparison.Ordinal))
                return true;
            var tolerance = Math.Max(word.Length, name.Length) >= 8 ? 2 : 1;
            if (Math.Abs(word.Length - name.Length) > tolerance)
                return false;
            var previous = Enumerable.Range(0, name.Length + 1).ToArray();
            for (var i = 1; i <= word.Length; i++)
            {
                var current = new int[name.Length + 1];
                current[0] = i;
                for (var j = 1; j <= name.Length; j++)
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] + (word[i - 1] == name[j - 1] ? 0 : 1));
                previous = current;
            }
            return previous[^1] <= tolerance;
        });
    }

    private static string TopicPattern(string topic, string[] topics) => topic switch
    {
        "Mobile" => @"mobile(?:\s+(?:development|apps?))?|phone\s+apps?",
        ".NET MAUI" when !topics.Contains("Mobile", StringComparer.OrdinalIgnoreCase) =>
            @"MAUI|mobile(?:\s+(?:development|apps?))?|phone\s+apps?",
        "Performance" => @"performance|(?:code|apps?)\s+(?:run\s+)?faster",
        "Web" => @"web(?:\s+(?:development|apps?|stuff))?|browser\s+apps?",
        _ => Regex.Escape(topic.StartsWith(".NET ", StringComparison.Ordinal) ? topic[5..] : topic)
    };

    public static (string[] Speakers, string Query) GroundPlannerQuery(
        ConferenceDocument conference, string request, string[] names, string inferred)
    {
        ValidatePlannerRequest(request);
        var entities = names.Concat(conference.Speakers.SelectMany(s => s.Name.Split(' ').Append(s.Name)))
            .Concat(conference.Sessions.SelectMany(s => s.Topics)).Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities)
            if (Regex.IsMatch(request, $@"\b(?:not|no|without)\s+{Regex.Escape(entity)}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                throw new InvalidDataException(UnsupportedExclusion);
        var topics = conference.Sessions.SelectMany(s => s.Topics).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var topicNames = names.Where(n => topics.Contains(n, StringComparer.OrdinalIgnoreCase)
            && FindSpeakers(conference, n).Count == 0).ToArray();
        var fullNames = conference.Speakers.Where(s => Regex.IsMatch(request, $@"\b{Regex.Escape(s.Name)}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            .Select(s => s.Name).ToArray();
        var remainder = request;
        foreach (var name in fullNames)
            remainder = Regex.Replace(remainder, $@"\b{Regex.Escape(name)}\b", "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var aliases = conference.Speakers.SelectMany(s => s.Name.Split(' '))
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(name => Regex.IsMatch(remainder, $@"\b{Regex.Escape(name)}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
        var prefixes = names.Where(name => FindSpeakers(conference, name).Count > 0
            && Regex.IsMatch(remainder, $@"\b{Regex.Escape(name)}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
        // An extracted, literally mentioned unknown name must reach validation, regardless of phrasing.
        var unknown = names.Except(topicNames, StringComparer.OrdinalIgnoreCase)
            .Where(name => !string.IsNullOrWhiteSpace(name) && FindSpeakers(conference, name).Count == 0
                && Regex.IsMatch(request,
                    $@"(?<![\p{{L}}\p{{N}}]){string.Join(@"\s+", name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape))}(?![\p{{L}}\p{{N}}])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
        var speakers = fullNames.Concat(aliases).Concat(prefixes).Concat(unknown).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var explicitTopics = topics.Where(t => Regex.IsMatch(request,
                $$"""(?<![\p{L}\p{N}]){{Regex.Escape(t)}}(?![\p{L}\p{N}])""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            .OrderByDescending(t => t.Length).ToArray();
        // Actual catalog entity kinds and literal topics outrank inferred labels.
        var query = explicitTopics.FirstOrDefault(t => t.Equals(inferred, StringComparison.OrdinalIgnoreCase))
            ?? explicitTopics.FirstOrDefault() ?? topicNames.FirstOrDefault() ?? inferred;
        return (speakers, query);
    }

    public static void ValidatePlannerRequest(string request)
    {
        if (Regex.IsMatch(request, @"\b\d{1,2}/\d{1,2}(?:/\d{2,4})?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new InvalidDataException("Use a named month or yyyy-MM-dd for dates so they aren't ambiguous.");
        if (Regex.IsMatch(request,
            @"\b(?:nothing|no|not)\s+(?:too\s+)?(?:advanced|basic|complicated|beginner|intermediate)\b|" +
            @"\b(?:beginner|intermediate|advanced)[ -](?:friendly|level)\b|" +
            @"\b(?:beginner|intermediate|advanced|basic)\s+(?:[\p{L}+#.-]+\s+){0,3}(?:sessions?|talks?|workshops?)\b|" +
            @"\b(?:sessions?|talks?|workshops?)\s+(?:(?:suitable|appropriate|designed)\s+)?for\s+(?:complete\s+)?beginners?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new InvalidDataException("The agenda doesn't label session difficulty. Try a topic or speaker instead.");
        const string clock = @"(?:\d{1,2}(?::\d{2})?\s*(?:am|pm)?|noon|midnight)";
        var ranges = Regex.Matches(request, $@"\b(?:between|from)\s+{clock}(?:\s+(?:and|to|until|through)\s+|\s*[-–—]\s*){clock}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (ranges.Any(range => IsNegatedTimeConstraint(request[..range.Index])))
            throw new InvalidDataException("Excluding a time range isn't supported yet. Request the hours you can watch.");
        var exclusions = Regex.Matches(request, PlannerExclusions,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        foreach (Match exclusion in exclusions)
        {
            // A clock cutoff is already supported by availability validation, not a negative entity filter.
            var temporal = Regex.IsMatch(request[(exclusion.Index + exclusion.Length)..],
                $@"^\s+{TemporalExclusionSubject}(?:after|before|outside|later\s+than|earlier\s+than)\s+(?:\d|noon\b|midnight\b)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!temporal)
                throw new InvalidDataException(UnsupportedExclusion);
        }
    }

    public static bool IsNegatedTimeConstraint(string beforeOperator)
    {
        var clause = Regex.Split(beforeOperator,
            @"[;!?]|\.(?:\s|$)|\b(?:but|however|although|yet)\b|\bcan(?:not|['’]t)\s+wait\s+(?:to|for)\b|" +
            @"(?:\b(?:and|or|so|then)\s+|,\s*)(?=(?:I|we|you|they)\b|(?:please\s+)?(?:show|find|list|give|suggest|recommend)\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))[^1];
        return Regex.Matches(clause, PlannerExclusions + @"|\b(?:nothing|no|not|without)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
        .Any(exclusion => Regex.IsMatch(clause[(exclusion.Index + exclusion.Length)..],
            $@"^\s+{TemporalExclusionSubject}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        || Regex.IsMatch(clause,
            @"\b(?:can(?:not|['’]t)|can\s+not|won['’]t|will\s+not|not\s+able\s+to|unable(?:\s+to)?|unavailable)\b(?:[\s,]+[\p{L}\p{N}+#.-]+)*[\s,]*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    public static IReadOnlyList<Speaker> FindSpeakers(ConferenceDocument conference, string? name)
    {
        var terms = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
            return [];
        var normalized = string.Join(' ', terms);
        var exact = conference.Speakers.Where(s =>
            s.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length > 0)
            return exact;
        return conference.Speakers.Where(s => terms.All(term =>
            s.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part.StartsWith(term, StringComparison.OrdinalIgnoreCase)))).ToArray();
    }

    public static IEnumerable<Session> Search(
        ConferenceDocument conference, string? query, DateOnly? localDay,
        IReadOnlyCollection<string>? topics, TimeZoneInfo timeZone)
    {
        var terms = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var speakers = conference.Speakers.ToDictionary(speaker => speaker.Id, speaker => speaker.Name);
        return conference.Sessions
            .Where(session => localDay is null || LocalDay(session.StartsAt, timeZone) == localDay)
            .Where(session => topics is null || topics.Count == 0
                || session.Topics.Any(topic => topics.Contains(topic, StringComparer.OrdinalIgnoreCase)))
            .Where(session =>
            {
                var text = string.Join(' ', session.Title, session.Description,
                    string.Join(' ', session.Topics), string.Join(' ', session.SpeakerIds.Select(id => speakers[id])));
                return terms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(session => session.StartsAt)
            .ThenBy(session => session.Title, StringComparer.Ordinal);
    }

    public static DateOnly LocalDay(DateTimeOffset instant, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);

    /// <summary>
    /// A short month and day in the reader's culture, in its own order and form: "Nov 10" in English,
    /// "10 de nov" in Spanish, "11月10日" in Japanese.
    /// </summary>
    public static string MonthDay(DateOnly day, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return day.ToString(culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM"), culture);
    }

    /// <summary>
    /// A short duration such as "27 days", "2 h 15 min" or "40 min". It rounds up to the minute, so a countdown never
    /// shows less time than is left.
    /// </summary>
    public static string Duration(TimeSpan span)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(span.TotalMinutes));
        var (days, hours) = (minutes / (24 * 60), minutes / 60);
        if (days >= 2)
            return $"{days} days";
        if (days == 1)
            return hours % 24 == 0 ? "1 day" : $"1 day {hours % 24} h";
        if (hours >= 1)
            return minutes % 60 == 0 ? $"{hours} h" : $"{hours} h {minutes % 60} min";
        return $"{minutes} min";
    }

    /// <summary>
    /// The stretches of time in which at least two of the sessions run at once, merged where they touch.
    /// </summary>
    public static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> OverlapRanges(IEnumerable<Session> sessions)
    {
        var ranges = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        DateTimeOffset? reach = null;
        foreach (var session in sessions.OrderBy(session => session.StartsAt))
        {
            if (reach is { } end && session.StartsAt < end)
            {
                var overlapEnd = session.EndsAt < end ? session.EndsAt : end;
                if (ranges.Count > 0 && session.StartsAt <= ranges[^1].End)
                    ranges[^1] = (ranges[^1].Start, overlapEnd > ranges[^1].End ? overlapEnd : ranges[^1].End);
                else
                    ranges.Add((session.StartsAt, overlapEnd));
            }
            reach = reach is { } current && current > session.EndsAt ? current : session.EndsAt;
        }
        return ranges;
    }

    public static bool Overlaps(Session first, Session second) =>
        first.Id != second.Id && first.StartsAt < second.EndsAt && second.StartsAt < first.EndsAt;

    public static bool IsInProgress(Session session, DateTimeOffset instant) =>
        session.StartsAt <= instant && instant < session.EndsAt;
}
