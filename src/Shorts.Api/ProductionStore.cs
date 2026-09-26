using System.Collections.Concurrent;
using Shorts.Domain;

namespace Shorts.Api;

public sealed class ProductionStore
{
    private readonly ConcurrentDictionary<Guid, Production> _productions = new();
    private readonly ConcurrentQueue<ReviewDecision> _decisions = new();
    private readonly ConcurrentDictionary<Guid, List<Asset>> _assets = new();
    private readonly ConcurrentDictionary<Guid, List<ScriptVersion>> _scripts = new();
    private readonly ConcurrentQueue<AuditEvent> _auditEvents = new();

    public Production Create(Guid candidateId)
    {
        var production = new Production { TopicCandidateId = candidateId };
        if (!_productions.TryAdd(production.Id, production)) throw new InvalidOperationException("Could not create production.");
        AddAudit("system", "ProductionCreated", "Production", production.Id.ToString());
        return production;
    }

    public bool TryGet(Guid id, out Production? production) => _productions.TryGetValue(id, out production);

    public IReadOnlyCollection<ReviewDecision> GetDecisions(Guid productionId) =>
        _decisions.Where(x => x.ProductionId == productionId).ToArray();

    public void Submit(Guid id, ReviewKind kind)
    {
        var production = GetRequired(id);
        lock (production)
        {
            production.State = ProductionWorkflow.SubmitForReview(production.State, kind);
            production.Version++;
            AddAudit("system", "ReviewSubmitted", "Production", id.ToString(), kind.ToString());
        }
    }

    public void Decide(Guid id, ReviewKind kind, ReviewOutcome outcome, string reviewerId, string? notes)
    {
        var production = GetRequired(id);
        lock (production)
        {
            production.State = ProductionWorkflow.Decide(production.State, kind, outcome);
            production.Version++;
            _decisions.Enqueue(new ReviewDecision
            {
                ProductionId = id,
                Kind = kind.ToString(),
                Decision = outcome.ToString(),
                ReviewerId = reviewerId,
                Notes = notes
            });
            AddAudit(reviewerId, "ReviewDecided", "Production", id.ToString(), $"{kind}:{outcome}");
        }
    }

    public void AttachAsset(Guid id, Asset asset)
    {
        var production = GetRequired(id);
        if (!AssetRightsPolicy.CanAttach(asset.RightsStatus)) throw new WorkflowRuleViolation($"Asset status {asset.RightsStatus} cannot be attached.");
        var assets = _assets.GetOrAdd(id, _ => []);
        lock (production)
        {
            lock (assets) assets.Add(asset);
            production.State = ProductionWorkflow.InvalidateRightsForAssetChange(production.State);
            production.Version++;
            AddAudit("system", "AssetAttached", "Production", id.ToString(), asset.RightsStatus.ToString());
        }
    }

    public IReadOnlyCollection<Asset> GetAssets(Guid id) => _assets.TryGetValue(id, out var assets) ? assets.ToArray() : [];
    public IReadOnlyCollection<ScriptVersion> GetScripts(Guid id) => _scripts.TryGetValue(id, out var scripts) ? scripts.OrderBy(x => x.Version).ToArray() : [];

    public ScriptVersion AddScript(Guid id, string body, string claimMapJson)
    {
        var production = GetRequired(id);
        ResearchPolicy.EnsureScriptHasClaimMap(body, claimMapJson);
        var scripts = _scripts.GetOrAdd(id, _ => []);
        lock (production)
        {
            var script = new ScriptVersion { ProductionId = id, Version = scripts.Count + 1, Body = body, ClaimMapJson = claimMapJson };
            lock (scripts) scripts.Add(script);
            production.State = ProductionState.ScriptDraft;
            production.Version++;
            AddAudit("system", "ScriptVersionCreated", "Production", id.ToString(), script.Version.ToString());
            return script;
        }
    }

    public void BeginRendering(Guid id, RenderManifest manifest)
    {
        var production = GetRequired(id);
        if (manifest.ProductionId != id) throw new WorkflowRuleViolation("Render manifest must reference the production being rendered.");
        RenderManifestValidator.Validate(manifest);
        lock (production)
        {
            var assets = GetAssets(id);
            if (assets.Count == 0) throw new WorkflowRuleViolation("At least one approved asset is required before rendering.");
            AssetRightsPolicy.EnsureCanRender(assets.Select(asset => asset.RightsStatus), hasExplicitRightsApproval: true);
            production.State = ProductionWorkflow.BeginRendering(production.State);
            production.Version++;
            AddAudit("system", "RenderQueued", "Production", id.ToString());
        }
    }
    public IReadOnlyCollection<AuditEvent> GetAuditEvents(Guid productionId) => _auditEvents.Where(x => x.EntityId == productionId.ToString()).ToArray();

    private void AddAudit(string actor, string action, string entityType, string entityId, string? payload = null) =>
        _auditEvents.Enqueue(new AuditEvent { Actor = actor, Action = action, EntityType = entityType, EntityId = entityId, PayloadJson = payload });

    private Production GetRequired(Guid id) => TryGet(id, out var production) && production is not null
        ? production
        : throw new KeyNotFoundException("Production was not found.");
}
