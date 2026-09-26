namespace Shorts.Domain;

public static class PublishingPolicy
{
    public static void EnsureCanUpload(ProductionState state, IReadOnlyCollection<RightsStatus> assetStatuses)
    {
        if (state != ProductionState.PublishApproved) throw new WorkflowRuleViolation("Only PublishApproved productions may upload.");
        if (assetStatuses.Count == 0) throw new WorkflowRuleViolation("At least one approved asset is required before upload.");
        AssetRightsPolicy.EnsureCanRender(assetStatuses, hasExplicitRightsApproval: true);
    }
}
