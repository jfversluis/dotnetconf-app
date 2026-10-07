using DotNetConf.Core;

namespace DotNetConf.App.Services;

public sealed class BundledConferenceDataSource : IConferenceDataSource
{
    public async Task<ConferenceDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync("conference.mock.json");
        return await ConferenceJson.ReadAsync(stream, cancellationToken);
    }
}
