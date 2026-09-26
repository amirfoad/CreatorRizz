using CreatorRizz.Domain;

namespace CreatorRizz.Application.Abstractions;

public interface ICandidateRepository
{
    TopicCandidate Add(string canonicalUrl, string title, string? creator, DateTimeOffset publishedAt, ViralSignals signals);
    bool TryGet(Guid id, out TopicCandidate? candidate);
    IReadOnlyCollection<TopicCandidate> List();
    void AddSource(Guid candidateId, string url, string publisher, string? excerpt, int reliabilityScore);
    IReadOnlyCollection<SourceItem> GetSources(Guid candidateId);
    ResearchPack CreateResearchPack(Guid candidateId, string summary, string factsJson, string uncertaintyJson);
    bool TryGetResearchPack(Guid candidateId, out ResearchPack? researchPack);
}
