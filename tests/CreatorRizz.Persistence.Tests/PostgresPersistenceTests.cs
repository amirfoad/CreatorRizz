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
    public async Task RecordedRenderWorkIsHandedToExactlyOneWorker()
    {
        EmptyOutbox();
        var production = QueueRender();

        var (first, second) = await ClaimFromTwoWorkersAtOnce(production);

        // Before the dispatcher existed this row was never picked up at all. The pair is asserted rather
        // than a single claim because two workers is the case that decides whether a render happens twice.
        Assert.Single(new[] { first, second }, claim => claim is not null);
        var claimed = first ?? second!;
        Assert.Equal(production, claimed.ProductionId);
        Assert.Equal(1, claimed.Attempt);
        Assert.Equal("worker-1", ReadOutboxEntry(production).ClaimedBy);
    }

    [Fact]
    public async Task AClaimedRowIsNotOfferedToAnyoneElseWhileTheLeaseHolds()
    {
        EmptyOutbox();
        var production = QueueRender();
        var clock = new MovableClock(DateTimeOffset.UtcNow);

        await using var first = fixture.CreateContext();
        var claimed = await new PostgresProductionJobDispatcher(first, clock)
            .ClaimNextAsync(ProductionJobKind.Render, "worker-1", TimeSpan.FromMinutes(30), 3, CancellationToken.None);

        Assert.NotNull(claimed);
        await using var second = fixture.CreateContext();
        Assert.Null(await new PostgresProductionJobDispatcher(second, clock)
            .ClaimNextAsync(ProductionJobKind.Render, "worker-2", TimeSpan.FromMinutes(30), 3, CancellationToken.None));
    }

    [Fact]
    public async Task AWorkedRowIsNotOfferedAgain()
    {
        EmptyOutbox();
        var production = QueueRender();
        var clock = new MovableClock(DateTimeOffset.UtcNow);

        Guid jobId;
        await using (var context = fixture.CreateContext())
        {
            var dispatcher = new PostgresProductionJobDispatcher(context, clock);
            jobId = (await dispatcher.ClaimNextAsync(ProductionJobKind.Render, "worker-1", TimeSpan.FromMinutes(30), 3, CancellationToken.None))!.Id;
            await dispatcher.CompleteAsync(jobId, CancellationToken.None);
        }

        // A lease that outlives the completed row would still keep it out of circulation; this is the
        // check that completion, not the clock, is what retires the work.
        clock.Advance(TimeSpan.FromHours(2));
        await using var later = fixture.CreateContext();
        Assert.Null(await new PostgresProductionJobDispatcher(later, clock)
            .ClaimNextAsync(ProductionJobKind.Render, "worker-1", TimeSpan.FromMinutes(30), 3, CancellationToken.None));

        var entry = ReadOutboxEntry(production);
        Assert.Equal(jobId, entry.Id);
        Assert.NotNull(entry.CompletedAt);
        Assert.Null(entry.ClaimedBy);
    }

    [Fact]
    public async Task AFailedRowGoesBackOnTheQueueWithTheReasonItFailed()
    {
        EmptyOutbox();
        var production = QueueRender();
        var clock = new MovableClock(DateTimeOffset.UtcNow);

        await using (var context = fixture.CreateContext())
        {
            var dispatcher = new PostgresProductionJobDispatcher(context, clock);
            var claimed = (await dispatcher.ClaimNextAsync(ProductionJobKind.Render, "worker-1", TimeSpan.FromMinutes(30), 3, CancellationToken.None))!;
            await dispatcher.ReleaseAsync(claimed.Id, "FFmpeg exited with 1.\nNo such filter", CancellationToken.None);
        }

        // The row is released, not deleted, and the reason is on the row. "This job is stuck" is not a
        // diagnosis; the operator has to be able to read why without turning on debug logging.
        var released = ReadOutboxEntry(production);
        Assert.Null(released.CompletedAt);
        Assert.Null(released.ClaimedBy);
        Assert.Contains("FFmpeg exited with 1", released.LastError!, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", released.LastError!, StringComparison.Ordinal);

        await using var retry = fixture.CreateContext();
        var retried = await new PostgresProductionJobDispatcher(retry, clock)
            .ClaimNextAsync(ProductionJobKind.Render, "worker-1", TimeSpan.FromMinutes(30), 3, CancellationToken.None);
        Assert.NotNull(retried);
        Assert.Equal(2, retried.Attempt);
    }

    [Fact]
    public async Task AWorkedRowWhoseWorkerDiedBecomesAvailableAgainWhenItsLeaseExpires()
    {
        EmptyOutbox();
        var production = QueueRender();
        var clock = new MovableClock(DateTimeOffset.UtcNow);

        await using (var context = fixture.CreateContext())
        {
            var claimed = await new PostgresProductionJobDispatcher(context, clock)
                .ClaimNextAsync(ProductionJobKind.Render, "worker-1", TimeSpan.FromMinutes(30), 3, CancellationToken.None);
            Assert.NotNull(claimed);
        }

        // Nothing released this row and nothing completed it, because the worker holding it is gone. The
        // lease is the only thing that brings the work back, which is why it is a timeout and not a flag.
        clock.Advance(TimeSpan.FromMinutes(31));
        await using var afterLease = fixture.CreateContext();
        var reclaimed = await new PostgresProductionJobDispatcher(afterLease, clock)
            .ClaimNextAsync(ProductionJobKind.Render, "worker-2", TimeSpan.FromMinutes(30), 3, CancellationToken.None);

        Assert.NotNull(reclaimed);
        Assert.Equal("worker-2", ReadOutboxEntry(production).ClaimedBy);
    }

    [Fact]
    public async Task AJobThatUsedUpItsAttemptsIsLeftAloneRatherThanRetriedForEver()
    {
        EmptyOutbox();
        var production = QueueRender();
        var clock = new MovableClock(DateTimeOffset.UtcNow);

        await using (var context = fixture.CreateContext())
        {
            var dispatcher = new PostgresProductionJobDispatcher(context, clock);
            var claimed = (await dispatcher.ClaimNextAsync(ProductionJobKind.Render, "worker-1", TimeSpan.FromMinutes(30), 1, CancellationToken.None))!;
            await dispatcher.ReleaseAsync(claimed.Id, "FFmpeg is not installed.", CancellationToken.None);
        }

        clock.Advance(TimeSpan.FromMinutes(31));
        await using var exhausted = fixture.CreateContext();
        Assert.Null(await new PostgresProductionJobDispatcher(exhausted, clock)
            .ClaimNextAsync(ProductionJobKind.Render, "worker-1", TimeSpan.FromMinutes(30), 1, CancellationToken.None));

        // It stays on the table, unfinished and with its reason, rather than being deleted or retried.
        var stuck = ReadOutboxEntry(production);
        Assert.Null(stuck.CompletedAt);
        Assert.Equal(1, stuck.Attempts);
        Assert.Contains("FFmpeg is not installed", stuck.LastError!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWorkerThatOnlyHandlesRendersLeavesSpeechRowsAlone()
    {
        EmptyOutbox();
        var production = CreateProductionWithScript();
        var versionReadByCaller = GetVersion(production);
        await using (var context = fixture.CreateContext())
            await new PostgresProductionJobQueue(context)
                .EnqueueTextToSpeechAsync(new TextToSpeechJob(production, "narration", "alloy", 1m),
                    ProductionJobKey.ForTextToSpeech(production, versionReadByCaller), CancellationToken.None);

        await using var dispatcherContext = fixture.CreateContext();
        var dispatcher = new PostgresProductionJobDispatcher(dispatcherContext, new MovableClock(DateTimeOffset.UtcNow));

        Assert.Null(await dispatcher.ClaimNextAsync(ProductionJobKind.Render, "worker-1", TimeSpan.FromMinutes(30), 3, CancellationToken.None));
        var speech = await dispatcher.ClaimNextAsync(ProductionJobKind.TextToSpeech, "worker-1", TimeSpan.FromMinutes(30), 3, CancellationToken.None);
        Assert.NotNull(speech);
        Assert.Equal(ProductionJobKind.TextToSpeech, speech.Kind);
    }

    private Guid QueueRender()
    {
        var production = CreateProductionWithApprovedRights();
        var manifest = new RenderManifest(production, 1080, 1920,
            [new TimelineClip(GetAssets(production).Single().Id, 0, 3000, 0)], [], "voice/one.mp3");
        using var context = fixture.CreateContext();
        WorkflowUsing(context, new PostgresProductionJobQueue(context))
            .QueueRenderAsync(production, GetVersion(production), manifest, CancellationToken.None).GetAwaiter().GetResult();
        return production;
    }

    [Fact]
    public void TheReviewQueueHoldsExactlyTheProductionsWaitingOnADecision()
    {
        var waitingOnScript = CreateProductionWithScript();
        SubmitReview(waitingOnScript, ReviewKind.Script);
        var waitingOnRights = CreateProductionWithAssetsReady();
        SubmitReview(waitingOnRights, ReviewKind.Rights);
        // Approved and rejected productions are not work for anybody; showing them in the queue is how a
        // reviewer ends up re-reading a decision that was already made.
        var alreadyApproved = CreateProductionWithApprovedRights();
        var rejected = CreateProductionWithScript();
        SubmitReview(rejected, ReviewKind.Script);
        DecideReview(rejected, ReviewKind.Script, ReviewOutcome.Reject, "reviewer-1");
        var notStarted = CreateProductionWithScript();
        var mine = new[] { waitingOnScript, waitingOnRights, alreadyApproved, rejected, notStarted };

        var scriptQueue = Mine(List(new ProductionQuery(awaitingReview: ReviewKind.Script)), mine);
        var rightsQueue = Mine(List(new ProductionQuery(awaitingReview: ReviewKind.Rights)), mine);

        Assert.Equal(new[] { waitingOnScript }, scriptQueue.Select(item => item.Id).ToArray());
        Assert.Equal(new[] { waitingOnRights }, rightsQueue.Select(item => item.Id).ToArray());
    }

    [Fact]
    public void TheQueueCanBeNarrowedToOneReviewKind()
    {
        var script = CreateProductionWithScript();
        SubmitReview(script, ReviewKind.Script);
        var rights = CreateProductionWithAssetsReady();
        SubmitReview(rights, ReviewKind.Rights);
        var publish = CreateProductionRendered();
        SubmitReview(publish, ReviewKind.Publish);
        var mine = new[] { script, rights, publish };

        foreach (var (kind, expected) in new (ReviewKind Kind, Guid Expected)[]
                 { (ReviewKind.Script, script), (ReviewKind.Rights, rights), (ReviewKind.Publish, publish) })
        {
            var queue = List(new ProductionQuery(awaitingReview: kind));

            // One review kind, one production, even though all three gates are open at once: a queue that
            // ignored the filter would show all three under every heading.
            Assert.Equal(new[] { expected }, Mine(queue, mine).Select(item => item.Id).ToArray());
            Assert.All(queue.Productions, item => Assert.Equal(kind, ProductionWorkflow.ReviewAwaitingDecision(item.State)));
        }
    }

    [Fact]
    public void TheBacklogCanBeNarrowedToOneState()
    {
        var inReview = CreateProductionWithScript();
        SubmitReview(inReview, ReviewKind.Script);
        var rightsApproved = CreateProductionWithApprovedRights();

        var page = List(new ProductionQuery(state: ProductionState.RightsApproved));

        Assert.Equal(new[] { rightsApproved }, Mine(page, new[] { inReview, rightsApproved }).Select(item => item.Id).ToArray());
    }

    [Fact]
    public void APageIsBoundedAndTheTotalIsTheCountOfTheFilterNotOfThePage()
    {
        // The database is shared, so the exact rows on any page belong to whichever test ran before. What
        // this pins down is the paging contract itself: a page is the size it asked for, pages do not
        // overlap, and the total is wide enough to paginate with.
        var small = List(new ProductionQuery(state: ProductionState.ScriptDraft, skip: 0, take: 2));
        var samePageSizedBigger = List(new ProductionQuery(state: ProductionState.ScriptDraft, skip: 0, take: 200));
        var next = List(new ProductionQuery(state: ProductionState.ScriptDraft, skip: 2, take: 2));

        Assert.Equal(2, small.Productions.Count);
        Assert.Equal(2, next.Productions.Count);
        Assert.True(small.HasMore);
        // The total is filter-wide: it does not shrink when the page does, so a client knows how many
        // pages there are without counting rows itself.
        Assert.Equal(small.TotalCount, samePageSizedBigger.TotalCount);
        Assert.Equal(samePageSizedBigger.Productions.Count, samePageSizedBigger.TotalCount);
        // Skipping advances the window instead of repeating it.
        Assert.Empty(small.Productions.Select(item => item.Id).Intersect(next.Productions.Select(item => item.Id)));
        Assert.Empty(List(new ProductionQuery(state: ProductionState.Published)).Productions);
    }

    [Fact]
    public void TheQueueReportsTheVersionAReviewerHasToSendBack()
    {
        var production = CreateProductionWithScript();
        SubmitReview(production, ReviewKind.Script);
        var versionReadByReviewer = GetVersion(production);

        var item = Assert.Single(Mine(List(new ProductionQuery(awaitingReview: ReviewKind.Script)), new[] { production }));

        // A reviewer that guesses the version gets a conflict instead of overwriting another
        // reviewer's work, which is the whole point of reading it here.
        Assert.Equal(versionReadByReviewer, item.Version);
        Assert.Equal(ProductionState.ScriptInReview, item.State);
    }

    private ProductionPage List(ProductionQuery query)
    {
        using var context = fixture.CreateContext();
        return new PostgresProductionRepository(context).List(query);
    }

    /// <summary>
    /// Narrows a page down to the productions this test created. The fixture shares one database across
    /// the collection, so an unscoped "the queue holds exactly this" would fail on whatever an earlier
    /// test happened to leave in review.
    /// </summary>
    private Production[] Mine(ProductionPage page, IReadOnlyCollection<Guid> mine) =>
        page.Productions.Where(item => mine.Contains(item.Id)).ToArray();

    private Guid CreateProductionWithAssetsReady()
    {
        var production = CreateProductionWithScript();
        SubmitReview(production, ReviewKind.Script);
        DecideReview(production, ReviewKind.Script, ReviewOutcome.Approve, "reviewer-1");
        BeginAssetPreparation(production);
        AttachLicensedAsset(production, "hook");
        DeclareAssetsReady(production);
        Assert.Equal(ProductionState.AssetsReady, GetState(production));
        return production;
    }

    private Guid CreateProductionRendered()
    {
        var production = CreateProductionWithApprovedRights();
        var manifest = new RenderManifest(production, 1080, 1920,
            [new TimelineClip(GetAssets(production).Single().Id, 0, 3000, 0)], [], "voice/one.mp3");
        using var context = fixture.CreateContext();
        WorkflowUsing(context, new PostgresProductionJobQueue(context))
            .QueueRenderAsync(production, GetVersion(production), manifest, CancellationToken.None).GetAwaiter().GetResult();
        CompleteRendering(production);
        Assert.Equal(ProductionState.Rendered, GetState(production));
        return production;
    }

    /// <summary>
    /// The outbox is one shared queue, not a per-production list, so a test that counts claims or claims
    /// twice has to begin with nothing else queued. The collection runs one test at a time, so emptying
    /// the table costs the other tests nothing: they each record the work they need.
    /// </summary>
    private void EmptyOutbox()
    {
        using var context = fixture.CreateContext();
        context.JobOutbox.ExecuteDelete();
    }

    /// <summary>
    /// Two dispatchers, two connections, one row. SKIP LOCKED is what makes the loser skip rather than
    /// wait, and the assertion is that exactly one of them walks away with work.
    /// </summary>
    private async Task<(ClaimedProductionJob? First, ClaimedProductionJob? Second)> ClaimFromTwoWorkersAtOnce(Guid production)
    {
        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();
        var clock = new MovableClock(DateTimeOffset.UtcNow);

        var claims = await Task.WhenAll(
            new PostgresProductionJobDispatcher(first, clock).ClaimNextAsync(ProductionJobKind.Render, "worker-1", TimeSpan.FromMinutes(30), 3, CancellationToken.None),
            new PostgresProductionJobDispatcher(second, clock).ClaimNextAsync(ProductionJobKind.Render, "worker-2", TimeSpan.FromMinutes(30), 3, CancellationToken.None));

        Assert.Single(claims, claim => claim is not null);
        Assert.Equal(production, claims.Single(claim => claim is not null)!.ProductionId);
        return (claims[0], claims[1]);
    }

    /// <summary>
    /// A clock the test moves by hand. Lease expiry is the one behaviour here that must not depend on
    /// how long a test happens to take.
    /// </summary>
    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
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
