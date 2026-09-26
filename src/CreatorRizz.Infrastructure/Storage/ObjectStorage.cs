using System.Security.Cryptography;
using CreatorRizz.Domain;

namespace CreatorRizz.Infrastructure.Storage;

public interface IObjectStorage
{
    Task<StoredObject> PutAsync(Stream content, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken);
}

public sealed class LocalObjectStorage(string rootPath) : IObjectStorage
{
    public async Task<StoredObject> PutAsync(Stream content, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(rootPath, $".tmp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        long length = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        try
        {
            await using (var destination = File.Create(tempPath))
            {
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    length += read;
                }
            }
            var checksum = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            var objectKey = checksum;
            var path = ResolvePath(objectKey);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path))
            {
                File.Delete(tempPath);
                return new StoredObject(objectKey, checksum, new FileInfo(path).Length);
            }
            File.Move(tempPath, path);
            return new StoredObject(objectKey, checksum, length);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = File.OpenRead(ResolvePath(objectKey));
        return Task.FromResult(stream);
    }

    private string ResolvePath(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey) || Path.IsPathRooted(objectKey) || objectKey.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Object keys must be relative and cannot traverse directories.", nameof(objectKey));
        var basePath = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(Path.Combine(basePath, objectKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Object key resolves outside the storage root.", nameof(objectKey));
        return candidate;
    }
}
