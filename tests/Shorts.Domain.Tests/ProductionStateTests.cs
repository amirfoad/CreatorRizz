using Shorts.Domain;
using Shorts.Infrastructure;
using Xunit;

namespace Shorts.Domain.Tests;

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
    public void DraftComposerCreatesACitedScriptFromResearch()
    {
        var candidate = new TopicCandidate { CanonicalUrl = "https://example.com/video", Title = "A story", Creator = "Creator", PublishedAt = DateTimeOffset.UtcNow };
        var pack = new ResearchPack { TopicCandidateId = candidate.Id, Summary = "The event has been verified.", FactsJson = "[]", UncertaintyJson = "[]" };
        var sources = new[]
        {
            new SourceItem { TopicCandidateId = candidate.Id, Url = "https://example.com/source-1", Publisher = "One", Excerpt = "First verified fact.", ReliabilityScore = 80 },
            new SourceItem { TopicCandidateId = candidate.Id, Url = "https://example.com/source-2", Publisher = "Two", Excerpt = "Second verified fact.", ReliabilityScore = 75 }
        };
        var draft = ScriptDraftComposer.Compose(candidate, pack, sources);
        Assert.Contains("source-1", draft.ClaimMapJson);
        Assert.Contains("The event has been verified.", draft.Body);
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
}
