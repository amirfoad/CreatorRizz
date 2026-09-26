using CreatorRizz.Domain;

namespace CreatorRizz.Application.Abstractions;

public interface ICandidateRepository
{
    TopicCandidate Add(string canonicalUrl, string title, string? creator, DateTimeOffset publishedAt, ViralSignals signals);

    /// <summary>
    /// Registers a discovered story, or returns the candidate already recorded for it. Discovery runs
    /// on a timer and retries, so this must never fail on a repeat visit; the canonical URL and the
    /// headline fingerprint are both treated as the same story.
    /// </summary>
    TopicCandidate RegisterDiscovered(DiscoveredTopic topic, ViralScoreWeights weights);

    bool TryGet(Guid id, out TopicCandidate? candidate);
    IReadOnlyCollection<TopicCandidate> List();
    void AddSource(Guid candidateId, string url, string publisher, string? excerpt, int reliabilityScore);
    IReadOnlyCollection<SourceItem> GetSources(Guid candidateId);
    ResearchPack CreateResearchPack(Guid candidateId, string summary, string factsJson, string uncertaintyJson);
    bool TryGetResearchPack(Guid candidateId, out ResearchPack? researchPack);
}
