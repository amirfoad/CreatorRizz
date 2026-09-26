using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using CreatorRizz.Infrastructure;
using CreatorRizz.Infrastructure.Configuration;
using CreatorRizz.Infrastructure.Discovery;
using CreatorRizz.Infrastructure.Jobs;
using CreatorRizz.Infrastructure.Scripting;
using CreatorRizz.Infrastructure.Storage;
using CreatorRizz.Infrastructure.Tts;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CreatorRizz.Domain.Tests;

public sealed class ProductionStateTests
{
    [Fact]
    public void StateNamesIncludeTheRequiredReviewGates()
    {
        Assert.Contains(ProductionState.ScriptApproved, Enum.GetValues<ProductionState>());
        Assert.Contains(ProductionState.RightsApproved, Enum.GetValues<ProductionState>());
        Assert.Contains(ProductionState.PublishApproved, Enum.GetValues<ProductionState>());
    }

    [Fact]
    public void ScriptApprovalMovesOnlyFromTheScriptReviewGate()
    {
        var reviewState = ProductionWorkflow.SubmitForReview(ProductionState.ScriptDraft, ReviewKind.Script);
        Assert.Equal(ProductionState.ScriptInReview, reviewState);
        Assert.Equal(ProductionState.ScriptApproved, ProductionWorkflow.Decide(reviewState, ReviewKind.Script, ReviewOutcome.Approve));
        Assert.Throws<WorkflowRuleViolation>(() => ProductionWorkflow.Decide(ProductionState.AssetsReady, ReviewKind.Script, ReviewOutcome.Approve));
    }

    [Fact]
    public void AssetChangesInvalidateACompletedRightsReview()
    {
        Assert.Equal(ProductionState.AssetsReady, ProductionWorkflow.InvalidateRightsForAssetChange(ProductionState.RightsApproved));
        Assert.Equal(ProductionState.ScriptDraft, ProductionWorkflow.InvalidateRightsForAssetChange(ProductionState.ScriptDraft));
    }

    [Fact]
    public void ViralScoreUsesTheDocumentedInitialWeights()
    {
        var score = ViralScore.Calculate(new ViralSignals(100, 100, 100, 100, 100, 100));
        Assert.Equal(100, score);
        Assert.Equal(35, ViralScore.Calculate(new ViralSignals(100, 0, 0, 0, 0, 0)));
    }

    [Fact]
    public void ViralScoreWeightsHaveToTotalOneHundredSoAScoreCannotBeSilentlyRescaled()
    {
        var weights = ViralScoreWeights.Version1;
        Assert.Equal(100m, weights.Total);

        var allSignals = new ViralSignals(100, 100, 100, 100, 100, 100);
        var recencyOnly = new ViralScoreWeights(0m, 0m, 100m, 0m, 0m, 0m);
        Assert.Equal(allSignals.Recency, ViralScore.Calculate(allSignals, recencyOnly));

        var broken = weights with { Engagement = 5m };
        var failure = Assert.Throws<ArgumentException>(() => broken.EnsureValid());
        Assert.Contains("must total 100", failure.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => (weights with { Recency = -1m }).EnsureValid());
    }

    [Fact]
    public void TheFingerprintSeesTheSameStoryBehindDifferentUrlsAndFormatting()
    {
        var first = CandidateFingerprint.From("  Meteor   Hits  Coastal Town! ", "Example News");
        var second = CandidateFingerprint.From("meteor hits coastal town", "example news");
        Assert.Equal(first, second);

        Assert.NotEqual(first, CandidateFingerprint.From("Meteor Hits Coastal Town", "Another Publisher"));
        Assert.NotEqual(first, CandidateFingerprint.From("Meteor Hits Coastal Town", null));
    }

    [Fact]
    public void RightsPolicyBlocksUnknownAssetsFromRendering()
    {
        Assert.False(AssetRightsPolicy.CanAttach(RightsStatus.Unknown));
        Assert.False(AssetRightsPolicy.CanRender(RightsStatus.CommentaryRisk, false));
        Assert.True(AssetRightsPolicy.CanRender(RightsStatus.CommentaryRisk, true));
        Assert.Throws<WorkflowRuleViolation>(() => AssetRightsPolicy.EnsureCanRender([RightsStatus.Licensed, RightsStatus.Unknown], true));
    }

    [Fact]
    public void RenderManifestAcceptsTheMvpVerticalFormat()
    {
        var manifest = new RenderManifest(Guid.NewGuid(), 1080, 1920,
            [new TimelineClip(Guid.NewGuid(), 0, 5000, 0)],
            [new CaptionCue(0, 1000, "A verified story")], "voice/one.mp3");
        RenderManifestValidator.Validate(manifest);
    }

    [Fact]
    public void RenderManifestRejectsNonVerticalOutput()
    {
        var manifest = new RenderManifest(Guid.NewGuid(), 1920, 1080,
            [new TimelineClip(Guid.NewGuid(), 0, 5000, 0)], [], "voice/one.mp3");
        Assert.Throws<WorkflowRuleViolation>(() => RenderManifestValidator.Validate(manifest));
    }

    [Fact]
    public void ResearchPolicyRequiresTwoUsableSources()
    {
        var source = new SourceItem { TopicCandidateId = Guid.NewGuid(), Url = "https://example.com/1", Publisher = "Example", Excerpt = "Fact", ReliabilityScore = 80 };
        Assert.Throws<WorkflowRuleViolation>(() => ResearchPolicy.EnsureSourcesAreSufficient([source]));
        ResearchPolicy.EnsureSourcesAreSufficient([source, new SourceItem { TopicCandidateId = source.TopicCandidateId, Url = "https://example.com/2", Publisher = "Example", Excerpt = "Second fact", ReliabilityScore = 60 }]);
    }

    [Fact]
    public void ScriptPolicyRequiresClaimEvidence()
    {
        Assert.Throws<WorkflowRuleViolation>(() => ResearchPolicy.EnsureScriptHasClaimMap("A claim", "{}"));
        ResearchPolicy.EnsureScriptHasClaimMap("A claim", "{\"claim\":\"source-1\"}");
    }

    [Fact]
    public void TheDefaultGeneratorRefusesInsteadOfReturningUncitedText()
    {
        IScriptGenerator generator = new DisabledScriptGenerator();
        var request = new ScriptGenerationRequest(Guid.NewGuid(), Guid.NewGuid(), "A story", "Two sources verify this.",
        [
            new SourceItem { TopicCandidateId = Guid.NewGuid(), Url = "https://example.com/1", Publisher = "One", Excerpt = "Fact", ReliabilityScore = 80 },
            new SourceItem { TopicCandidateId = Guid.NewGuid(), Url = "https://example.com/2", Publisher = "Two", Excerpt = "Second fact", ReliabilityScore = 80 }
        ]);

        var failure = Assert.Throws<InvalidOperationException>(() => generator.GenerateAsync(request, CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("No script generator is configured", failure.Message);
    }

    [Fact]
    public void TheDefaultGeneratorStillRejectsAnEmptyRequestBeforeRefusing()
    {
        IScriptGenerator generator = new DisabledScriptGenerator();
        var noSummary = new ScriptGenerationRequest(Guid.NewGuid(), Guid.NewGuid(), "A story", "  ", []);
        Assert.Throws<ArgumentException>(() => generator.GenerateAsync(noSummary, CancellationToken.None).GetAwaiter().GetResult());
    }

    [Fact]
    public void SubtitleWriterCreatesStandardSrtTiming()
    {
        var result = SrtSubtitleWriter.Write([new CaptionCue(1500, 3250, "Verified context")]);
        Assert.Contains("00:00:01,500 --> 00:00:03,250", result);
        Assert.Contains("Verified context", result);
    }

    [Fact]
    public void SubtitleWriterRejectsInvalidCaptionWithoutNeedingARenderManifest()
    {
        Assert.Throws<WorkflowRuleViolation>(() => SrtSubtitleWriter.Write([new CaptionCue(1000, 1000, "Invalid timing")]));
    }

    [Fact]
    public void PublishingRequiresFinalHumanApprovalAndApprovedAssets()
    {
        Assert.Throws<WorkflowRuleViolation>(() => PublishingPolicy.EnsureCanUpload(ProductionState.Rendered, [RightsStatus.Licensed]));
        Assert.Throws<WorkflowRuleViolation>(() => PublishingPolicy.EnsureCanUpload(ProductionState.PublishApproved, [RightsStatus.Unknown]));
        PublishingPolicy.EnsureCanUpload(ProductionState.PublishApproved, [RightsStatus.Licensed]);
    }

    [Fact]
    public async Task LocalObjectStoragePreservesContentAndRejectsTraversal()
    {
        var path = Path.Combine(Path.GetTempPath(), $"creatorrizz-{Guid.NewGuid():N}");
        try
        {
            var storage = new LocalObjectStorage(path);
            await using var input = new MemoryStream("hello"u8.ToArray());
            var stored = await storage.PutAsync("assets/hello.txt", input, CancellationToken.None);
            Assert.Equal(5, stored.Length);
            await using var output = await storage.OpenReadAsync(stored.ObjectKey, CancellationToken.None);
            using var reader = new StreamReader(output);
            Assert.Equal("hello", await reader.ReadToEndAsync());
            await Assert.ThrowsAsync<ArgumentException>(async () => await storage.PutAsync("../escape.txt", new MemoryStream(), CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task BackgroundQueuePreservesJobTypeAndPayload()
    {
        IBackgroundJobQueue queue = new InMemoryBackgroundJobQueue();
        await queue.EnqueueAsync("research", "{\"candidateId\":\"abc\"}", CancellationToken.None);
        var job = await queue.DequeueAsync(CancellationToken.None);
        Assert.Equal("research", job.Type);
        Assert.Equal("{\"candidateId\":\"abc\"}", job.PayloadJson);
    }

    [Fact]
    public async Task DisabledTtsProviderRejectsInvalidSpeedBeforeAnyExternalCall()
    {
        ITextToSpeechProvider provider = new DisabledTextToSpeechProvider();
        var request = new TextToSpeechRequest(Guid.NewGuid(), "Narration", "voice", 3m);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => provider.SynthesizeAsync(request, CancellationToken.None));
    }

    [Fact]
    public void TtsUsesAlloyWhenNoVoiceIsRequested()
    {
        var request = new TextToSpeechRequest(Guid.NewGuid(), "Narration", null, 1m);
        Assert.Equal("alloy", request.EffectiveVoiceId);
    }

    [Fact]
    public void RenderingRequiresRightsApproval()
    {
        Assert.Throws<WorkflowRuleViolation>(() => ProductionWorkflow.BeginRendering(ProductionState.AssetsReady));
        Assert.Equal(ProductionState.Rendering, ProductionWorkflow.BeginRendering(ProductionState.RightsApproved));
    }

    [Fact]
    public void AssetPreparationOnlyStartsAfterScriptApproval()
    {
        Assert.Equal(ProductionState.AssetsPreparing, ProductionWorkflow.BeginAssetPreparation(ProductionState.ScriptApproved));
        Assert.Throws<WorkflowRuleViolation>(() => ProductionWorkflow.BeginAssetPreparation(ProductionState.ScriptDraft));
    }

    [Fact]
    public void AssetsMustBePreparedBeforeTheRightsGateOpens()
    {
        Assert.Equal(ProductionState.AssetsReady, ProductionWorkflow.DeclareAssetsReady(ProductionState.AssetsPreparing));
        Assert.Throws<WorkflowRuleViolation>(() => ProductionWorkflow.DeclareAssetsReady(ProductionState.ScriptApproved));
    }

    [Fact]
    public void RenderCompletionRequiresARenderInProgress()
    {
        Assert.Equal(ProductionState.Rendered, ProductionWorkflow.CompleteRendering(ProductionState.Rendering));
        Assert.Throws<WorkflowRuleViolation>(() => ProductionWorkflow.CompleteRendering(ProductionState.RightsApproved));
    }

    [Fact]
    public void AssetUsageRejectsABlankNarrativePurpose()
    {
        Assert.Throws<ArgumentException>(() => AssetUsagePolicy.NormalizePurpose("  "));
        Assert.Equal("evidence", AssetUsagePolicy.NormalizePurpose("  evidence  "));
    }

    [Fact]
    public void AssetUsageTimingIsOnlyCheckedWhenBothBoundsAreKnown()
    {
        AssetUsagePolicy.EnsureTimingIsOrdered(null, null);
        AssetUsagePolicy.EnsureTimingIsOrdered(400, 3200);
        Assert.Throws<WorkflowRuleViolation>(() => AssetUsagePolicy.EnsureTimingIsOrdered(3200, 400));
        Assert.Throws<WorkflowRuleViolation>(() => AssetUsagePolicy.EnsureTimingIsOrdered(-1, 400));
    }

    [Fact]
    public void AProductionAcceptsChangesOnlyForTheVersionThatWasRead()
    {
        var production = new Production { Version = 7 };
        production.EnsureVersion(7);

        var conflict = Assert.Throws<ProductionVersionConflict>(() => production.EnsureVersion(6));
        Assert.Equal(6, conflict.ExpectedVersion);
        Assert.Equal(7, conflict.CurrentVersion);
    }

    [Fact]
    public void RssParserReturnsOnlyItemsWithValidLinks()
    {
        const string xml = """
            <rss><channel>
              <item><title>Valid story</title><link>https://example.com/story</link><pubDate>2026-09-26T08:00:00Z</pubDate></item>
              <item><title>Invalid story</title><link>not a url</link></item>
            </channel></rss>
            """;
        var topics = RssDiscoveryParser.Parse(xml, new Uri("https://example.com/feed.xml"));
        var topic = Assert.Single(topics);
        Assert.Equal("Valid story", topic.Title);
        Assert.Equal("example.com", topic.Publisher);
    }

    [Fact]
    public void ConfiguredWeightsReplaceTheDefaultsInsteadOfFallingBackToThem()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Discovery:Weights:ViewVelocity"] = "50",
            ["Discovery:Weights:Engagement"] = "20",
            ["Discovery:Weights:Recency"] = "15",
            ["Discovery:Weights:CreatorRelevance"] = "5",
            ["Discovery:Weights:CrossSource"] = "5",
            ["Discovery:Weights:StoryPotential"] = "5"
        }).Build();

        var weights = configuration.ToWeights();

        Assert.Equal(100m, weights.Total);
        Assert.Equal(50m, weights.ViewVelocity);
        Assert.Equal(5m, weights.StoryPotential);
        Assert.Equal(50m, ViralScore.Calculate(new ViralSignals(100, 0, 0, 0, 0, 0), weights));
    }

    [Fact]
    public void AMisspelledWeightNameFailsInsteadOfSilentlyKeepingTheDefaults()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Discovery:Weights:Engagement"] = "50",
            ["Discovery:Weights:Novelty"] = "20"
        }).Build();

        var failure = Assert.Throws<InvalidOperationException>(() => configuration.ToWeights());

        Assert.Contains("Novelty", failure.Message);
        Assert.Contains("ViewVelocity", failure.Message);
    }

    [Fact]
    public void AFeedMustListItsOwnHostBeforeItCanBeFetched()
    {
        var allowed = new DiscoveryFeedOptions { Name = "Example", Url = "https://news.example.com/feed.xml", AllowedHosts = ["news.example.com"] };
        allowed.EnsureAllowed();

        var blocked = new DiscoveryFeedOptions { Name = "Example", Url = "https://news.example.com/feed.xml", AllowedHosts = ["other.example.com"] };
        var failure = Assert.Throws<InvalidOperationException>(blocked.EnsureAllowed);
        Assert.Contains("news.example.com", failure.Message);
    }
}
