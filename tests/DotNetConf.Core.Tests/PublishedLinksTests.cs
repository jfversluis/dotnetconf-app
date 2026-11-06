using System.Text;
using System.Text.Json;
using DotNetConf.Core;
using static DotNetConf.Core.Tests.RemoteAgendaTests;

namespace DotNetConf.Core.Tests;

// All recording URLs and link labels in these tests are synthetic.
public sealed class PublishedLinksTests
{
    private static JsonElement Value(string value) =>
        JsonSerializer.SerializeToElement(value, ConferenceJsonContext.Default.String);

    private static JsonElement Value(string[] value) =>
        JsonSerializer.SerializeToElement(value, ConferenceJsonContext.Default.StringArray);

    private static async Task<ConferenceDocument> Read(MicrosoftAgenda data, ConferenceDocument? previous = null)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Json(data)));
        return await MicrosoftAgendaJson.ReadAsync(stream, previous ?? Previous);
    }

    [Fact]
    public async Task PublishedRecordingsResourcesAndSocialsRoundTripThroughTheValidatedCacheContract()
    {
        var source = RemoteAgendaTests.Microsoft;
        source = source with
        {
            Sessions = [source.Sessions[0] with
            {
                YoutubeLink = Value("https://www.youtube.com/watch?v=synthetic01"),
                SessionLinks = Value(new[] { "[Synthetic resource](https://example.com/resource)", "https://example.com/other" })
            }],
            Speakers = [source.Speakers[0] with
            {
                PrimarySocials = Value("[Synthetic profile](https://example.com/profile)"),
                AdditionalSocials = Value("https://example.com/other-profile")
            }]
        };
        var document = await Read(source);
        using var cache = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(document, ConferenceJsonContext.Default.ConferenceDocument));
        var restored = await ConferenceJson.ReadAsync(cache);
        Assert.Equal("https://www.youtube.com/watch?v=synthetic01", Assert.Single(restored.Sessions).RecordingUrl);
        Assert.Equal("Synthetic resource", restored.Sessions[0].ResourceLinks[0].Title);
        Assert.Equal(2, restored.Speakers[0].SocialLinks.Count);
    }

    [Fact]
    public async Task MissingOrUnpublishedLinksRemovePreviouslyPublishedValuesWithoutDroppingBiography()
    {
        var source = RemoteAgendaTests.Microsoft;
        var first = await Read(source with
        {
            Sessions = [source.Sessions[0] with { YoutubeLink = Value("https://youtu.be/synthetic01"), SessionLinks = Value("https://example.com/resource") }],
            Speakers = [source.Speakers[0] with { PrimarySocials = Value("https://example.com/profile") }]
        });
        var next = await Read(source with
        {
            Sessions = [source.Sessions[0] with { YoutubeLink = Value("*No response*"), SessionLinks = Value("*No response*") }]
        }, first);
        Assert.Empty(next.Sessions[0].RecordingUrl);
        Assert.Empty(next.Sessions[0].ResourceLinks);
        Assert.Empty(next.Speakers[0].SocialLinks);
        Assert.Equal(first.Speakers[0].Bio, next.Speakers[0].Bio);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://youtube.com.evil.example/watch?v=synthetic01")]
    [InlineData("https://www.youtube.com/redirect?q=https://example.com")]
    [InlineData("https://user:password@www.youtube.com/watch?v=synthetic01")]
    [InlineData("http://www.youtube.com/watch?v=synthetic01")]
    public async Task InvalidRecordingIsRejected(string url)
    {
        var source = RemoteAgendaTests.Microsoft;
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(source with
        {
            Sessions = [source.Sessions[0] with { YoutubeLink = Value(url) }]
        }));
    }

    [Theory]
    [InlineData("https://youtu.be/synthetic01")]
    [InlineData("https://www.youtube.com/watch?v=synthetic01&t=12")]
    [InlineData("https://www.youtube.com/live/synthetic01")]
    public void SupportedYouTubeVideoUrlsAreRecognized(string url) => Assert.True(ConferenceLinks.IsYouTubeUrl(url));

    [Theory]
    [InlineData("[Synthetic](javascript:alert(1))")]
    [InlineData("[Synthetic](http://example.com)")]
    [InlineData("https://user:password@example.com/")]
    public async Task InvalidResourcesAreRejected(string text)
    {
        var source = RemoteAgendaTests.Microsoft;
        await Assert.ThrowsAsync<InvalidDataException>(() => Read(source with
        {
            Sessions = [source.Sessions[0] with { SessionLinks = Value(text) }]
        }));
    }

    [Fact]
    public void SyntheticIdsNeverBecomeInventedOfficialSessionPages()
    {
        Assert.Equal(ConferenceLinks.Website, ConferenceLinks.SessionUrl("synthetic-session"));
        Assert.Equal("https://dotnet.microsoft.com/en-us/conf/2026/agenda/brk006", ConferenceLinks.SessionUrl("BRK006"));
    }
}
