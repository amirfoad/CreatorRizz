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

public sealed class ResearchPack
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TopicCandidateId { get; init; }
    public required string Summary { get; init; }
    public required string FactsJson { get; init; }
    public required string UncertaintyJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class Production
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TopicCandidateId { get; init; }
    public ProductionState State { get; set; } = ProductionState.ScriptDraft;
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class ScriptVersion
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProductionId { get; init; }
    public int Version { get; init; }
    public required string Body { get; init; }
    public required string ClaimMapJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class Asset
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string ObjectKey { get; init; }
    public required string Type { get; init; }
    public string? SourceUrl { get; init; }
    public required RightsStatus RightsStatus { get; set; }
    public string? LicenseEvidence { get; set; }
    public string? Checksum { get; init; }
}

public sealed class AssetUsage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProductionId { get; init; }
    public Guid AssetId { get; init; }
    public int InMilliseconds { get; init; }
    public int OutMilliseconds { get; init; }
    public required string NarrativePurpose { get; init; }
}

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
