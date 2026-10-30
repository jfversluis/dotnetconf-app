using DotNetConf.Core;

namespace DotNetConf.App.Services;

public sealed class ConferenceMetadataDataSource : IConferenceDataSource
{
    public async Task<ConferenceDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var metadata = await FileSystem.OpenAppPackageFileAsync("conference.json");
        return await ConferenceJson.ReadAsync(metadata, cancellationToken);
    }
}
