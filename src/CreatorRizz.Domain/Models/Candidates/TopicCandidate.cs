namespace CreatorRizz.Domain;

public sealed class TopicCandidate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string CanonicalUrl { get; init; }
    public required string Title { get; init; }
    public string? Creator { get; init; }
    public required string Fingerprint { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public decimal ViralScore { get; set; }
    public ProductionState State { get; set; } = ProductionState.Discovered;
}
