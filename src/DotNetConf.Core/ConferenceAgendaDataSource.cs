namespace DotNetConf.Core;

/// <summary>Planner and reminders read the same validated live/cache snapshot as the agenda UI.</summary>
public sealed class ConferenceAgendaDataSource(IConferenceRepository repository) : IConferenceDataSource
{
    public async Task<ConferenceDocument> LoadAsync(CancellationToken cancellationToken = default) =>
        (await repository.LoadAsync(cancellationToken)).Document;
}
