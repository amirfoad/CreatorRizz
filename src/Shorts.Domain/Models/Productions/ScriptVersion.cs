namespace Shorts.Domain;

public sealed class ScriptVersion
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProductionId { get; init; }
    public int Version { get; init; }
    public required string Body { get; init; }
    public required string ClaimMapJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
