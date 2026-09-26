namespace CreatorRizz.Domain;

public sealed class Production
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TopicCandidateId { get; init; }
    public ProductionState State { get; set; } = ProductionState.ScriptDraft;
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Every state change goes through here, so two reviewers working on the same gate cannot
    /// silently overwrite each other. <paramref name="expectedVersion"/> is the version the caller
    /// last read.
    /// </summary>
    public void EnsureVersion(int expectedVersion)
    {
        if (Version != expectedVersion) throw new ProductionVersionConflict(expectedVersion, Version);
    }
}
