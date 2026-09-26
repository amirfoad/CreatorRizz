namespace CreatorRizz.Domain;

public sealed class Production
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TopicCandidateId { get; init; }
    public ProductionState State { get; set; } = ProductionState.ScriptDraft;
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
