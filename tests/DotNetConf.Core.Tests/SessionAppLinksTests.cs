namespace DotNetConf.Core.Tests;

public sealed class SessionAppLinksTests
{
    [Theory]
    [InlineData("synthetic-session")]
    [InlineData("synthetic session")]
    [InlineData("synthetic:session+2026")]
    [InlineData("synthetic.session")]
    public void PublicIdsRoundTripAsOneEncodedSegment(string id)
    {
        Assert.Equal(id, SessionAppLinks.Parse(SessionAppLinks.Create(id)));
    }

    [Theory]
    [InlineData("https://session/synthetic")]
    [InlineData("dotnetconf://speaker/synthetic")]
    [InlineData("dotnetconf://session")]
    [InlineData("dotnetconf://session/")]
    [InlineData("dotnetconf://session//synthetic")]
    [InlineData("dotnetconf://session/first/second")]
    [InlineData("dotnetconf://session/first/../second")]
    [InlineData("dotnetconf://session/%2E%2E")]
    [InlineData("dotnetconf://session/synthetic?route=settings")]
    [InlineData("dotnetconf://session/synthetic#settings")]
    [InlineData("dotnetconf://user@session/synthetic")]
    [InlineData("dotnetconf://session:1234/synthetic")]
    [InlineData("dotnetconf://session/%2Fsettings")]
    [InlineData("dotnetconf://session/%252Fsettings")]
    [InlineData("dotnetconf://session/%00")]
    public void UntrustedRoutesAreRejected(string link)
    {
        Assert.Throws<UriFormatException>(() => SessionAppLinks.Parse(link));
    }

    [Fact]
    public void OverlongIdentifiersAreRejected()
    {
        Assert.Throws<UriFormatException>(() => SessionAppLinks.Create(new string('a', 257)));
    }
}
