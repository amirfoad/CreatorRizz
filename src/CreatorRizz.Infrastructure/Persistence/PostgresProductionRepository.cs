using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CreatorRizz.Infrastructure.Persistence;

/// <summary>PostgreSQL implementation of the production, review and asset persistence port.</summary>
public sealed class PostgresProductionRepository(CreatorRizzDbContext database) : IProductionRepository
{
    public Production Create(Guid candidateId)
    {
        if (database.TopicCandidates.Find(candidateId) is null) throw new KeyNotFoundException("Candidate was not found.");
        var production = new Production { TopicCandidateId = candidateId };
        database.Productions.Add(production);
        AddAudit("system", "ProductionCreated", "Production", production.Id);
        database.SaveWorkflowChanges();
        return production;
    }

    public bool TryGet(Guid id, out Production? production)
    {
        production = database.Productions.Find(id);
        return production is not null;
    }

    public void Submit(Guid id, int expectedVersion, ReviewKind kind)
    {
        var production = GetRequired(id, expectedVersion);
        production.State = ProductionWorkflow.SubmitForReview(production.State, kind);
        production.Version++;
        AddAudit("system", "ReviewSubmitted", "Production", id, kind.ToString());
        SaveVersion(id, expectedVersion);
    }

    public void Decide(Guid id, int expectedVersion, ReviewKind kind, ReviewOutcome outcome, string reviewerId, string? notes)
    {
        if (string.IsNullOrWhiteSpace(reviewerId)) throw new ArgumentException("A review decision requires a reviewer identity.", nameof(reviewerId));
        var production = GetRequired(id, expectedVersion);
        production.State = ProductionWorkflow.Decide(production.State, kind, outcome);
        production.Version++;
        database.ReviewDecisions.Add(new ReviewDecision
        {
            ProductionId = id,
            Kind = kind.ToString(),
            Decision = outcome.ToString(),
            ReviewerId = reviewerId,
            Notes = notes
        });
        AddAudit(reviewerId, "ReviewDecided", "Production", id, $"{kind}:{outcome}");
        SaveVersion(id, expectedVersion);
    }

    public void AttachAsset(Guid id, int expectedVersion, Asset asset, string narrativePurpose)
    {
        var production = GetRequired(id, expectedVersion);
        if (!AssetRightsPolicy.CanAttach(asset.RightsStatus)) throw new WorkflowRuleViolation($"Asset status {asset.RightsStatus} cannot be attached.");
        database.Assets.Add(asset);
        database.AssetUsages.Add(new AssetUsage
        {
            ProductionId = id,
            AssetId = asset.Id,
            NarrativePurpose = AssetUsagePolicy.NormalizePurpose(narrativePurpose)
        });
        production.State = ProductionWorkflow.InvalidateRightsForAssetChange(production.State);
        production.Version++;
        AddAudit("system", "AssetAttached", "Production", id, asset.RightsStatus.ToString());
        SaveVersion(id, expectedVersion);
    }

    public void BeginAssetPreparation(Guid id, int expectedVersion)
    {
        var production = GetRequired(id, expectedVersion);
        production.State = ProductionWorkflow.BeginAssetPreparation(production.State);
        production.Version++;
        AddAudit("system", "AssetPreparationStarted", "Production", id);
        SaveVersion(id, expectedVersion);
    }

    public void DeclareAssetsReady(Guid id, int expectedVersion)
    {
        var production = GetRequired(id, expectedVersion);
        if (database.AssetUsages.AsNoTracking().Count(usage => usage.ProductionId == id) == 0)
            throw new WorkflowRuleViolation("At least one approved asset is required before the rights review can start.");
        production.State = ProductionWorkflow.DeclareAssetsReady(production.State);
        production.Version++;
        AddAudit("system", "AssetsDeclaredReady", "Production", id);
        SaveVersion(id, expectedVersion);
    }

    public void CompleteRendering(Guid id, int expectedVersion)
    {
        var production = GetRequired(id, expectedVersion);
        production.State = ProductionWorkflow.CompleteRendering(production.State);
        production.Version++;
        AddAudit("system", "RenderCompleted", "Production", id);
        SaveVersion(id, expectedVersion);
    }

    public IReadOnlyCollection<Asset> GetAssets(Guid id) => database.AssetUsages
        .AsNoTracking()
        .Where(usage => usage.ProductionId == id)
        .Join(database.Assets.AsNoTracking(), usage => usage.AssetId, asset => asset.Id, (_, asset) => asset)
        .ToArray();

    public IReadOnlyCollection<ScriptVersion> GetScripts(Guid id) => database.ScriptVersions
        .AsNoTracking()
        .Where(script => script.ProductionId == id)
        .OrderBy(script => script.Version)
        .ToArray();

    public ScriptVersion AddScript(Guid id, int expectedVersion, string body, string claimMapJson)
    {
        var production = GetRequired(id, expectedVersion);
        ResearchPolicy.EnsureScriptHasClaimMap(body, claimMapJson);
        var nextVersion = (database.ScriptVersions
            .Where(script => script.ProductionId == id)
            .Select(script => (int?)script.Version)
            .Max() ?? 0) + 1;
        var script = new ScriptVersion { ProductionId = id, Version = nextVersion, Body = body, ClaimMapJson = claimMapJson };
        database.ScriptVersions.Add(script);
        production.State = ProductionState.ScriptDraft;
        production.Version++;
        AddAudit("system", "ScriptVersionCreated", "Production", id, script.Version.ToString());
        SaveVersion(id, expectedVersion);
        return script;
    }

    public void BeginRendering(Guid id, int expectedVersion, RenderManifest manifest)
    {
        var production = GetRequired(id, expectedVersion);
        if (manifest.ProductionId != id) throw new WorkflowRuleViolation("Render manifest must reference the production being rendered.");
        RenderManifestValidator.Validate(manifest);

        var usages = database.AssetUsages.Where(usage => usage.ProductionId == id).ToArray();
        if (usages.Length == 0) throw new WorkflowRuleViolation("At least one approved asset is required before rendering.");
        AssetRightsPolicy.EnsureCanRender(LoadRightsStatuses(usages), hasExplicitRightsApproval: true);

        foreach (var usage in usages)
        {
            var clip = manifest.Clips.FirstOrDefault(item => item.AssetId == usage.AssetId);
            if (clip is null) continue;
            AssetUsagePolicy.EnsureTimingIsOrdered(clip.StartMilliseconds, clip.EndMilliseconds);
            usage.InMilliseconds = clip.StartMilliseconds;
            usage.OutMilliseconds = clip.EndMilliseconds;
        }

        production.State = ProductionWorkflow.BeginRendering(production.State);
        production.Version++;
        AddAudit("system", "RenderQueued", "Production", id);
        SaveVersion(id, expectedVersion);
    }

    public IReadOnlyCollection<AuditEvent> GetAuditEvents(Guid productionId) => database.AuditEvents
        .AsNoTracking()
        .Where(entry => entry.EntityType == "Production" && entry.EntityId == productionId.ToString())
        .OrderBy(entry => entry.OccurredAt)
        .ToArray();

    private List<RightsStatus> LoadRightsStatuses(IReadOnlyCollection<AssetUsage> usages)
    {
        var assetIds = usages.Select(usage => usage.AssetId).ToArray();
        return database.Assets.AsNoTracking()
            .Where(asset => assetIds.Contains(asset.Id))
            .Select(asset => asset.RightsStatus)
            .ToList();
    }

    private void AddAudit(string actor, string action, string entityType, Guid entityId, string? payload = null) =>
        database.AuditEvents.Add(new AuditEvent
        {
            Actor = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            PayloadJson = payload is null ? null : JsonSerializer.Serialize(payload)
        });

    private Production GetRequired(Guid id, int expectedVersion)
    {
        if (!TryGet(id, out var production) || production is null) throw new KeyNotFoundException("Production was not found.");
        production.EnsureVersion(expectedVersion);
        return production;
    }

    /// <summary>
    /// The version guard above catches a caller that is already stale. This catches the interleaving
    /// where two callers both read the same version and then race to write, which only the database
    /// can arbitrate.
    /// </summary>
    private void SaveVersion(Guid id, int expectedVersion)
    {
        try
        {
            database.SaveWorkflowChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            var currentVersion = database.Productions
                .AsNoTracking()
                .Where(production => production.Id == id)
                .Select(production => (int?)production.Version)
                .Max() ?? expectedVersion;
            throw new ProductionVersionConflict(expectedVersion, currentVersion);
        }
    }
}
