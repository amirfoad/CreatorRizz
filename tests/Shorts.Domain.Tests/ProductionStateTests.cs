using Shorts.Domain;
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
}
