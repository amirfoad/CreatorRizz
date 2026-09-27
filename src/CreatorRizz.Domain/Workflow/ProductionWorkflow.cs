namespace CreatorRizz.Domain;

public enum ReviewKind { Script, Rights, Publish }
public enum ReviewOutcome { Approve, Rework, Reject }

public sealed class WorkflowRuleViolation(string message) : InvalidOperationException(message);

public static class ProductionWorkflow
{
    public static ProductionState SubmitForReview(ProductionState state, ReviewKind kind) => (state, kind) switch
    {
        (ProductionState.ScriptDraft, ReviewKind.Script) => ProductionState.ScriptInReview,
        (ProductionState.AssetsReady, ReviewKind.Rights) => ProductionState.RightsReview,
        (ProductionState.Rendered, ReviewKind.Publish) => ProductionState.PublishReview,
        _ => throw new WorkflowRuleViolation($"Cannot submit {kind} review from {state}.")
    };

    public static ProductionState Decide(ProductionState state, ReviewKind kind, ReviewOutcome outcome) => (state, kind, outcome) switch
    {
        (ProductionState.ScriptInReview, ReviewKind.Script, ReviewOutcome.Approve) => ProductionState.ScriptApproved,
        (ProductionState.ScriptInReview, ReviewKind.Script, ReviewOutcome.Rework) => ProductionState.ScriptDraft,
        (ProductionState.ScriptInReview, ReviewKind.Script, ReviewOutcome.Reject) => ProductionState.Rejected,
        (ProductionState.RightsReview, ReviewKind.Rights, ReviewOutcome.Approve) => ProductionState.RightsApproved,
        (ProductionState.RightsReview, ReviewKind.Rights, ReviewOutcome.Rework) => ProductionState.AssetsPreparing,
        (ProductionState.RightsReview, ReviewKind.Rights, ReviewOutcome.Reject) => ProductionState.Rejected,
        (ProductionState.PublishReview, ReviewKind.Publish, ReviewOutcome.Approve) => ProductionState.PublishApproved,
        (ProductionState.PublishReview, ReviewKind.Publish, ReviewOutcome.Rework) => ProductionState.Rendered,
        (ProductionState.PublishReview, ReviewKind.Publish, ReviewOutcome.Reject) => ProductionState.Rejected,
        _ => throw new WorkflowRuleViolation($"Cannot apply {outcome} to {kind} review from {state}.")
    };

    public static ProductionState InvalidateRightsForAssetChange(ProductionState state) => state switch
    {
        ProductionState.RightsApproved or ProductionState.Rendering or ProductionState.Rendered or ProductionState.PublishReview or ProductionState.PublishApproved => ProductionState.AssetsReady,
        _ => state
    };

    public static ProductionState BeginAssetPreparation(ProductionState state) => state == ProductionState.ScriptApproved
        ? ProductionState.AssetsPreparing
        : throw new WorkflowRuleViolation("Script approval is required before preparing assets.");

    public static ProductionState DeclareAssetsReady(ProductionState state) => state == ProductionState.AssetsPreparing
        ? ProductionState.AssetsReady
        : throw new WorkflowRuleViolation("Assets must be prepared before the rights review can start.");

    public static ProductionState CompleteRendering(ProductionState state) => state == ProductionState.Rendering
        ? ProductionState.Rendered
        : throw new WorkflowRuleViolation("Rendering must be in progress before the render can complete.");

    public static ProductionState BeginRendering(ProductionState state) => state == ProductionState.RightsApproved
        ? ProductionState.Rendering
        : throw new WorkflowRuleViolation("Rights approval is required before rendering.");

    /// <summary>
    /// The review a production is sitting in front of a reviewer for, or null when it is not waiting on
    /// one. This lives next to the transitions above because it is the same mapping read backwards; a
    /// query that worked it out again would be a second place to keep in step.
    /// </summary>
    public static ReviewKind? ReviewAwaitingDecision(ProductionState state) => state switch
    {
        ProductionState.ScriptInReview => ReviewKind.Script,
        ProductionState.RightsReview => ReviewKind.Rights,
        ProductionState.PublishReview => ReviewKind.Publish,
        _ => null
    };
}
