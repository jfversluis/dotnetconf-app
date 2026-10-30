using DotNetConf.Core;

namespace DotNetConf.Core.Tests;

public sealed class SpeakerPhotoUrlTests
{
    [Theory]
    [InlineData("https://github.com/person.png")]
    [InlineData("http://github.com/person.png")]
    [InlineData("https://WWW.GitHub.com./person.png")]
    [InlineData("https://github\u3002com/photo.png")]
    [InlineData("https://\uFFFD.com/photo.png")]
    [InlineData("https://\u200C.com/photo.png")]
    [InlineData("https://avatars.githubusercontent.com/u/123")]
    [InlineData("https://raw.githubusercontent.com/repo/photo.jpg")]
    [InlineData("https://private-user-images.githubusercontent.com/photo.jpg")]
    [InlineData("https://github.githubassets.com/photo.png")]
    [InlineData("https://person.github.io/photo.png")]
    [InlineData("https://github-production-user-asset-6210df.s3.amazonaws.com/photo.png")]
    [InlineData("https://name:password@example.com/photo.png")]
    [InlineData("http://example.com/photo.png")]
    [InlineData("file:///photo.png")]
    [InlineData("data:image/png;base64,test")]
    [InlineData("photo.png")]
    [InlineData("")]
    [InlineData(null)]
    public void ForbiddenHostsCredentialsAndNonHttpsUrlsAreRejected(string? url) =>
        Assert.Empty(SpeakerPhotoUrl.Normalize(url));

    [Theory]
    [InlineData("https://cdn.sessionize.com/image/avatar.jpg")]
    [InlineData("https://www.dotnetconf.net/img/speakers/avatar.png")]
    [InlineData("https://github.com.example.com/avatar.jpg")]
    [InlineData("https://example.com/github.com/avatar.jpg")]
    [InlineData("https://example.com/user-attachments/avatar.jpg")]
    public void PolicyUsesActualHostNotPathOrSubstring(string url) =>
        Assert.Equal(url, SpeakerPhotoUrl.Normalize(url));
}
