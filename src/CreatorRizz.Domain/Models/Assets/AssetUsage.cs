namespace CreatorRizz.Domain;

public sealed class AssetUsage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProductionId { get; init; }
    public Guid AssetId { get; init; }
    public int InMilliseconds { get; init; }
    public int OutMilliseconds { get; init; }
    public required string NarrativePurpose { get; init; }
}
