using System.Security.Cryptography;

namespace Shorts.Infrastructure;

public sealed record StoredObject(string ObjectKey, string Checksum, long Length);

public interface IObjectStorage
{
    Task<StoredObject> PutAsync(string objectKey, Stream content, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken);
}

public sealed class LocalObjectStorage(string rootPath) : IObjectStorage
{
    public async Task<StoredObject> PutAsync(string objectKey, Stream content, CancellationToken cancellationToken)
    {
        var path = ResolvePath(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var destination = File.Create(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            length += read;
        }
        return new StoredObject(objectKey, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), length);
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
