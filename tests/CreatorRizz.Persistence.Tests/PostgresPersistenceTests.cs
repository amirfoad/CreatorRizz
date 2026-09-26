using CreatorRizz.Application;
using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using CreatorRizz.Infrastructure.Jobs;
using CreatorRizz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;
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
        Assert.Contains("job_outbox", tables);
    }

    [Fact]
    public async Task ARenderThatCannotBeRecordedLeavesTheProductionOutOfRendering()
    {
        var production = CreateProductionWithApprovedRights();
        var manifest = new RenderManifest(production, 1080, 1920,
            [new TimelineClip(GetAssets(production).Single().Id, 0, 3000, 0)], [], "voice/one.mp3");

        await using (var context = fixture.CreateContext())
        {
            var workflow = WorkflowUsing(context, new UnrecordableJobQueue());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                workflow.QueueRenderAsync(production, GetVersion(production), manifest, CancellationToken.None).AsTask());
        }

        // Before the outbox this was the failure mode: the production sat in Rendering waiting for a
        // render that had never been recorded anywhere.
        Assert.Equal(ProductionState.RightsApproved, GetState(production));
        Assert.Equal(0, CountOutboxEntries(production));
    }

    [Fact]
    public async Task QueuingARenderRecordsTheWorkAlongsideTheStateChange()
    {
        var production = CreateProductionWithApprovedRights();
        var manifest = new RenderManifest(production, 1080, 1920,
            [new TimelineClip(GetAssets(production).Single().Id, 0, 3000, 0)], [], "voice/one.mp3");
        var versionReadByCaller = GetVersion(production);

        await using (var context = fixture.CreateContext())
            await WorkflowUsing(context, new PostgresProductionJobQueue(context))
                .QueueRenderAsync(production, versionReadByCaller, manifest, CancellationToken.None);

        Assert.Equal(ProductionState.Rendering, GetState(production));

        var entry = ReadOutboxEntry(production);
        Assert.Equal(ProductionJobKind.Render, entry.Kind);
        Assert.Equal(production, entry.ProductionId);
        Assert.Equal(ProductionJobKey.ForRender(production, versionReadByCaller), entry.IdempotencyKey);
        Assert.Equal(production, JsonSerializer.Deserialize<RenderManifest>(entry.PayloadJson)!.ProductionId);
    }

    [Fact]
    public async Task RecordingTheSameRenderTwiceIsOnePieceOfWork()
    {
        var production = CreateProductionWithApprovedRights();
        var manifest = new RenderManifest(production, 1080, 1920,
            [new TimelineClip(GetAssets(production).Single().Id, 0, 3000, 0)], [], "voice/one.mp3");
        var key = ProductionJobKey.ForRender(production, GetVersion(production));

        await using (var context = fixture.CreateContext())
        {
            var jobs = new PostgresProductionJobQueue(context);
            await jobs.EnqueueRenderAsync(manifest, key, CancellationToken.None);
            await jobs.EnqueueRenderAsync(manifest, key, CancellationToken.None);
        }

        Assert.Equal(1, CountOutboxEntries(production));
    }

    /// <summary>
    /// Shares one context across the transaction, the production repository and the queue, which is what
    /// the composition root does at runtime and what makes the two writes one commit.
    /// </summary>
    private CreatorRizzWorkflow WorkflowUsing(CreatorRizzDbContext context, IProductionJobQueue jobs) => new(
        new PostgresCandidateRepository(context, ViralScoreWeights.Version1),
        new PostgresProductionRepository(context),
        jobs,
        new UnusableScriptGenerator(),
        new PostgresWorkflowTransaction(context),
        ViralScoreWeights.Version1);

    // The fixture shares one database across the whole collection, so every outbox assertion is scoped to
    // its own production rather than counting the table.
    private int CountOutboxEntries(Guid production)
    {
        using var context = fixture.CreateContext();
        return context.JobOutbox.Count(entry => entry.ProductionId == production);
    }

    private JobOutboxEntry ReadOutboxEntry(Guid production)
    {
        using var context = fixture.CreateContext();
        return context.JobOutbox.AsNoTracking().Single(entry => entry.ProductionId == production);
    }

    /// <summary>Queuing a render must never reach for the model, so this fails loudly if it does.</summary>
    private sealed class UnusableScriptGenerator : IScriptGenerator
    {
        public ValueTask<GeneratedScript> GenerateAsync(ScriptGenerationRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Queuing a render must not generate a script.");
    }

    private sealed class UnrecordableJobQueue : IProductionJobQueue
    {
        public ValueTask EnqueueTextToSpeechAsync(TextToSpeechJob job, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The queue could not record the work.");

        public ValueTask EnqueueRenderAsync(RenderManifest manifest, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The queue could not record the work.");
    }

    [Fact]
    public void RegisteringTheSameStoryByHandIsRefusedAndPointsAtTheStoredCandidate()
    {
        var url = $"https://example.com/story-{Guid.NewGuid():N}";
        var title = $"A verified story {Guid.NewGuid():N}";
        var signals = StrongSignals;

        using (var context = fixture.CreateContext())
            new PostgresCandidateRepository(context, ViralScoreWeights.Version1).Add(url, title, "Creator", DateTimeOffset.UtcNow, signals);

        using var again = fixture.CreateContext();
        var failure = Assert.Throws<InvalidOperationException>(() =>
            new PostgresCandidateRepository(again, ViralScoreWeights.Version1).Add(url, title, "Creator", DateTimeOffset.UtcNow, signals));

        Assert.Contains("already registered", failure.Message);
        Assert.Contains("Discovery registers repeats instead of failing", failure.Message);
        Assert.Single(CountCandidates(url));
    }

    [Fact]
    public void DiscoveryRegistersARepeatedStoryOnlyOnce()
    {
        var topic = new DiscoveredTopic(
            "Meteor Hits Coastal Town",
            $"https://feed.example.com/meteor-{Guid.NewGuid():N}",
            "Example News",
            DateTimeOffset.UtcNow,
            new ViralSignals(0m, 0m, 90m, 0m, 0m, 0m));

        var first = RegisterDiscovered(topic);
        var repeat = RegisterDiscovered(topic);
        var sameStoryAnotherUrl = RegisterDiscovered(topic with
        {
            CanonicalUrl = $"https://other.example.com/meteor-{Guid.NewGuid():N}",
            Title = "  meteor   hits coastal TOWN  "
        });

        Assert.Equal(first.Id, repeat.Id);
        Assert.Equal(first.Id, sameStoryAnotherUrl.Id);
        Assert.Equal(1, CountCandidatesWithFingerprint(first.Fingerprint));
    }

    [Fact]
    public void DiscoveryScoresARecencyOnlySignalWithoutInventingEngagement()
    {
        var topic = new DiscoveredTopic(
            "An Old Story",
            $"https://feed.example.com/old-{Guid.NewGuid():N}",
            "Example News",
            DateTimeOffset.UtcNow.AddDays(-15),
            new ViralSignals(0m, 0m, 50m, 0m, 0m, 0m));

        var candidate = RegisterDiscovered(topic);

        Assert.Equal(7.5m, candidate.ViralScore);
    }

    [Fact]
    public void AGeneratedScriptIsStoredWithTheModelAndSourcesThatProducedIt()
    {
        var production = CreateProductionWithScript();
        AddVerifiedSources(production);
        var sources = GetSourcesFor(production);
        Assert.Equal(2, sources.Count);
        var generated = new GeneratedScript(
            "The storm made landfall at dawn.",
            "{\"claim\":\"storm\",\"sourceId\":\"abc\"}",
            "test-model-v1",
            "prompt-v3");

        var script = AddGeneratedScript(production, generated, sources);

        var stored = Assert.Single(GetScriptGenerations(production));
        Assert.Equal("test-model-v1", stored.ModelId);
        Assert.Equal("prompt-v3", stored.PromptVersion);
        Assert.Equal(generated.Body, stored.Body);
        Assert.Contains("https://example.com/source-1", stored.InputReferencesJson);
        Assert.Contains("ScriptGenerated", GetAuditActions(production));
        Assert.Equal(script.Version, GetScripts(production).Last().Version);
    }

    [Fact]
    public void AGeneratedScriptWithoutItsProvenanceIsRefused()
    {
        var production = CreateProductionWithScript();
        AddVerifiedSources(production);
        var version = GetVersion(production);
        var sources = GetSourcesFor(production);
        var noModel = new GeneratedScript("A body.", "{\"claim\":\"storm\"}", "  ", "prompt-v3");
        var noPrompt = new GeneratedScript("A body.", "{\"claim\":\"storm\"}", "test-model-v1", "  ");

        Mutate(repository => Assert.Throws<ArgumentException>(() => repository.AddGeneratedScript(production, version, noModel, sources)));
        Mutate(repository => Assert.Throws<ArgumentException>(() => repository.AddGeneratedScript(production, version, noPrompt, sources)));

        Assert.Empty(GetScriptGenerations(production));
        Assert.DoesNotContain("ScriptGenerated", GetAuditActions(production));
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
            new StoredObject("assets/one.mp4", Guid.NewGuid().ToString("N"), 0), "   "));
        Assert.Empty(GetAssets(production));
    }

    [Fact]
    public void AnUnknownRightsStatusNeverReachesTheProduction()
    {
        var production = CreateProductionWithScript();

        var failure = Assert.Throws<WorkflowRuleViolation>(() => AttachAsset(production,
            new StoredObject("assets/unknown.mp4", Guid.NewGuid().ToString("N"), 0), "hook", RightsStatus.Unknown));

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

    private TopicCandidate RegisterDiscovered(DiscoveredTopic topic)
    {
        using var context = fixture.CreateContext();
        return new PostgresCandidateRepository(context, ViralScoreWeights.Version1).RegisterDiscovered(topic, ViralScoreWeights.Version1);
    }

    private ScriptVersion AddGeneratedScript(Guid production, GeneratedScript generated, IReadOnlyCollection<SourceItem> sources)
    {
        using var context = fixture.CreateContext();
        return new PostgresProductionRepository(context).AddGeneratedScript(production, GetVersion(production), generated, sources);
    }

    private IReadOnlyCollection<SourceItem> GetSourcesFor(Guid production)
    {
        using var context = fixture.CreateContext();
        return context.SourceItems.AsNoTracking()
            .Where(source => source.TopicCandidateId == GetCandidateId(production))
            .ToArray();
    }

    private void AddVerifiedSources(Guid production)
    {
        var candidateId = GetCandidateId(production);
        foreach (var (url, publisher, excerpt) in new[]
        {
            ("https://example.com/source-1", "One", "The first verified fact."),
            ("https://example.com/source-2", "Two", "The second verified fact.")
        })
        {
            using var context = fixture.CreateContext();
            new PostgresCandidateRepository(context, ViralScoreWeights.Version1)
                .AddSource(candidateId, url, publisher, excerpt, 80);
        }
    }

    private Guid GetCandidateId(Guid production)
    {
        using var context = fixture.CreateContext();
        return context.Productions.AsNoTracking().Single(item => item.Id == production).TopicCandidateId;
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

    /// <summary>
    /// The title has to be unique per call, because a candidate is now identified by its headline
    /// fingerprint as well as its URL. Reusing one title across tests collides on that index.
    /// </summary>
    private TopicCandidate AddCandidate(string url)
    {
        using var context = fixture.CreateContext();
        return new PostgresCandidateRepository(context, ViralScoreWeights.Version1)
            .Add(url, $"A verified story {Guid.NewGuid():N}", "Creator", DateTimeOffset.UtcNow, StrongSignals);
    }

    private void AddScriptVersion(Guid production, string body) =>
        Mutate(repository => repository.AddScript(production, GetVersion(production), body,
            $"{{\"claim\":\"source-{CountScripts(production) + 1}\"}}"));

    private Asset AttachLicensedAsset(Guid production, string purpose) =>
        AttachAsset(production, new StoredObject($"assets/{Guid.NewGuid():N}.mp4", Guid.NewGuid().ToString("N"), 0), purpose);

    private Asset AttachAsset(Guid production, StoredObject stored, string purpose, RightsStatus rightsStatus = RightsStatus.Licensed)
    {
        Mutate(repository => repository.AttachAsset(production, GetVersion(production), stored, "video", null, rightsStatus, purpose, null));
        return new Asset { Id = Guid.NewGuid(), ObjectKey = stored.ObjectKey, Type = "video", RightsStatus = rightsStatus, Checksum = stored.Checksum };
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

    private int CountCandidatesWithFingerprint(string fingerprint)
    {
        using var context = fixture.CreateContext();
        return context.TopicCandidates.Count(candidate => candidate.Fingerprint == fingerprint);
    }

    private IReadOnlyCollection<ScriptGeneration> GetScriptGenerations(Guid production)
    {
        using var context = fixture.CreateContext();
        return new PostgresProductionRepository(context).GetScriptGenerations(production);
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
