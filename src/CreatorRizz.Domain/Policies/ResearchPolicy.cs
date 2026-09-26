namespace CreatorRizz.Domain;

public static class ResearchPolicy
{
    public const int MinimumSourceCount = 2;

    public static void EnsureSourcesAreSufficient(IEnumerable<SourceItem> sources)
    {
        var usable = sources.Count(source => source.ReliabilityScore >= 50 && !string.IsNullOrWhiteSpace(source.Excerpt));
        if (usable < MinimumSourceCount) throw new WorkflowRuleViolation("A research pack requires at least two usable sources.");
    }

    public static void EnsureScriptHasClaimMap(string body, string claimMapJson)
    {
        if (string.IsNullOrWhiteSpace(body)) throw new WorkflowRuleViolation("A script body is required.");
        if (string.IsNullOrWhiteSpace(claimMapJson) || claimMapJson == "{}") throw new WorkflowRuleViolation("Every script requires a non-empty claim map.");
    }
}
