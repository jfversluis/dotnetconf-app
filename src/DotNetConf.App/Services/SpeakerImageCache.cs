using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;
using System.Net;
using System.IO.Compression;
using System.Collections.Concurrent;
using DotNetConf.Core;
using Microsoft.Extensions.Logging;

namespace DotNetConf.App.Services;

/// <summary>Optional durable backing for platforms whose file system does not survive process/page restarts.</summary>
public interface ISpeakerImagePersistence
{
    byte[]? Read(string key);
    void Write(string key, byte[] content);
    void Trim(IReadOnlyCollection<string> retainedKeys);
}

public sealed class SpeakerImageCache(HttpClient http, string directory, ILogger<SpeakerImageCache> logger,
    ISpeakerImagePersistence? persistence = null)
{
    private const int MaximumImageBytes = 4 * 1024 * 1024;
    private const long MaximumCacheBytes = 64 * 1024 * 1024;
    private readonly ConcurrentDictionary<string, byte> restoredPaths = new(StringComparer.Ordinal);

    public string? FindCached(string url)
    {
        var path = GetPath(url);
        if (path is null)
            return null;
        try
        {
            if (File.Exists(path))
                return path;
            var bytes = persistence?.Read(Path.GetFileName(path));
            if (bytes is null || bytes.Length > MaximumImageBytes || !IsImage(bytes))
                return null;
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, bytes);
            // WASM does not support setting file timestamps. Restored images should still revalidate.
            restoredPaths.TryAdd(path, 0);
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not restore a saved speaker portrait.");
            return null;
        }
    }

    public async Task<string?> GetAsync(string url, CancellationToken cancellationToken)
    {
        var path = GetPath(url);
        if (path is null)
        {
            if (!string.IsNullOrWhiteSpace(url))
                logger.LogWarning("Ignored a prohibited or invalid speaker portrait URL.");
            return null;
        }
        string? temporary = null;
        try
        {
            FindCached(url);
            if (File.Exists(path) && !restoredPaths.ContainsKey(path) &&
                DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromDays(1))
                return path;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await DownloadAsync(new Uri(url), timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumImageBytes)
                throw new InvalidDataException("The speaker portrait is too large.");
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await input.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > MaximumImageBytes)
                    throw new InvalidDataException("The speaker portrait is too large.");
                await buffer.WriteAsync(chunk.AsMemory(0, read), timeout.Token);
            }
            if (!IsImage(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)))
                throw new InvalidDataException("The speaker portrait is not a supported image.");
            Directory.CreateDirectory(directory);
            temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllBytesAsync(temporary, buffer.ToArray(), timeout.Token);
            persistence?.Write(Path.GetFileName(path), buffer.ToArray());
            File.Move(temporary, path, overwrite: true);
            restoredPaths.TryRemove(path, out _);
            return path;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException or
            UnauthorizedAccessException or OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not cache a speaker portrait from {Host}.", new Uri(url).Host);
            return File.Exists(path) ? path : null;
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(exception, "Could not remove a temporary speaker portrait.");
                }
            }
        }
    }

    private async Task<HttpResponseMessage> DownloadAsync(Uri uri, CancellationToken token)
    {
        for (var redirects = 0; ; redirects++)
        {
            if (SpeakerPhotoUrl.Normalize(uri.AbsoluteUri).Length == 0)
                throw new InvalidDataException("The speaker portrait redirected to a prohibited URL.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (OperatingSystem.IsBrowser())
            {
                // Fetch cannot expose a cross-origin redirect's Location. Reject it instead of following
                // an unchecked destination (HttpClientHandler.AllowAutoRedirect is unsupported in WASM).
                request.Options.Set(new HttpRequestOptionsKey<IDictionary<string, object>>("WebAssemblyFetchOptions"),
                    new Dictionary<string, object> { ["redirect"] = "error" });
            }
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.RequestMessage?.RequestUri is { } destination &&
                SpeakerPhotoUrl.Normalize(destination.AbsoluteUri).Length == 0)
            {
                response.Dispose();
                throw new InvalidDataException("The speaker portrait response came from a prohibited URL.");
            }
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or
                HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (redirects >= 4 || location is null || !Uri.TryCreate(uri, location, out var next))
                throw new InvalidDataException("The speaker portrait has an invalid or excessive redirect chain.");
            uri = next;
        }
    }

    public void Trim(IEnumerable<string> urls)
    {
        try
        {
            var retainedKeys = urls.Select(GetPath).Where(path => path is not null)
                .Select(path => Path.GetFileName(path!)).ToHashSet(StringComparer.Ordinal);
            persistence?.Trim(retainedKeys);
            if (!Directory.Exists(directory))
                return;
            long total = 0;
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.image").OrderByDescending(file => file.LastWriteTimeUtc))
            {
                if (!retainedKeys.Contains(file.Name))
                {
                    file.Delete();
                    continue;
                }
                total += file.Length;
                if (total > MaximumCacheBytes)
                    file.Delete();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not trim the speaker portrait cache.");
        }
    }

    private string? GetPath(string url)
    {
        var approved = SpeakerPhotoUrl.Normalize(url);
        return approved.Length > 0
            ? Path.Combine(directory, $"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(approved)))}.image")
            : null;
    }

    private static bool IsImage(ReadOnlySpan<byte> bytes)
    {
        var (width, height) = Dimensions(bytes);
        return width is > 0 and <= 2048 && height is > 0 and <= 2048 && (long)width * height <= 1_048_576;
    }

    private static (int Width, int Height) Dimensions(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 14)
            return default;
        if (bytes.Length >= 45 && bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) &&
            bytes.Slice(12, 4).SequenceEqual("IHDR"u8) && bytes.Slice(bytes.Length - 8, 4).SequenceEqual("IEND"u8))
        {
            return PngDimensions(bytes);
        }
        if (bytes.Length >= 30 && bytes.StartsWith("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8) &&
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) == bytes.Length - 8)
        {
            (int Width, int Height) dimensions = default;
            var imageData = false;
            var index = 12;
            for (; index + 8 <= bytes.Length;)
            {
                var length = BinaryPrimitives.ReadInt32LittleEndian(bytes[(index + 4)..]);
                if (length < 0 || length > bytes.Length - index - 8 ||
                    bytes.Slice(index, 4).SequenceEqual("ANIM"u8) || bytes.Slice(index, 4).SequenceEqual("ANMF"u8))
                    return default;
                var type = bytes.Slice(index, 4);
                var data = bytes.Slice(index + 8, length);
                if (type.SequenceEqual("VP8X"u8))
                {
                    if (index != 12 || length != 10 || (data[0] & 0x02) != 0)
                        return default;
                    dimensions = (1 + ReadUInt24(data[4..]), 1 + ReadUInt24(data[7..]));
                }
                else if (type.SequenceEqual("VP8 "u8) || type.SequenceEqual("VP8L"u8))
                {
                    if (imageData)
                        return default;
                    imageData = true;
                    (int Width, int Height) frame;
                    if (type.SequenceEqual("VP8 "u8))
                    {
                        if (length <= 10 || (data[0] & 1) != 0 ||
                            !data.Slice(3, 3).SequenceEqual(new byte[] { 0x9D, 0x01, 0x2A }) ||
                            (ReadUInt24(data) >> 5) > length - 10)
                            return default;
                        frame = (BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) & 0x3FFF,
                            BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) & 0x3FFF);
                    }
                    else
                    {
                        if (length <= 5 || data[0] != 0x2F || (data[4] & 0xE0) != 0)
                            return default;
                        var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[1..]);
                        frame = ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
                    }
                    if (dimensions != default && dimensions != frame)
                        return default;
                    dimensions = frame;
                }
                index += length + 8 + (length & 1);
            }
            return imageData && index == bytes.Length ? dimensions : default;
        }
        if (bytes[0] != 0xFF || bytes[1] != 0xD8 || bytes[^2] != 0xFF || bytes[^1] != 0xD9)
            return default;
        (int Width, int Height) jpegDimensions = default;
        var scanned = false;
        var quantization = false;
        var huffman = false;
        for (var index = 2; index < bytes.Length;)
        {
            if (bytes[index++] != 0xFF)
                return default;
            while (index < bytes.Length && bytes[index] == 0xFF)
                index++;
            if (index >= bytes.Length)
                return default;
            var marker = bytes[index++];
            if (marker == 0xD9)
                return scanned && quantization && huffman && index == bytes.Length ? jpegDimensions : default;
            if (index + 2 > bytes.Length)
                return default;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[index..]);
            if (length < 2 || index + length > bytes.Length)
                return default;
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC) && length >= 8)
                jpegDimensions = (BinaryPrimitives.ReadUInt16BigEndian(bytes[(index + 5)..]),
                    BinaryPrimitives.ReadUInt16BigEndian(bytes[(index + 3)..]));
            quantization |= marker == 0xDB;
            huffman |= marker == 0xC4;
            index += length;
            if (marker != 0xDA)
                continue;
            if (jpegDimensions == default || length < 6)
                return default;
            var start = index;
            while (index < bytes.Length - 1)
            {
                if (bytes[index] != 0xFF)
                {
                    index++;
                    continue;
                }
                var next = bytes[index + 1];
                if (next == 0 || next is >= 0xD0 and <= 0xD7)
                    index += 2;
                else
                    break;
            }
            if (index == start)
                return default;
            scanned = true;
        }
        return default;
    }

    private static (int Width, int Height) PngDimensions(ReadOnlySpan<byte> bytes)
    {
        if (BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) != 13)
            return default;
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes[16..]);
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes[20..]);
        if (width is <= 0 or > 2048 || height is <= 0 or > 2048 || (long)width * height > 1_048_576)
            return default;
        var depth = bytes[24];
        var color = bytes[25];
        var channels = color switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        if (channels == 0 || bytes[26] != 0 || bytes[27] != 0 || bytes[28] > 1 ||
            !(color switch
            {
                0 => depth is 1 or 2 or 4 or 8 or 16,
                3 => depth is 1 or 2 or 4 or 8,
                _ => depth is 8 or 16
            }))
            return default;
        using var compressed = new MemoryStream();
        var interlaced = bytes[28] == 1;
        var palette = false;
        var dataEnded = false;
        for (var index = 8; index + 12 <= bytes.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes[index..]);
            if (length < 0 || length > bytes.Length - index - 12)
                return default;
            var type = bytes.Slice(index + 4, 4);
            var data = bytes.Slice(index + 8, length);
            if (PngCrc(bytes.Slice(index + 4, length + 4)) != BinaryPrimitives.ReadUInt32BigEndian(bytes[(index + length + 8)..]))
                return default;
            if (type.SequenceEqual("IDAT"u8))
            {
                if (dataEnded)
                    return default;
                compressed.Write(data);
            }
            else
            {
                dataEnded |= compressed.Length > 0;
                if (type.SequenceEqual("IEND"u8))
                {
                    if (length != 0 || index + 12 != bytes.Length || compressed.Length == 0 || color == 3 && !palette)
                        return default;
                    try
                    {
                        var packed = compressed.GetBuffer().AsSpan(0, (int)compressed.Length);
                        if (packed.Length < 6 || (packed[0] & 15) != 8 || (packed[0] >> 4) > 7 ||
                            ((packed[0] << 8) | packed[1]) % 31 != 0 || (packed[1] & 32) != 0)
                            return default;
                        var expectedChecksum = BinaryPrimitives.ReadUInt32BigEndian(packed[^4..]);
                        compressed.Position = 0;
                        using var decoded = new ZLibStream(compressed, CompressionMode.Decompress);
                        uint sum = 1, weighted = 0;
                        // Adam7 uses seven independently filtered passes; noninterlaced PNGs use one.
                        (int X, int Y, int Dx, int Dy)[] passes = interlaced
                            ? [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4),
                                (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)]
                            : [(0, 0, 1, 1)];
                        foreach (var pass in passes)
                        {
                            var columns = (Math.Max(0, width - pass.X) + pass.Dx - 1) / pass.Dx;
                            if (columns == 0)
                                continue;
                            var row = new byte[1 + (columns * channels * depth + 7) / 8];
                            for (var y = pass.Y; y < height; y += pass.Dy)
                            {
                                decoded.ReadExactly(row);
                                if (row[0] > 4)
                                    return default;
                                foreach (var value in row)
                                {
                                    sum = (sum + value) % 65521;
                                    weighted = (weighted + sum) % 65521;
                                }
                            }
                        }
                        return decoded.ReadByte() == -1 && (weighted << 16 | sum) == expectedChecksum
                            ? (width, height) : default;
                    }
                    catch (Exception exception) when (exception is IOException or InvalidDataException) { return default; }
                }
                if (type.SequenceEqual("IHDR"u8) && index != 8 || type.SequenceEqual("acTL"u8))
                    return default;
                if (type.SequenceEqual("PLTE"u8))
                {
                    if (dataEnded || length == 0 || length > 768 || length % 3 != 0)
                        return default;
                    palette = true;
                }
            }
            index += length + 12;
        }
        return default;
    }

    private static uint PngCrc(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u);
        }
        return ~crc;
    }
    private static int ReadUInt24(ReadOnlySpan<byte> bytes) => bytes[0] | bytes[1] << 8 | bytes[2] << 16;
}
