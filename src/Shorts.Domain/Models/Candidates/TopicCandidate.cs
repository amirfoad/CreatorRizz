namespace Shorts.Domain;

public sealed class TopicCandidate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string CanonicalUrl { get; init; }
    public required string Title { get; init; }
    public string? Creator { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
    public decimal ViralScore { get; set; }
    public ProductionState State { get; set; } = ProductionState.Discovered;
}
