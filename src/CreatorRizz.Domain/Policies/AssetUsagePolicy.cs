namespace CreatorRizz.Domain;

/// <summary>
/// Rules for an asset usage row. The narrative purpose is always required; source timing is
/// optional because it only becomes known once a render manifest places the asset on the timeline.
/// </summary>
public static class AssetUsagePolicy
{
    public static string NormalizePurpose(string narrativePurpose)
    {
        if (string.IsNullOrWhiteSpace(narrativePurpose))
            throw new ArgumentException("An asset usage requires a narrative purpose.", nameof(narrativePurpose));
        return narrativePurpose.Trim();
    }

    public static void EnsureTimingIsOrdered(int? inMilliseconds, int? outMilliseconds)
    {
        if (inMilliseconds is null || outMilliseconds is null) return;
        if (inMilliseconds < 0 || outMilliseconds <= inMilliseconds)
            throw new WorkflowRuleViolation("Asset usage timing must start at or after zero and end after it starts.");
    }
}
