namespace DotNetConf.Core;

public sealed record ConferenceDocument
{
    public int SchemaVersion { get; init; }
    public bool IsMock { get; init; }
    public bool CommunityEventsAreMock { get; init; } = true;
    public string AgendaNotice { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string WebsiteUrl { get; init; } = "";
    public List<Speaker> Speakers { get; init; } = [];
    /// <summary>Profiles retained only to resolve withdrawn saved talks, not current directory entries.</summary>
    public List<Speaker> WithdrawnSpeakers { get; init; } = [];
    public List<Session> Sessions { get; init; } = [];
    /// <summary>Last-known withdrawn talks for resolving saved IDs; these are not part of the active agenda.</summary>
    public List<Session> WithdrawnSessions { get; init; } = [];
    public List<CommunityEvent> CommunityEvents { get; init; } = [];
}

public sealed record CommunityEvent
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Venue { get; init; } = "";
    public string City { get; init; } = "";
    public string Country { get; init; } = "";
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public string WebsiteUrl { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string LocationLabel => $"{Venue}, {City}, {Country}";
    [System.Text.Json.Serialization.JsonIgnore]
    public string KindLabel => Kind == "watch-party" ? "Watch party" : "In-person talks";
    [System.Text.Json.Serialization.JsonIgnore]
    public string KindDescription => Kind == "watch-party"
        ? "Watch the .NET Conf broadcast together with your local community."
        : "Local presenters share the latest .NET Conf materials in person.";
    [System.Text.Json.Serialization.JsonIgnore]
    public string AccessibilityLabel => $"{Name}. {KindLabel}. {LocationLabel}.";
}

public sealed record Speaker
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Tagline { get; init; } = "";
    public string Bio { get; init; } = "";
    public string ProfilePicture { get; init; } = "";
}

public sealed record Session
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public DateTimeOffset StartsAt { get; init; }
    public DateTimeOffset EndsAt { get; init; }
    public List<string> SpeakerIds { get; init; } = [];
    public List<string> Topics { get; init; } = [];
}

public interface IConferenceDataSource
{
    Task<ConferenceDocument> LoadAsync(CancellationToken cancellationToken = default);
}
