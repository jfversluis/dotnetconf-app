using System.Net;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DotNetConf.App.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotNetConf.Core.Tests;

public sealed class SpeakerImageCacheTests : IDisposable
{
    private const string Url = "https://example.com/speaker.jpg";
    private readonly string directory = Path.Combine(
        Directory.GetCurrentDirectory(), ".agenda-cache-tests", Guid.NewGuid().ToString("N"));
    private readonly ImageHandler handler = new();
    private readonly HttpClient client;
    private SpeakerImageCache Cache() => new(client, directory, NullLogger<SpeakerImageCache>.Instance);
    // Generated one-pixel image, independent of any speaker photograph.
    private static byte[] Jpeg => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAb/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAABAb/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCLAXJL/9k=");
    private static byte[] Png => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=");
    private static byte[] Gif => Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
    private static byte[] Webp => Convert.FromBase64String("UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA");

    public SpeakerImageCacheTests() => client = new(handler);

    public void Dispose()
    {
        client.Dispose();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    [Theory]
    [InlineData("http://example.com/photo.jpg")]
    [InlineData("file:///portrait.jpg")]
    [InlineData("/portrait.jpg")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData("https://github.com/person.png")]
    [InlineData("https://WWW.GITHUB.COM./person.png")]
    [InlineData("https://avatars.githubusercontent.com/u/1")]
    [InlineData("https://raw.githubusercontent.com/repo/photo.jpg")]
    [InlineData("https://private-user-images.githubusercontent.com/photo.jpg")]
    [InlineData("https://github.githubassets.com/photo.png")]
    [InlineData("https://person.github.io/photo.png")]
    [InlineData("https://github-production-user-asset-6210df.s3.amazonaws.com/photo.png")]
    [InlineData("https://name:password@example.com/photo.jpg")]
    [InlineData("https://\uFFFD.com/photo.jpg")]
    public async Task UrlPolicyNeverDownloadsNonHttpsImages(string url)
    {
        var cache = Cache();
        Assert.Null(cache.FindCached(url));
        Assert.Null(await cache.GetAsync(url, CancellationToken.None));
        Assert.Equal(0, handler.Requests);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task LegacyForbiddenAndOrphanedFilesArePrunedButApprovedOfflinePhotosRemain()
    {
        var cache = Cache();
        var approved = (await cache.GetAsync(Url, CancellationToken.None))!;
        var forbiddenUrl = "https://github.com/person.png";
        var forbiddenPath = Path.Combine(directory,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(forbiddenUrl))) + ".image");
        await File.WriteAllBytesAsync(forbiddenPath, Jpeg);
        var orphan = (await cache.GetAsync("https://example.com/old-photo.jpg", CancellationToken.None))!;
        var requests = handler.Requests;
        Assert.Null(cache.FindCached(forbiddenUrl));
        Assert.Null(await cache.GetAsync(forbiddenUrl, CancellationToken.None));
        Assert.Equal(requests, handler.Requests);
        cache.Trim([Url, forbiddenUrl]);
        Assert.False(File.Exists(forbiddenPath));
        Assert.False(File.Exists(orphan));
        Assert.Equal(approved, cache.FindCached(Url));
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(approved));
    }

    [Theory]
    [InlineData("https://github.com/person.png")]
    [InlineData("https://avatars.githubusercontent.com/u/123")]
    [InlineData("http://example.com/photo.jpg")]
    [InlineData("https://name:password@example.com/photo.jpg")]
    public async Task ForbiddenRedirectIsNeverRequestedOrCached(string location)
    {
        handler.Respond = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri(location);
            return Task.FromResult(response);
        };
        Assert.Null(await Cache().GetAsync(Url, CancellationToken.None));
        Assert.Equal(1, handler.Requests);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task AllowedRelativeRedirectCachesUnderOriginalApprovedUrl()
    {
        handler.Respond = (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/speaker.jpg")
            {
                var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
                response.Headers.Location = new Uri("/new.jpg", UriKind.Relative);
                return Task.FromResult(response);
            }
            Assert.Equal("/new.jpg", request.RequestUri.AbsolutePath);
            return Task.FromResult(Ok(Jpeg));
        };
        var path = await Cache().GetAsync(Url, CancellationToken.None);
        Assert.NotNull(path);
        Assert.Equal(path, Cache().FindCached(Url));
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(path));
        Assert.Equal(2, handler.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingLocationAndRedirectLoopsFailBoundedly(bool missingLocation)
    {
        handler.Respond = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PermanentRedirect);
            if (!missingLocation)
                response.Headers.Location = new Uri("/loop.jpg", UriKind.Relative);
            return Task.FromResult(response);
        };
        Assert.Null(await Cache().GetAsync(Url, CancellationToken.None));
        Assert.Equal(missingLocation ? 1 : 5, handler.Requests);
    }

    [Fact]
    public async Task UnexpectedForbiddenResponseDestinationCannotReplaceAllowedLastGoodFile()
    {
        var cache = Cache();
        var path = (await cache.GetAsync(Url, CancellationToken.None))!;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://github.com/person.png"),
            Content = new ByteArrayContent(Png)
        });
        Assert.Equal(path, await cache.GetAsync(Url, CancellationToken.None));
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task DownloadIsCachedAcrossInstancesWithoutAnotherRequest()
    {
        var cache = Cache();
        Assert.Null(cache.FindCached(Url));
        var path = await cache.GetAsync(Url, CancellationToken.None);
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(path));
        Assert.Equal(path, cache.FindCached(Url));
        Assert.Equal(path, await cache.GetAsync(Url, CancellationToken.None));
        Assert.Equal(path, await Cache().GetAsync(Url, CancellationToken.None));
        Assert.Equal(1, handler.Requests);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task CacheKeysAreSafeAndDifferentForDifferentUrls()
    {
        var cache = Cache();
        var first = await cache.GetAsync(Url + "?size=1", CancellationToken.None);
        var second = await cache.GetAsync(Url + "?size=2", CancellationToken.None);
        Assert.NotEqual(first, second);
        Assert.Equal(Path.GetFullPath(directory), Path.GetDirectoryName(first));
        Assert.EndsWith(".image", first);
    }

    [Fact]
    public async Task ExpiryDownloadsAndAtomicallyReplacesSamePath()
    {
        var cache = Cache();
        var path = (await cache.GetAsync(Url, CancellationToken.None))!;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        var updated = Png;
        handler.Respond = (_, _) => Task.FromResult(Ok(updated));
        Assert.Equal(path, cache.FindCached(Url));
        Assert.Equal(path, await cache.GetAsync(Url, CancellationToken.None));
        Assert.Equal(updated, await File.ReadAllBytesAsync(path));
        Assert.Equal(2, handler.Requests);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(503)]
    public async Task ErrorStatusRetainsStaleFileAndReturnsNullOnFirstDownload(int status)
    {
        var cache = Cache();
        var path = (await cache.GetAsync(Url, CancellationToken.None))!;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        handler.Respond = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        Assert.Equal(path, await cache.GetAsync(Url, CancellationToken.None));
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(path));
        Assert.Null(await cache.GetAsync("https://example.com/unknown.jpg", CancellationToken.None));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task OfflineAndTimeoutRetainStaleFiles()
    {
        var cache = Cache();
        var path = (await cache.GetAsync(Url, CancellationToken.None))!;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        handler.Respond = (_, _) => throw new HttpRequestException("offline");
        Assert.Equal(path, await cache.GetAsync(Url, CancellationToken.None));
        handler.Respond = (_, _) => throw new TaskCanceledException("HTTP timeout");
        Assert.Equal(path, await cache.GetAsync(Url, CancellationToken.None));
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("webp")]
    [InlineData("webp-extended")]
    [InlineData("webp-lossless")]
    public async Task SupportedSignaturesAreCached(string format)
    {
        var bytes = format switch
        {
            "jpeg" => Jpeg,
            "png" => Png,
            "webp-extended" => AppendWebpChunk(WithDimensions("webp-extended", 1, 1), Webp[12..]),
            "webp-lossless" => Convert.FromBase64String("UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA=="),
            _ => Webp
        };
        handler.Respond = (_, _) => Task.FromResult(Ok(bytes));
        var path = await Cache().GetAsync(Url, CancellationToken.None);
        Assert.NotNull(path);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>error</html>")]
    [InlineData("RIFF0000WAVEdata")]
    [InlineData("GIF89a")]
    public async Task InvalidSignaturesDoNotReplaceLastGoodFile(string invalid)
    {
        var cache = Cache();
        var path = (await cache.GetAsync(Url, CancellationToken.None))!;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        handler.Respond = (_, _) => Task.FromResult(Ok(Encoding.UTF8.GetBytes(invalid)));
        Assert.Equal(path, await cache.GetAsync(Url, CancellationToken.None));
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(path));
        Assert.Null(await cache.GetAsync("https://example.com/new.jpg", CancellationToken.None));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("png")]
    [InlineData("gif")]
    [InlineData("jpeg")]
    [InlineData("webp")]
    public async Task TruncatedAndHeaderOnlyImagesCannotReplaceLastGoodFiles(string format)
    {
        var cache = Cache();
        var path = (await cache.GetAsync(Url, CancellationToken.None))!;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        var valid = format switch { "png" => Png, "gif" => Gif, "jpeg" => Jpeg, _ => Webp };
        foreach (var invalid in new[] { valid[..Math.Min(valid.Length, 12)], valid[..^1] })
        {
            handler.Respond = (_, _) => Task.FromResult(Ok(invalid));
            Assert.Equal(path, await cache.GetAsync(Url, CancellationToken.None));
            Assert.Equal(Jpeg, await File.ReadAllBytesAsync(path));
        }
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("png-no-idat")]
    [InlineData("png-bad-crc")]
    [InlineData("png-truncated-zlib")]
    [InlineData("png-no-pixels")]
    [InlineData("jpeg-no-scan")]
    [InlineData("jpeg-bad-scan-length")]
    [InlineData("webp-extended-no-image")]
    [InlineData("webp-frame-header-only")]
    [InlineData("webp-trailing-byte")]
    public async Task StructurallyIncompleteImagesCannotReplaceLastGoodFile(string format)
    {
        var cache = Cache();
        var path = (await cache.GetAsync(Url, CancellationToken.None))!;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        var invalid = format switch
        {
            "png-no-idat" => [.. Png[..33], .. Png[^12..]],
            "png-bad-crc" => Png,
            "png-truncated-zlib" => Png,
            "png-no-pixels" => Png,
            "jpeg-no-scan" => [.. Jpeg[..(Array.IndexOf(Jpeg, (byte)0xDA) - 1)], 0xFF, 0xD9],
            "jpeg-bad-scan-length" => Jpeg,
            "webp-extended-no-image" => WithDimensions("webp-extended", 1, 1),
            "webp-frame-header-only" => Webp[..30],
            _ => AppendWebpChunk(Webp, [0])
        };
        if (format == "png-bad-crc")
            invalid[52] ^= 1;
        if (format is "png-truncated-zlib" or "png-no-pixels")
        {
            var compressed = new MemoryStream();
            using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.SmallestSize, true))
                zlib.Write(format == "png-no-pixels" ? [] : new byte[] { 1, 255, 255 });
            var payload = compressed.ToArray();
            if (format == "png-truncated-zlib")
                payload = payload[..4];
            var chunk = new byte[payload.Length + 12];
            BinaryPrimitives.WriteInt32BigEndian(chunk, payload.Length);
            "IDAT"u8.CopyTo(chunk.AsSpan(4));
            payload.CopyTo(chunk, 8);
            var crc = uint.MaxValue;
            foreach (var value in chunk.AsSpan(4, payload.Length + 4))
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u);
            }
            BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4), ~crc);
            invalid = [.. Png[..33], .. chunk, .. Png[^12..]];
        }
        if (format == "jpeg-bad-scan-length")
        {
            var scan = Array.IndexOf(invalid, (byte)0xDA);
            invalid[scan + 1] = 0xFF;
            invalid[scan + 2] = 0xFF;
        }
        if (format == "webp-frame-header-only")
        {
            BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(4), invalid.Length - 8);
            BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(16), 10);
        }
        handler.Respond = (_, _) => Task.FromResult(Ok(invalid));
        Assert.Equal(path, await cache.GetAsync(Url, CancellationToken.None));
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(path));
        Assert.Null(await cache.GetAsync(Url + "?new", CancellationToken.None));
    }

    [Fact]
    public async Task BrowserPortraitSurvivesFreshFileSystemAndOfflineReload()
    {
        var values = new Dictionary<string, string>();
        var persistence = new DotNetConf.App.BrowserCache(values.GetValueOrDefault,
            (key, value) => values[key] = value, key => values.Remove(key),
            prefix => values.Keys.Where(key => key.StartsWith(prefix)).Select(key => key[prefix.Length..]).ToList());
        var cache = new SpeakerImageCache(client, directory, NullLogger<SpeakerImageCache>.Instance, persistence);
        var path = (await cache.GetAsync(Url, CancellationToken.None))!;
        Directory.Delete(directory, recursive: true);
        handler.Respond = (_, _) => throw new HttpRequestException("offline");
        cache = new SpeakerImageCache(client, directory, NullLogger<SpeakerImageCache>.Instance, persistence);
        Assert.Equal(path, cache.FindCached(Url));
        Assert.Equal(path, await cache.GetAsync(Url, CancellationToken.None));
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(path));
        cache.Trim([]);
        Assert.Empty(values);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task DurableStorageFailureRetainsLastGoodPortraitAndDoesNotReportFirstDownloadSaved()
    {
        var values = new Dictionary<string, string>();
        var fail = false;
        var persistence = new DotNetConf.App.BrowserCache(values.GetValueOrDefault,
            (key, value) => { if (fail) throw new IOException("quota"); values[key] = value; },
            key => values.Remove(key),
            prefix => values.Keys.Where(key => key.StartsWith(prefix)).Select(key => key[prefix.Length..]).ToList());
        var cache = new SpeakerImageCache(client, directory, NullLogger<SpeakerImageCache>.Instance, persistence);
        var path = (await cache.GetAsync(Url, CancellationToken.None))!;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        fail = true;
        handler.Respond = (_, _) => Task.FromResult(Ok(Png));
        Assert.Equal(path, await cache.GetAsync(Url, CancellationToken.None));
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(path));
        Assert.Null(await cache.GetAsync(Url + "?new", CancellationToken.None));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("png", 2049, 1)]
    [InlineData("png", 1, 2049)]
    [InlineData("png", 2048, 2048)]
    [InlineData("png", 1024, 1025)]
    [InlineData("png", 0, 1)]
    [InlineData("png", -1, 1)]
    [InlineData("jpeg", 2049, 1)]
    [InlineData("jpeg", 1, 2049)]
    [InlineData("jpeg", 2048, 2048)]
    [InlineData("webp", 2049, 1)]
    [InlineData("webp", 1, 2049)]
    [InlineData("webp", 2048, 2048)]
    [InlineData("webp-extended", 2049, 1)]
    [InlineData("webp-extended", 1, 2049)]
    [InlineData("webp-extended", 2048, 2048)]
    [InlineData("webp-lossless", 2049, 1)]
    [InlineData("webp-lossless", 1, 2049)]
    [InlineData("webp-lossless", 2048, 2048)]
    public async Task HugeDimensionsAndDecodedPixelBombsCannotReplaceLastGoodFiles(string format, int width, int height)
    {
        var cache = Cache();
        var path = (await cache.GetAsync(Url, CancellationToken.None))!;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        var invalid = WithDimensions(format, width, height);
        handler.Respond = (_, _) => Task.FromResult(Ok(invalid));
        Assert.Equal(path, await cache.GetAsync(Url, CancellationToken.None));
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(path));
        Assert.Null(await cache.GetAsync("https://example.com/new.jpg", CancellationToken.None));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("gif87")]
    [InlineData("gif89")]
    [InlineData("apng")]
    [InlineData("webp-animation-flag")]
    [InlineData("webp-animation-chunk")]
    [InlineData("webp-frame-chunk")]
    public async Task PotentiallyAnimatedImagesAreRejectedEvenWithSmallCanvas(string format)
    {
        var cache = Cache();
        var path = (await cache.GetAsync(Url, CancellationToken.None))!;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        byte[] bytes;
        switch (format)
        {
            case "gif87":
            case "gif89":
                bytes = Gif;
                if (format == "gif87")
                    bytes[4] = (byte)'7';
                break;
            case "apng":
                var animationChunk = new byte[20];
                BinaryPrimitives.WriteInt32BigEndian(animationChunk, 8);
                "acTL"u8.CopyTo(animationChunk.AsSpan(4));
                BinaryPrimitives.WriteUInt32BigEndian(animationChunk.AsSpan(8), 10000);
                var png = Png;
                bytes = [.. png[..33], .. animationChunk, .. png[33..]];
                break;
            default:
                bytes = AppendWebpChunk(WithDimensions("webp-extended", 1, 1), Webp[12..]);
                if (format == "webp-animation-flag")
                    bytes[20] |= 0x02;
                else
                {
                    var chunk = new byte[format == "webp-animation-chunk" ? 14 : 24];
                    Encoding.ASCII.GetBytes(format == "webp-animation-chunk" ? "ANIM" : "ANMF").CopyTo(chunk, 0);
                    BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(4), chunk.Length - 8);
                    bytes = AppendWebpChunk(bytes, chunk);
                }
                break;
        }
        handler.Respond = (_, _) => Task.FromResult(Ok(bytes));
        Assert.Equal(path, await cache.GetAsync(Url, CancellationToken.None));
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(path));
        Assert.Null(await cache.GetAsync("https://example.com/new.jpg", CancellationToken.None));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("png", -1)]
    [InlineData("png", int.MaxValue)]
    [InlineData("webp", -1)]
    [InlineData("webp", int.MaxValue)]
    public async Task InvalidChunkLengthsAreRejectedWithoutOverwritingCachedFile(string format, int length)
    {
        var cache = Cache();
        var path = (await cache.GetAsync(Url, CancellationToken.None))!;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        var invalid = format == "png" ? Png : Webp;
        if (format == "png")
            BinaryPrimitives.WriteInt32BigEndian(invalid.AsSpan(8), length);
        else
            BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(16), length);
        handler.Respond = (_, _) => Task.FromResult(Ok(invalid));
        Assert.Equal(path, await cache.GetAsync(Url, CancellationToken.None));
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(path));
    }

    private static byte[] AppendWebpChunk(byte[] image, byte[] chunk)
    {
        byte[] combined = [.. image, .. chunk];
        BinaryPrimitives.WriteUInt32LittleEndian(combined.AsSpan(4), (uint)combined.Length - 8);
        return combined;
    }

    private static byte[] WithDimensions(string format, int width, int height)
    {
        switch (format)
        {
            case "png":
                var png = Png;
                BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), width);
                BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), height);
                return png;
            case "gif":
                var gif = Gif;
                BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(6), (ushort)width);
                BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(8), (ushort)height);
                return gif;
            case "jpeg":
                var jpeg = Jpeg;
                for (var index = 2; index + 8 < jpeg.Length; index++)
                {
                    if (jpeg[index] != 0xFF || jpeg[index + 1] is not (>= 0xC0 and <= 0xCF) ||
                        jpeg[index + 1] is 0xC4 or 0xC8 or 0xCC)
                        continue;
                    BinaryPrimitives.WriteUInt16BigEndian(jpeg.AsSpan(index + 5), (ushort)height);
                    BinaryPrimitives.WriteUInt16BigEndian(jpeg.AsSpan(index + 7), (ushort)width);
                    return jpeg;
                }
                throw new InvalidDataException("The generated JPEG test fixture has no SOF marker.");
            case "webp-extended":
            case "webp-lossless":
                var header = new byte[30];
                "RIFF"u8.CopyTo(header);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)header.Length - 8);
                "WEBP"u8.CopyTo(header.AsSpan(8));
                if (format == "webp-extended")
                {
                    "VP8X"u8.CopyTo(header.AsSpan(12));
                    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 10);
                    WriteUInt24(header.AsSpan(24), width - 1);
                    WriteUInt24(header.AsSpan(27), height - 1);
                }
                else
                {
                    "VP8L"u8.CopyTo(header.AsSpan(12));
                    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 10);
                    header[20] = 0x2F;
                    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(21),
                        (uint)(width - 1) | ((uint)(height - 1) << 14));
                }
                return header;
            default:
                var webp = Webp;
                BinaryPrimitives.WriteUInt16LittleEndian(webp.AsSpan(26), (ushort)width);
                BinaryPrimitives.WriteUInt16LittleEndian(webp.AsSpan(28), (ushort)height);
                return webp;
        }
    }

    private static void WriteUInt24(Span<byte> destination, int value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)(value >> 16);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizedImageWithOrWithoutContentLengthRetainsOldFile(bool contentLength)
    {
        var cache = Cache();
        var path = (await cache.GetAsync(Url, CancellationToken.None))!;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        var bytes = new byte[4 * 1024 * 1024 + 1];
        Jpeg.CopyTo(bytes, 0);
        handler.Respond = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = contentLength ? new ByteArrayContent(bytes) : new StreamContent(new NonSeekableReadStream(bytes))
            };
            if (!contentLength)
                response.Content.Headers.ContentLength = null;
            return Task.FromResult(response);
        };
        Assert.Equal(path, await cache.GetAsync(Url, CancellationToken.None));
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    internal sealed class NonSeekableReadStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes, writable: false);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task CancellationDuringDownloadPropagatesWithoutReplacingOldFile()
    {
        var cache = Cache();
        var path = (await cache.GetAsync(Url, CancellationToken.None))!;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Respond = async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Ok(Jpeg);
        };
        using var cancellation = new CancellationTokenSource();
        var pending = cache.GetAsync(Url, cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(Jpeg, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task CleanupFailureIsLoggedWithoutEscapingDownloadFallback()
    {
        if (OperatingSystem.IsWindows())
            return;
        Directory.CreateDirectory(directory);
        var permissions = File.GetUnixFileMode(directory);
        var destination = Path.Combine(directory,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Url))) + ".image");
        Directory.CreateDirectory(destination);
        var logger = new CallbackLogger(() =>
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        });
        var cache = new SpeakerImageCache(client, directory, logger);
        try
        {
            Assert.Null(await cache.GetAsync(Url, CancellationToken.None));
            Assert.True(logger.Warnings >= 2);
        }
        finally { File.SetUnixFileMode(directory, permissions); }
    }

    [Fact]
    public async Task TrimKeepsNewestImagesWithinBudgetAndIgnoresUnrelatedFiles()
    {
        var cache = Cache();
        cache.Trim([]);
        Directory.CreateDirectory(directory);
        var retained = new List<string>();
        foreach (var (name, age) in new[] { ("old.image", -3), ("middle.image", -2), ("new.image", -1) })
        {
            var url = "https://example.com/" + name;
            retained.Add(url);
            var path = (await cache.GetAsync(url, CancellationToken.None))!;
            await using (var file = File.Create(path))
                file.SetLength(24 * 1024 * 1024);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(age));
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "unrelated.txt"), "Keep");
        cache.Trim(retained);
        Assert.Null(cache.FindCached(retained[0]));
        Assert.NotNull(cache.FindCached(retained[1]));
        Assert.NotNull(cache.FindCached(retained[2]));
        Assert.True(File.Exists(Path.Combine(directory, "unrelated.txt")));
    }

    private static HttpResponseMessage Ok(byte[] bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class CallbackLogger(Action warning) : ILogger<SpeakerImageCache>
    {
        public int Warnings { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings++;
                warning();
            }
        }
    }

    private sealed class ImageHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
            (_, _) => Task.FromResult(Ok(Jpeg));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Respond(request, cancellationToken);
        }
    }
}
