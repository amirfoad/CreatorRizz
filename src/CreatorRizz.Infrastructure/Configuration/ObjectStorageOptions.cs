namespace CreatorRizz.Infrastructure.Configuration;

/// <summary>
/// Where stored objects live. This is configuration rather than a constant because the API writes the
/// files and a separate worker process has to read them back: two processes with two hard-coded roots
/// means the worker can never open anything the API stored.
/// </summary>
public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    /// <summary>
    /// Filesystem root, absolute or relative to the running process. A relative path is deliberate: the
    /// API and the worker each run from the repository root locally and then resolve to the same
    /// <c>storage</c> folder. Empty is not allowed, because two processes each falling back to their own
    /// base directory is exactly the split this setting exists to prevent.
    /// </summary>
    public string RootPath { get; init; } = Path.Combine("storage", "objects");
}
