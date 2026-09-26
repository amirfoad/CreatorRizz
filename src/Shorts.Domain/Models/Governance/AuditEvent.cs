namespace Shorts.Domain;

public sealed class AuditEvent
{
    public long Id { get; init; }
    public required string Actor { get; init; }
    public required string Action { get; init; }
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public string? PayloadJson { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
