using System.Text;
using System.Text.RegularExpressions;
using DotNetConf.Core;

namespace DotNetConf.App.Services;

public static class PlannerCatalog
{
    public const int MaximumSessions = 120;
    public const int MaximumCharacters = 9000;

    public static string[] SemanticInterests(ConferenceDocument conference, string request)
    {
        List<string> interests = [];
        List<string> pending = [];
        void Flush()
        {
            if (pending.Count > 0)
                interests.Add(string.Join(" and ", pending));
            pending.Clear();
        }
        foreach (var clause in Regex.Split(request, @"\s*(?:,|;|\band\b|\bor\b|\bplus\b)\s*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            if (Agenda.MentionedTopics(conference, clause).Length > 0
                || !new PlannerTopicRequest(clause, [], "some", "any", "independent").HasInterest(conference, clause))
                Flush();
            else
                pending.Add(clause);
        }
        Flush();
        if (interests.Count > 4)
            throw new InvalidDataException("Use at most four independent interests.");
        foreach (var interest in interests)
        {
            var subject = Regex.Match(interest, @"\b(?:everything|all\s+(?:sessions|talks))\s+(?:about|on)\s+([\p{L}\p{N}+#.]+)\s*[.!?]?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!subject.Success)
                continue;
            var name = subject.Groups[1].Value.TrimEnd('.');
            if (!conference.Sessions.Any(s => Regex.IsMatch($"{s.Title} {s.Description} {string.Join(' ', s.Topics)}",
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))))
                throw new InvalidDataException($"No agenda topic or session mentions '{name}'. A related subject is not an exact match.");
        }
        return interests.ToArray();
    }

    public static string DescribeTopics(ConferenceDocument conference)
    {
        if (conference.Sessions.Count > MaximumSessions)
            throw new InvalidDataException($"On-device planning supports at most {MaximumSessions} sessions in this preview.");
        var text = new StringBuilder("<topic-catalog>\n");
        foreach (var topic in conference.Sessions.SelectMany(s => s.Topics).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var examples = conference.Sessions.Where(s => s.Topics.Contains(topic))
                .Select(s => s.Title.ReplaceLineEndings(" ")).Distinct().Take(1);
            text.Append(topic).Append(": ").AppendJoin("; ", examples).AppendLine();
        }
        text.Append("</topic-catalog>");
        if (text.Length > MaximumCharacters)
            throw new InvalidDataException("The topic catalog is too large for this local model. No topics were silently omitted.");
        return text.ToString();
    }
}
