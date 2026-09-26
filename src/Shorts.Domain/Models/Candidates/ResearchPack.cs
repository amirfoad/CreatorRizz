namespace Shorts.Domain;

public sealed class ResearchPack
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TopicCandidateId { get; init; }
    public required string Summary { get; init; }
    public required string FactsJson { get; init; }
    public required string UncertaintyJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
