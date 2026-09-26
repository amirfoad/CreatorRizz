namespace CreatorRizz.Domain;

public sealed class ReviewDecision
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProductionId { get; init; }
    public required string Kind { get; init; }
    public required string Decision { get; init; }
    public required string ReviewerId { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset DecidedAt { get; init; } = DateTimeOffset.UtcNow;
}
