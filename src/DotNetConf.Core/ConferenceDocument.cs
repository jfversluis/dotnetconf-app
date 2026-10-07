namespace DotNetConf.Core;

public sealed record ConferenceDocument
{
    public int SchemaVersion { get; init; }
    public bool IsMock { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string WebsiteUrl { get; init; } = "";
    public List<Speaker> Speakers { get; init; } = [];
    public List<Session> Sessions { get; init; } = [];
}

public sealed record Speaker
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Tagline { get; init; } = "";
    public string Bio { get; init; } = "";
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
