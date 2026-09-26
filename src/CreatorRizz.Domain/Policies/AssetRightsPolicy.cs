namespace CreatorRizz.Domain;

public static class AssetRightsPolicy
{
    public static bool CanAttach(RightsStatus status) => status is RightsStatus.Owned or RightsStatus.Licensed or RightsStatus.PermissionGranted or RightsStatus.PlatformRemix;

    public static bool CanRender(RightsStatus status, bool hasExplicitRightsApproval) =>
        CanAttach(status) || (status is RightsStatus.CommentaryRisk && hasExplicitRightsApproval);

    public static void EnsureCanRender(IEnumerable<RightsStatus> statuses, bool hasExplicitRightsApproval)
    {
        var blocked = statuses.FirstOrDefault(status => !CanRender(status, hasExplicitRightsApproval));
        if (!CanRender(blocked, hasExplicitRightsApproval))
            throw new WorkflowRuleViolation($"Asset with rights status {blocked} cannot enter rendering.");
    }
}
