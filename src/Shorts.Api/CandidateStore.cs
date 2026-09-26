using System.Collections.Concurrent;
using Shorts.Domain;

namespace Shorts.Api;

public sealed class CandidateStore
{
    private readonly ConcurrentDictionary<string, TopicCandidate> _byUrl = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, List<SourceItem>> _sources = new();
    private readonly ConcurrentDictionary<Guid, ResearchPack> _researchPacks = new();

    public TopicCandidate Add(CreateCandidateRequest request)
    {
        var canonicalUrl = request.CanonicalUrl.Trim();
        if (!Uri.TryCreate(canonicalUrl, UriKind.Absolute, out _)) throw new ArgumentException("CanonicalUrl must be an absolute URL.");
        if (string.IsNullOrWhiteSpace(request.Title)) throw new ArgumentException("Candidate title is required.");
        var candidate = new TopicCandidate { CanonicalUrl = canonicalUrl, Title = request.Title.Trim(), Creator = request.Creator, PublishedAt = request.PublishedAt, ViralScore = ViralScore.Calculate(request.Signals), State = ProductionState.Scored };
        if (!_byUrl.TryAdd(canonicalUrl, candidate)) throw new InvalidOperationException("A candidate with this canonical URL already exists.");
        return candidate;
    }

    public bool TryGet(Guid id, out TopicCandidate? candidate)
    {
        candidate = _byUrl.Values.SingleOrDefault(x => x.Id == id);
        return candidate is not null;
    }

    public IReadOnlyCollection<TopicCandidate> List() => _byUrl.Values.OrderByDescending(x => x.ViralScore).ToArray();

    public void AddSource(Guid candidateId, CreateSourceRequest request)
    {
        if (!TryGet(candidateId, out _)) throw new KeyNotFoundException("Candidate was not found.");
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _)) throw new ArgumentException("Source URL must be absolute.");
        if (request.ReliabilityScore is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(request), "Reliability score must be between 0 and 100.");
        var sources = _sources.GetOrAdd(candidateId, _ => []);
        lock (sources) sources.Add(new SourceItem { TopicCandidateId = candidateId, Url = request.Url, Publisher = request.Publisher, Excerpt = request.Excerpt, ReliabilityScore = request.ReliabilityScore });
    }

    public IReadOnlyCollection<SourceItem> GetSources(Guid candidateId) => _sources.TryGetValue(candidateId, out var sources) ? sources.ToArray() : [];

    public ResearchPack CreateResearchPack(Guid candidateId, CreateResearchPackRequest request)
    {
        if (!TryGet(candidateId, out _)) throw new KeyNotFoundException("Candidate was not found.");
        ResearchPolicy.EnsureSourcesAreSufficient(GetSources(candidateId));
        var researchPack = new ResearchPack { TopicCandidateId = candidateId, Summary = request.Summary, FactsJson = request.FactsJson, UncertaintyJson = request.UncertaintyJson };
        _researchPacks[candidateId] = researchPack;
        return researchPack;
    }

    public bool TryGetResearchPack(Guid candidateId, out ResearchPack? researchPack) => _researchPacks.TryGetValue(candidateId, out researchPack);
}
