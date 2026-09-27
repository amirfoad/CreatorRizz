using CreatorRizz.Domain;
using Xunit;

namespace CreatorRizz.Domain.Tests;

/// <summary>
/// The review queue is built from the state a production is sitting in, so the mapping from state to
/// review kind is now load-bearing in two directions: the transitions put a production into a waiting
/// state, and the queue reads it back. A state that maps one way and not the other is a queue that is
/// silently empty.
/// </summary>
public sealed class ReviewQueueTests
{
    public static TheoryData<ProductionState, ReviewKind> WaitingStates => new()
    {
        { ProductionState.ScriptInReview, ReviewKind.Script },
        { ProductionState.RightsReview, ReviewKind.Rights },
        { ProductionState.PublishReview, ReviewKind.Publish }
    };

    [Theory]
    [MemberData(nameof(WaitingStates))]
    public void AWaitingProductionNamesTheReviewItIsWaitingOn(ProductionState state, ReviewKind expected)
    {
        Assert.Equal(expected, ProductionWorkflow.ReviewAwaitingDecision(state));
    }

    [Theory]
    [MemberData(nameof(WaitingStates))]
    public void TheStateAReviewReturnsToIsNotOneTheQueueIsWaitingOn(ProductionState state, ReviewKind kind)
    {
        // Approving script review lands in ScriptApproved, not back in a review state. If that were
        // counted as waiting, an approved production would keep showing up in the queue forever.
        var approved = ProductionWorkflow.Decide(state, kind, ReviewOutcome.Approve);

        Assert.Null(ProductionWorkflow.ReviewAwaitingDecision(approved));
    }

    [Fact]
    public void EveryWaitingStateIsReachableBySubmittingThatSameReview()
    {
        // The mapping is only trustworthy if the two ends agree. Submitting review kind X from the state
        // that review is submitted from must land in the state the queue looks for.
        var from = new Dictionary<ReviewKind, ProductionState>
        {
            [ReviewKind.Script] = ProductionState.ScriptDraft,
            [ReviewKind.Rights] = ProductionState.AssetsReady,
            [ReviewKind.Publish] = ProductionState.Rendered
        };

        foreach (var (kind, origin) in from)
        {
            var submitted = ProductionWorkflow.SubmitForReview(origin, kind);
            Assert.Equal(kind, ProductionWorkflow.ReviewAwaitingDecision(submitted));
        }
    }

    [Fact]
    public void AProductionThatIsNotInReviewBelongsInNoQueue()
    {
        foreach (var state in Enum.GetValues<ProductionState>())
        {
            if (state is ProductionState.ScriptInReview or ProductionState.RightsReview or ProductionState.PublishReview) continue;
            Assert.Null(ProductionWorkflow.ReviewAwaitingDecision(state));
        }
    }

    [Fact]
    public void APageThatCannotBeServedIsRefusedAtTheQueryRatherThanDeepInsideTheDatabase()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProductionQuery(skip: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProductionQuery(take: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProductionQuery(take: ProductionQuery.MaxPageSize + 1));
    }

    [Fact]
    public void APageKnowsWhetherAnotherOneExistsWithoutTheCallerCountingRows()
    {
        var firstPage = new ProductionPage(new Production[50], TotalCount: 120, Skip: 0, Take: 50);
        var lastFullPage = new ProductionPage(new Production[50], TotalCount: 100, Skip: 50, Take: 50);
        var shortPage = new ProductionPage(new Production[20], TotalCount: 120, Skip: 50, Take: 50);
        var empty = new ProductionPage([], TotalCount: 0, Skip: 0, Take: 50);

        Assert.True(firstPage.HasMore);
        Assert.False(lastFullPage.HasMore);
        // A page that came back short is the last page: the filter matched nothing beyond it, whatever
        // the count says, and a "next" button that leads nowhere is worse than no button.
        Assert.False(shortPage.HasMore);
        Assert.False(empty.HasMore);
    }
}
