namespace Shorts.Domain;

public sealed class SourceItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TopicCandidateId { get; init; }
    public required string Url { get; init; }
    public required string Publisher { get; init; }
    public string? Excerpt { get; init; }
    public int ReliabilityScore { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
}
