using CreatorRizz.Domain;

namespace CreatorRizz.Application.Abstractions;

public interface IProductionRepository
{
    Production Create(Guid candidateId);
    bool TryGet(Guid id, out Production? production);
    void Submit(Guid id, ReviewKind kind);
    void Decide(Guid id, ReviewKind kind, ReviewOutcome outcome, string reviewerId, string? notes);
    void AttachAsset(Guid id, Asset asset);
    IReadOnlyCollection<Asset> GetAssets(Guid id);
    IReadOnlyCollection<ScriptVersion> GetScripts(Guid id);
    ScriptVersion AddScript(Guid id, string body, string claimMapJson);
    void BeginRendering(Guid id, RenderManifest manifest);
    IReadOnlyCollection<AuditEvent> GetAuditEvents(Guid productionId);
}
