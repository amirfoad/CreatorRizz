using CreatorRizz.Domain;

namespace CreatorRizz.Application.Abstractions;

/// <summary>
/// Persistence port for the production workflow. Every method that changes a production takes the
/// version the caller last read, so a stale caller is rejected instead of overwriting a newer change.
/// </summary>
public interface IProductionRepository
{
    Production Create(Guid candidateId);
    bool TryGet(Guid id, out Production? production);
    void Submit(Guid id, int expectedVersion, ReviewKind kind);
    void Decide(Guid id, int expectedVersion, ReviewKind kind, ReviewOutcome outcome, string reviewerId, string? notes);
    void AttachAsset(Guid id, int expectedVersion, StoredObject stored, string type, string? sourceUrl, RightsStatus rightsStatus, string narrativePurpose, string? licenseEvidence);
    void BeginAssetPreparation(Guid id, int expectedVersion);
    void DeclareAssetsReady(Guid id, int expectedVersion);
    void CompleteRendering(Guid id, int expectedVersion);
    IReadOnlyCollection<Asset> GetAssets(Guid id);
    IReadOnlyCollection<ScriptVersion> GetScripts(Guid id);
    ScriptVersion AddScript(Guid id, int expectedVersion, string body, string claimMapJson);

    /// <summary>
    /// Stores a generated script together with the record of what generated it, in one write. The two
    /// must not be separable: a script in the review queue whose provenance was lost cannot be audited.
    /// </summary>
    ScriptVersion AddGeneratedScript(Guid id, int expectedVersion, GeneratedScript script, IReadOnlyCollection<SourceItem> inputs);

    void BeginRendering(Guid id, int expectedVersion, RenderManifest manifest);
    IReadOnlyCollection<ScriptGeneration> GetScriptGenerations(Guid id);
    IReadOnlyCollection<AuditEvent> GetAuditEvents(Guid productionId);
}
