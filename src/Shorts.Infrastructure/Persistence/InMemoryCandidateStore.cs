using System.Collections.Concurrent;
using Shorts.Domain;

namespace Shorts.Infrastructure.Persistence;

/// <summary>Development-only persistence adapter. Data is lost when the process stops.</summary>
public sealed class InMemoryCandidateStore
{
    private readonly ConcurrentDictionary<string, TopicCandidate> _byUrl = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, List<SourceItem>> _sources = new();
    private readonly ConcurrentDictionary<Guid, ResearchPack> _researchPacks = new();

    public TopicCandidate Add(string canonicalUrl, string title, string? creator, DateTimeOffset publishedAt, ViralSignals signals)
    {
        canonicalUrl = canonicalUrl.Trim();
        if (!Uri.TryCreate(canonicalUrl, UriKind.Absolute, out _)) throw new ArgumentException("CanonicalUrl must be an absolute URL.");
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Candidate title is required.");
        var candidate = new TopicCandidate { CanonicalUrl = canonicalUrl, Title = title.Trim(), Creator = creator, PublishedAt = publishedAt, ViralScore = ViralScore.Calculate(signals), State = ProductionState.Scored };
        if (!_byUrl.TryAdd(canonicalUrl, candidate)) throw new InvalidOperationException("A candidate with this canonical URL already exists.");
        return candidate;
    }

    public bool TryGet(Guid id, out TopicCandidate? candidate)
    {
        candidate = _byUrl.Values.SingleOrDefault(x => x.Id == id);
        return candidate is not null;
    }

    public IReadOnlyCollection<TopicCandidate> List() => _byUrl.Values.OrderByDescending(x => x.ViralScore).ToArray();

    public void AddSource(Guid candidateId, string url, string publisher, string? excerpt, int reliabilityScore)
    {
        if (!TryGet(candidateId, out _)) throw new KeyNotFoundException("Candidate was not found.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) throw new ArgumentException("Source URL must be absolute.");
        if (reliabilityScore is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(reliabilityScore), "Reliability score must be between 0 and 100.");
        var sources = _sources.GetOrAdd(candidateId, _ => []);
        lock (sources) sources.Add(new SourceItem { TopicCandidateId = candidateId, Url = url, Publisher = publisher, Excerpt = excerpt, ReliabilityScore = reliabilityScore });
    }

    public IReadOnlyCollection<SourceItem> GetSources(Guid candidateId) => _sources.TryGetValue(candidateId, out var sources) ? sources.ToArray() : [];

    public ResearchPack CreateResearchPack(Guid candidateId, string summary, string factsJson, string uncertaintyJson)
    {
        if (!TryGet(candidateId, out _)) throw new KeyNotFoundException("Candidate was not found.");
        ResearchPolicy.EnsureSourcesAreSufficient(GetSources(candidateId));
        var researchPack = new ResearchPack { TopicCandidateId = candidateId, Summary = summary, FactsJson = factsJson, UncertaintyJson = uncertaintyJson };
        _researchPacks[candidateId] = researchPack;
        return researchPack;
    }

    public bool TryGetResearchPack(Guid candidateId, out ResearchPack? researchPack) => _researchPacks.TryGetValue(candidateId, out researchPack);
}
