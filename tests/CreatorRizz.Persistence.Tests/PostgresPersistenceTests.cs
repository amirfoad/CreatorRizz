using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using CreatorRizz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CreatorRizz.Persistence.Tests;

/// <summary>
/// Each mutation runs on its own <see cref="CreatorRizzDbContext"/>, the way a scoped context behaves
/// per HTTP request. Sharing one context across steps would hide real concurrency behaviour behind a
/// change tracker that never goes stale.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresPersistenceTests(PostgresFixture fixture)
{
    private static readonly ViralSignals StrongSignals = new(90, 80, 70, 60, 50, 40);

    [Fact]
    public async Task MigrationsBuildEveryWorkflowTableOnAnEmptyDatabase()
    {
        await using var database = fixture.CreateContext();
        var tables = (await database.Database
            .SqlQueryRaw<string>("select table_name from information_schema.tables where table_schema = 'public'")
            .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("topic_candidates", tables);
        Assert.Contains("source_items", tables);
        Assert.Contains("research_packs", tables);
        Assert.Contains("productions", tables);
        Assert.Contains("script_versions", tables);
        Assert.Contains("assets", tables);
        Assert.Contains("asset_usages", tables);
        Assert.Contains("review_decisions", tables);
        Assert.Contains("audit_events", tables);
    }

    [Fact]
    public void TheSameCanonicalUrlCannotBeRegisteredTwice()
    {
        var url = $"https://example.com/story-{Guid.NewGuid():N}";
        var first = AddCandidate(url);
        Assert.NotEqual(Guid.Empty, first.Id);

        var failure = Assert.Throws<InvalidOperationException>(() => AddCandidate(url));
        Assert.Contains("IX_topic_candidates_canonical_url", failure.Message);
        Assert.Single(CountCandidates(url));
    }

    [Fact]
    public void ANewScriptVersionKeepsEarlierVersionsAndTheAuditTrail()
    {
        var production = CreateProductionWithScript();

        AddScriptVersion(production, "Second draft body.");
        AddScriptVersion(production, "Third draft body.");

        var scripts = GetScripts(production);
        Assert.Equal([1, 2, 3], scripts.Select(script => script.Version));
        Assert.Equal(["First draft body.", "Second draft body.", "Third draft body."], scripts.Select(script => script.Body));

        var actions = GetAuditActions(production);
        Assert.Equal(["ProductionCreated", "ScriptVersionCreated", "ScriptVersionCreated", "ScriptVersionCreated"], actions);
    }

    [Fact]
    public void AttachingAnAssetAfterRightsApprovalReturnsTheProductionToAssetsReady()
    {
        var production = CreateProductionWithApprovedRights();

        AttachLicensedAsset(production, "hook");

        Assert.Equal(ProductionState.AssetsReady, GetState(production));
        Assert.Contains("AssetAttached", GetAuditActions(production));
    }

    [Fact]
    public void AssetUsageTimingStaysUnknownUntilARenderManifestArrives()
    {
        var production = CreateProductionWithApprovedRights();
        var asset = GetAssets(production).Single();

        Assert.Null(GetUsageTiming(production, asset.Id).InMilliseconds);

        BeginRendering(production, asset, 400, 3200);

        var usage = GetUsageTiming(production, asset.Id);
        Assert.Equal(400, usage.InMilliseconds);
        Assert.Equal(3200, usage.OutMilliseconds);
        Assert.Equal(ProductionState.Rendering, GetState(production));
    }

    [Fact]
    public void AProductionReachesPublishApprovalOnlyThroughAllThreeHumanGates()
    {
        var production = CreateProductionWithApprovedRights();
        var asset = GetAssets(production).Single();

        BeginRendering(production, asset, 0, 3000);
        Assert.Throws<WorkflowRuleViolation>(() => SubmitReview(production, ReviewKind.Publish));

        CompleteRendering(production);
        SubmitReview(production, ReviewKind.Publish);
        DecideReview(production, ReviewKind.Publish, ReviewOutcome.Approve, "reviewer-3");

        Assert.Equal(ProductionState.PublishApproved, GetState(production));
        PublishingPolicy.EnsureCanUpload(GetState(production), GetAssets(production).Select(item => item.RightsStatus).ToArray());
        Assert.Equal(3, GetAuditEvents(production).Count(entry => entry.Action == "ReviewDecided"));
    }

    [Fact]
    public void AttachingAnAssetAfterRenderInvalidatesRightsAndBlocksPublishing()
    {
        var production = CreateProductionWithApprovedRights();
        var asset = GetAssets(production).Single();
        BeginRendering(production, asset, 0, 3000);
        CompleteRendering(production);
        SubmitReview(production, ReviewKind.Publish);
        DecideReview(production, ReviewKind.Publish, ReviewOutcome.Approve, "reviewer-3");

        AttachLicensedAsset(production, "evidence");

        Assert.Equal(ProductionState.AssetsReady, GetState(production));
        Assert.Throws<WorkflowRuleViolation>(() =>
            PublishingPolicy.EnsureCanUpload(GetState(production), GetAssets(production).Select(item => item.RightsStatus).ToArray()));
    }

    [Fact]
    public void AStaleReviewerIsRejectedBeforeAnythingIsWritten()
    {
        var production = CreateProductionWithScript();
        var versionTheReviewerRead = GetVersion(production);
        SubmitReview(production, ReviewKind.Script);

        var conflict = Assert.Throws<ProductionVersionConflict>(() =>
            DecideReview(production, ReviewKind.Script, ReviewOutcome.Reject, "reviewer-2", versionTheReviewerRead));

        Assert.Equal(versionTheReviewerRead, conflict.ExpectedVersion);
        Assert.Equal(versionTheReviewerRead + 1, conflict.CurrentVersion);
        Assert.Equal(ProductionState.ScriptInReview, GetState(production));
        Assert.DoesNotContain(GetAuditEvents(production), entry => entry.Actor == "reviewer-2");
    }

    [Fact]
    public async Task TwoWritersThatBothReadTheSameVersionProduceOneWinner()
    {
        var production = CreateProductionWithScript();
        SubmitReview(production, ReviewKind.Script);
        var contestedVersion = GetVersion(production);

        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var firstWriter = new PostgresProductionRepository(firstContext);
        var secondWriter = new PostgresProductionRepository(secondContext);

        // Both reviewers read the same version before either writes, so neither pre-check can see the
        // other. Only the database can settle this one.
        Assert.Equal(contestedVersion, firstContext.Productions.Find(production)!.Version);
        Assert.Equal(contestedVersion, secondContext.Productions.Find(production)!.Version);

        firstWriter.Decide(production, contestedVersion, ReviewKind.Script, ReviewOutcome.Approve, "reviewer-1", null);
        var conflict = Assert.Throws<ProductionVersionConflict>(() =>
            secondWriter.Decide(production, contestedVersion, ReviewKind.Script, ReviewOutcome.Approve, "reviewer-3", null));

        Assert.Equal(contestedVersion + 1, conflict.CurrentVersion);
        Assert.Equal(ProductionState.ScriptApproved, GetState(production));
        Assert.Equal(1, GetAuditEvents(production).Count(entry => entry.Action == "ReviewDecided"));
    }

    [Fact]
    public async Task AssetUsageCannotExistWithoutAProductionAndAnAsset()
    {
        await using var database = fixture.CreateContext();
        var asset = new Asset { ObjectKey = $"orphan/{Guid.NewGuid():N}.mp4", Type = "video", RightsStatus = RightsStatus.Owned };
        database.Assets.Add(asset);
        database.AssetUsages.Add(new AssetUsage
        {
            ProductionId = Guid.NewGuid(),
            AssetId = asset.Id,
            NarrativePurpose = "orphan"
        });

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
        Assert.IsType<PostgresException>(failure.InnerException);
    }

    [Fact]
    public void AnAttachedAssetNeedsANarrativePurpose()
    {
        var production = CreateProductionWithScript();

        Assert.Throws<ArgumentException>(() => AttachAsset(production,
            new Asset { ObjectKey = "assets/one.mp4", Type = "video", RightsStatus = RightsStatus.Owned }, "   "));
        Assert.Empty(GetAssets(production));
    }

    [Fact]
    public void AnUnknownRightsStatusNeverReachesTheProduction()
    {
        var production = CreateProductionWithScript();

        var failure = Assert.Throws<WorkflowRuleViolation>(() => AttachAsset(production,
            new Asset { ObjectKey = "assets/unknown.mp4", Type = "video", RightsStatus = RightsStatus.Unknown }, "hook"));

        Assert.Contains("Unknown", failure.Message);
        Assert.Empty(GetAssets(production));
    }

    private Guid CreateProductionWithScript()
    {
        using var context = fixture.CreateContext();
        var production = new PostgresProductionRepository(context)
            .Create(AddCandidate($"https://example.com/script-{Guid.NewGuid():N}").Id);
        AddScriptVersion(production.Id, "First draft body.");
        return production.Id;
    }

    private Guid CreateProductionWithApprovedRights()
    {
        var production = CreateProductionWithScript();
        SubmitReview(production, ReviewKind.Script);
        DecideReview(production, ReviewKind.Script, ReviewOutcome.Approve, "reviewer-1");
        BeginAssetPreparation(production);
        AttachLicensedAsset(production, "hook");
        DeclareAssetsReady(production);
        SubmitReview(production, ReviewKind.Rights);
        DecideReview(production, ReviewKind.Rights, ReviewOutcome.Approve, "reviewer-2");
        Assert.Equal(ProductionState.RightsApproved, GetState(production));
        return production;
    }

    private TopicCandidate AddCandidate(string url)
    {
        using var context = fixture.CreateContext();
        return new PostgresCandidateRepository(context)
            .Add(url, "A verified story", "Creator", DateTimeOffset.UtcNow, StrongSignals);
    }

    private void AddScriptVersion(Guid production, string body) =>
        Mutate(repository => repository.AddScript(production, GetVersion(production), body,
            $"{{\"claim\":\"source-{CountScripts(production) + 1}\"}}"));

    private Asset AttachLicensedAsset(Guid production, string purpose) =>
        AttachAsset(production, new Asset
        {
            ObjectKey = $"assets/{Guid.NewGuid():N}.mp4",
            Type = "video",
            RightsStatus = RightsStatus.Licensed
        }, purpose);

    private Asset AttachAsset(Guid production, Asset asset, string purpose)
    {
        Mutate(repository => repository.AttachAsset(production, GetVersion(production), asset, purpose));
        return asset;
    }

    private void SubmitReview(Guid production, ReviewKind kind) =>
        Mutate(repository => repository.Submit(production, GetVersion(production), kind));

    private void DecideReview(Guid production, ReviewKind kind, ReviewOutcome outcome, string reviewerId, int? versionToUse = null) =>
        Mutate(repository => repository.Decide(production, versionToUse ?? GetVersion(production), kind, outcome, reviewerId, null));

    private void BeginAssetPreparation(Guid production) =>
        Mutate(repository => repository.BeginAssetPreparation(production, GetVersion(production)));

    private void DeclareAssetsReady(Guid production) =>
        Mutate(repository => repository.DeclareAssetsReady(production, GetVersion(production)));

    private void CompleteRendering(Guid production) =>
        Mutate(repository => repository.CompleteRendering(production, GetVersion(production)));

    private void BeginRendering(Guid production, Asset asset, int inMilliseconds, int outMilliseconds) =>
        Mutate(repository => repository.BeginRendering(production, GetVersion(production), new RenderManifest(production, 1080, 1920,
            [new TimelineClip(asset.Id, inMilliseconds, outMilliseconds, 0)], [], "voice/one.mp3")));

    private void Mutate(Action<PostgresProductionRepository> change)
    {
        using var context = fixture.CreateContext();
        change(new PostgresProductionRepository(context));
    }

    private int GetVersion(Guid production)
    {
        using var context = fixture.CreateContext();
        return context.Productions.AsNoTracking().Single(item => item.Id == production).Version;
    }

    private ProductionState GetState(Guid production)
    {
        using var context = fixture.CreateContext();
        return context.Productions.AsNoTracking().Single(item => item.Id == production).State;
    }

    private Guid[] CountCandidates(string url)
    {
        using var context = fixture.CreateContext();
        return context.TopicCandidates.Where(candidate => candidate.CanonicalUrl == url).Select(candidate => candidate.Id).ToArray();
    }

    private IReadOnlyCollection<ScriptVersion> GetScripts(Guid production)
    {
        using var context = fixture.CreateContext();
        return new PostgresProductionRepository(context).GetScripts(production);
    }

    private int CountScripts(Guid production)
    {
        using var context = fixture.CreateContext();
        return new PostgresProductionRepository(context).GetScripts(production).Count;
    }

    private IReadOnlyCollection<Asset> GetAssets(Guid production)
    {
        using var context = fixture.CreateContext();
        return new PostgresProductionRepository(context).GetAssets(production);
    }

    private IReadOnlyCollection<AuditEvent> GetAuditEvents(Guid production)
    {
        using var context = fixture.CreateContext();
        return new PostgresProductionRepository(context).GetAuditEvents(production);
    }

    private IReadOnlyCollection<string> GetAuditActions(Guid production) => GetAuditEvents(production)
        .Select(entry => entry.Action)
        .ToArray();

    private AssetUsage GetUsageTiming(Guid production, Guid assetId)
    {
        using var context = fixture.CreateContext();
        return context.AssetUsages.AsNoTracking().Single(usage => usage.ProductionId == production && usage.AssetId == assetId);
    }
}
