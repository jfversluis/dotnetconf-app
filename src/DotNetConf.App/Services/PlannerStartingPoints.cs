using System.Text.Json;
using DotNetConf.Core;

namespace DotNetConf.App.Services;

public sealed record PlannerStartingPoint(string Label, string Request);
internal sealed record PlannerStartingPointTopics(string[] Topics);

public static class PlannerStartingPoints
{
    public static IReadOnlyList<PlannerStartingPoint> Ground(ConferenceDocument conference, string[]? topics)
    {
        if (topics is null || topics.Length is < 3 or > 5
            || topics.Distinct(StringComparer.OrdinalIgnoreCase).Count() != topics.Length)
            throw new InvalidDataException("The model must suggest three to five different agenda topics.");
        var catalog = conference.Sessions.SelectMany(s => s.Topics).ToHashSet(StringComparer.Ordinal);
        if (topics.Any(topic => !catalog.Contains(topic)))
            throw new InvalidDataException("A suggested topic is not in the agenda.");
        string[] invitations = ["Make me dangerous with {0}", "{0}, hold the boring bits", "Feed my {0} curiosity", "More {0}, less FOMO", "Down the {0} rabbit hole"];
        var offset = Random.Shared.Next(invitations.Length);
        List<PlannerStartingPoint> suggestions = topics.Select((topic, index) =>
        {
            var invitation = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                invitations[(offset + index) % invitations.Length], topic);
            return new PlannerStartingPoint(invitation, $"I'm interested in {topic}.");
        }).ToList();
        var scheduledIds = conference.Sessions.SelectMany(session => session.SpeakerIds).ToHashSet(StringComparer.Ordinal);
        var speakers = conference.Speakers.Where(speaker => scheduledIds.Contains(speaker.Id)
            && !string.IsNullOrWhiteSpace(speaker.Name))
            .GroupBy(speaker => speaker.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1).Select(group => group.Single()).ToArray();
        if (speakers.Length > 0)
        {
            var speaker = speakers[Random.Shared.Next(speakers.Length)];
            suggestions.Insert(1, new($"Team {speaker.Name}", $"I want to see all sessions by {speaker.Name}."));
        }
        return suggestions;
    }

    public static JsonElement Schema(ConferenceDocument conference, string[]? candidates = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WriteStartObject("properties");
            writer.WriteStartObject("topics");
            writer.WriteString("type", "array");
            writer.WriteNumber("minItems", 3);
            writer.WriteNumber("maxItems", 5);
            writer.WriteStartObject("items");
            writer.WriteString("type", "string");
            writer.WriteStartArray("enum");
            foreach (var topic in candidates ?? conference.Sessions.SelectMany(s => s.Topics).Distinct().ToArray())
                writer.WriteStringValue(topic);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteStartArray("required");
            writer.WriteStringValue("topics");
            writer.WriteEndArray();
            writer.WriteBoolean("additionalProperties", false);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }
}
