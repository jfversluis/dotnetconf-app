namespace DotNetConf.Core;

public interface IConferenceCacheStore
{
    Task<byte[]?> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(byte[] content, CancellationToken cancellationToken = default);
}

/// <summary>Bounded, atomic storage that never replaces the last-good file with a partial write.</summary>
public sealed class FileConferenceCacheStore(string path) : IConferenceCacheStore
{
    private const int MaximumCacheBytes = 16 * 1024 * 1024;

    public async Task<byte[]?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaximumCacheBytes)
                throw new InvalidDataException("The agenda cache exceeds the size limit.");
            using var content = new MemoryStream();
            var buffer = new byte[16384];
            int count;
            while ((count = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                if (content.Length + count > MaximumCacheBytes)
                    throw new InvalidDataException("The agenda cache exceeds the size limit.");
                await content.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }
            return content.ToArray();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public async Task WriteAsync(byte[] content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (content.Length > MaximumCacheBytes)
            throw new InvalidDataException("The agenda cache exceeds the size limit.");
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var pending = fullPath + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            await using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await file.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(pending, fullPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(pending); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
