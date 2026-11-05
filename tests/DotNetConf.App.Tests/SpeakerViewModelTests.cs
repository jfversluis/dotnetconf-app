using DotNetConf.App.ViewModels;
using DotNetConf.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Controls;

namespace DotNetConf.App.Tests;

public sealed class SpeakerViewModelTests
{
    [Theory]
    [InlineData("https://cdn.sessionize.com/image/photo.jpg")]
    [InlineData("https://avatars.githubusercontent.com/u/123")]
    [InlineData("https://github.com/user-attachments/assets/synthetic-photo")]
    [InlineData("https://another-feed-host.example/photo.png?size=400")]
    public void FeedPhotoUsesFrameworkLoadingAndCaching(string url)
    {
        var speaker = Create(url);

        var image = Assert.IsType<UriImageSource>(speaker.ProfilePicture);
        Assert.Equal(url, image.Uri.AbsoluteUri);
        Assert.True(image.CachingEnabled);
    }

    [Theory]
    [InlineData("http://github.com/elbruno.png", "https://github.com/elbruno.png")]
    [InlineData("http://example.com:80/photo.jpg?size=400", "https://example.com/photo.jpg?size=400")]
    [InlineData("http://example.com:8080/photo.jpg", "https://example.com:8080/photo.jpg")]
    public void HttpFeedPhotoLoadsOverHttpsWithoutChangingThePublishedModel(string url, string expected)
    {
        var speaker = Create(url);

        Assert.Equal(expected, Assert.IsType<UriImageSource>(speaker.ProfilePicture).Uri.AbsoluteUri);
        Assert.Equal(url, speaker.Model.ProfilePicture);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not a url")]
    public void MissingOrMalformedPhotoShowsTheBot(string url) =>
        AssertBot(Create(url));

    [Fact]
    public void LoadingFailureShowsTheBotWithoutRepeatedlyReplacingIt()
    {
        var speaker = Create("https://example.com/photo.jpg");

        speaker.UseFallbackPicture(new HttpRequestException("Synthetic download failure"));

        AssertBot(speaker);
        var fallback = speaker.ProfilePicture;
        speaker.UseFallbackPicture(new IOException("Synthetic decoder failure"));
        Assert.Same(fallback, speaker.ProfilePicture);
    }

    [Fact]
    public void RefreshRetriesAFailedPhotoEvenWhenTheFeedIsUnchanged()
    {
        const string url = "https://example.com/photo.jpg";
        var speaker = Create(url);
        speaker.UseFallbackPicture(new HttpRequestException("Synthetic offline failure"));

        speaker.Update(speaker.Model);

        Assert.Equal(url, Assert.IsType<UriImageSource>(speaker.ProfilePicture).Uri.AbsoluteUri);
    }

    [Fact]
    public void ProfileEditsDoNotReloadAnUnchangedPhotoButChangedOrRemovedUrlsDo()
    {
        var speaker = Create("https://example.com/photo.jpg");
        var image = speaker.ProfilePicture;

        speaker.Update(speaker.Model with { Bio = "Synthetic updated biography" });

        Assert.Same(image, speaker.ProfilePicture);
        speaker.Update(speaker.Model with { ProfilePicture = "https://example.com/updated.jpg" });
        Assert.Equal("https://example.com/updated.jpg",
            Assert.IsType<UriImageSource>(speaker.ProfilePicture).Uri.AbsoluteUri);
        speaker.Update(speaker.Model with { ProfilePicture = "" });
        AssertBot(speaker);
    }

    private static SpeakerViewModel Create(string url) => new(
        new Speaker { Id = "synthetic-speaker", ProfilePicture = url },
        new ConferenceViewModel(null!, null!, null!, null!, null!,
            NullLogger<ConferenceViewModel>.Instance, new TestReminders()));

    private static void AssertBot(SpeakerViewModel speaker) =>
        Assert.Equal("speaker_placeholder.png", Assert.IsType<FileImageSource>(speaker.ProfilePicture).File);
}
