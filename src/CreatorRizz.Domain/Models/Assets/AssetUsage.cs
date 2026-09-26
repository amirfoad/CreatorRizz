namespace CreatorRizz.Domain;

/// <summary>
/// Links an asset to the production that uses it. Source timing stays unknown until a render
/// manifest places the asset on the timeline.
/// </summary>
public sealed class AssetUsage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProductionId { get; init; }
    public Guid AssetId { get; init; }
    public int? InMilliseconds { get; set; }
    public int? OutMilliseconds { get; set; }
    public required string NarrativePurpose { get; init; }
}
