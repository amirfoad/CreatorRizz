using System.Text.Json;

namespace Shorts.Domain;

public sealed record ScriptDraft(string Body, string ClaimMapJson);

public static class ScriptDraftComposer
{
    public static ScriptDraft Compose(TopicCandidate candidate, ResearchPack researchPack, IReadOnlyCollection<SourceItem> sources)
    {
        ResearchPolicy.EnsureSourcesAreSufficient(sources);
        var usableSources = sources.Where(source => source.ReliabilityScore >= 50 && !string.IsNullOrWhiteSpace(source.Excerpt)).ToArray();
        var hook = $"Here is what happened with {candidate.Creator ?? "this creator"}.";
        var body = $"{hook}\n\n{researchPack.Summary}\n\nWhat we can verify: {string.Join(" ", usableSources.Select(source => source.Excerpt))}\n\nThe available sources do not prove more than this.";
        var claimMap = JsonSerializer.Serialize(usableSources.Select(source => new { claim = source.Excerpt, sourceId = source.Id, sourceUrl = source.Url }));
        ResearchPolicy.EnsureScriptHasClaimMap(body, claimMap);
        return new ScriptDraft(body, claimMap);
    }
}
