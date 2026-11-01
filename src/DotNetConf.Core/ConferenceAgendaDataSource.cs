namespace DotNetConf.Core;

/// <summary>Planner and reminders read the same validated live/cache snapshot as the agenda UI.</summary>
public sealed class ConferenceAgendaDataSource(IConferenceRepository repository) : IConferenceDataSource
{
    public async Task<ConferenceDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await repository.LoadAsync(cancellationToken);
        if (snapshot.Document.Sessions.Count == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Cancelling an optional planner wait must not cancel the shared agenda download.
            snapshot = await repository.RefreshAsync().WaitAsync(cancellationToken);
        }
        return snapshot.Document;
    }
}
