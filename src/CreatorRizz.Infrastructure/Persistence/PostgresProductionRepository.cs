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

    /// <summary>
    /// Review filtering goes through <see cref="ProductionWorkflow.ReviewAwaitingDecision"/> so the
    /// queue cannot drift from the transitions that put a production into a waiting state.
    /// </summary>
    public ProductionPage List(ProductionQuery query)
    {
        var matching = database.Productions.AsNoTracking().AsQueryable();
        if (query.State is { } state) matching = matching.Where(production => production.State == state);
        if (query.AwaitingReview is { } review)
        {
            var waiting = new[] { ProductionState.ScriptInReview, ProductionState.RightsReview, ProductionState.PublishReview }
                .Where(candidate => ProductionWorkflow.ReviewAwaitingDecision(candidate) == review)
                .ToArray();
            matching = matching.Where(production => waiting.Contains(production.State));
        }

        // Newest first: the operator is looking for what to work on now, and a review queue that shows
        // the oldest stuck item first hides the reason it is stuck.
        var page = matching
            .OrderByDescending(production => production.CreatedAt)
            .ThenByDescending(production => production.Id)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToArray();
        return new ProductionPage(page, matching.Count(), query.Skip, query.Take);
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

    public void AttachAsset(Guid id, int expectedVersion, StoredObject stored, string type, string? sourceUrl, RightsStatus rightsStatus, string narrativePurpose, string? licenseEvidence)
    {
        var production = GetRequired(id, expectedVersion);
        if (!AssetRightsPolicy.CanAttach(rightsStatus)) throw new WorkflowRuleViolation($"Asset status {rightsStatus} cannot be attached.");
        var existing = database.Assets.AsNoTracking().FirstOrDefault(asset => asset.Checksum == stored.Checksum);
        var objectKey = existing?.ObjectKey ?? stored.ObjectKey;
        // A duplicate of the same content already exists; reuse the stored key and do
        // not write the file bytes a second time. The new Asset still records its own
        // usage but shares the immutable object storage.
        var asset = existing ?? new Asset
        {
            ObjectKey = objectKey,
            Type = type,
            SourceUrl = sourceUrl,
            RightsStatus = rightsStatus,
            LicenseEvidence = licenseEvidence,
            Checksum = stored.Checksum
        };
        if (existing is null)
        {
            database.Assets.Add(asset);
        }
        database.AssetUsages.Add(new AssetUsage
        {
            ProductionId = id,
            AssetId = asset.Id,
            NarrativePurpose = AssetUsagePolicy.NormalizePurpose(narrativePurpose)
        });
        production.State = ProductionWorkflow.InvalidateRightsForAssetChange(production.State);
        production.Version++;
        AddAudit("system", "AssetAttached", "Production", id, rightsStatus.ToString());
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
        var script = AddScriptVersion(id, body, claimMapJson);
        production.State = ProductionState.ScriptDraft;
        production.Version++;
        AddAudit("system", "ScriptVersionCreated", "Production", id, script.Version.ToString());
        SaveVersion(id, expectedVersion);
        return script;
    }

    public ScriptVersion AddGeneratedScript(Guid id, int expectedVersion, GeneratedScript script, IReadOnlyCollection<SourceItem> inputs)
    {
        var production = GetRequired(id, expectedVersion);
        ArgumentNullException.ThrowIfNull(script);
        if (string.IsNullOrWhiteSpace(script.ModelId)) throw new ArgumentException("A generated script must record the model that wrote it.", nameof(script));
        if (string.IsNullOrWhiteSpace(script.PromptVersion)) throw new ArgumentException("A generated script must record the prompt version.", nameof(script));
        ResearchPolicy.EnsureScriptHasClaimMap(script.Body, script.ClaimMapJson);

        var references = inputs.Select(source => new { source.Id, source.Url, source.Publisher, source.ReliabilityScore }).ToArray();
        var stored = AddScriptVersion(id, script.Body, script.ClaimMapJson);
        database.ScriptGenerations.Add(new ScriptGeneration
        {
            ProductionId = id,
            ModelId = script.ModelId,
            PromptVersion = script.PromptVersion,
            InputReferencesJson = JsonSerializer.Serialize(references),
            Body = script.Body,
            ClaimMapJson = script.ClaimMapJson
        });
        production.State = ProductionState.ScriptDraft;
        production.Version++;
        AddAudit("system", "ScriptGenerated", "Production", id, $"{script.ModelId}@{script.PromptVersion}");
        SaveVersion(id, expectedVersion);
        return stored;
    }

    private ScriptVersion AddScriptVersion(Guid id, string body, string claimMapJson)
    {
        var nextVersion = (database.ScriptVersions
            .Where(script => script.ProductionId == id)
            .Select(script => (int?)script.Version)
            .Max() ?? 0) + 1;
        var script = new ScriptVersion { ProductionId = id, Version = nextVersion, Body = body, ClaimMapJson = claimMapJson };
        database.ScriptVersions.Add(script);
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

    public IReadOnlyCollection<ScriptGeneration> GetScriptGenerations(Guid id) => database.ScriptGenerations
        .AsNoTracking()
        .Where(generation => generation.ProductionId == id)
        .OrderBy(generation => generation.CreatedAt)
        .ToArray();

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
