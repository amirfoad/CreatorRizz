namespace CreatorRizz.Domain;

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
