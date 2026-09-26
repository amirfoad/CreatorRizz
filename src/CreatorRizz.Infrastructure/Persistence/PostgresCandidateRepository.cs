using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using Microsoft.EntityFrameworkCore;

namespace CreatorRizz.Infrastructure.Persistence;

/// <summary>PostgreSQL implementation of the candidate and research persistence port.</summary>
public sealed class PostgresCandidateRepository(CreatorRizzDbContext database) : ICandidateRepository
{
    public TopicCandidate Add(string canonicalUrl, string title, string? creator, DateTimeOffset publishedAt, ViralSignals signals)
    {
        canonicalUrl = canonicalUrl.Trim();
        if (!Uri.TryCreate(canonicalUrl, UriKind.Absolute, out _)) throw new ArgumentException("CanonicalUrl must be an absolute URL.");
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Candidate title is required.");

        var candidate = new TopicCandidate { CanonicalUrl = canonicalUrl, Title = title.Trim(), Creator = creator, PublishedAt = publishedAt, ViralScore = ViralScore.Calculate(signals), State = ProductionState.Scored };
        database.TopicCandidates.Add(candidate);
        Save();
        return candidate;
    }

    public bool TryGet(Guid id, out TopicCandidate? candidate)
    {
        candidate = database.TopicCandidates.Find(id);
        return candidate is not null;
    }

    public IReadOnlyCollection<TopicCandidate> List() => database.TopicCandidates.AsNoTracking().OrderByDescending(item => item.ViralScore).ToArray();

    public void AddSource(Guid candidateId, string url, string publisher, string? excerpt, int reliabilityScore)
    {
        if (!TryGet(candidateId, out _)) throw new KeyNotFoundException("Candidate was not found.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) throw new ArgumentException("Source URL must be absolute.");
        if (reliabilityScore is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(reliabilityScore), "Reliability score must be between 0 and 100.");
        database.SourceItems.Add(new SourceItem { TopicCandidateId = candidateId, Url = url, Publisher = publisher, Excerpt = excerpt, ReliabilityScore = reliabilityScore });
        Save();
    }

    public IReadOnlyCollection<SourceItem> GetSources(Guid candidateId) => database.SourceItems.AsNoTracking().Where(item => item.TopicCandidateId == candidateId).ToArray();

    public ResearchPack CreateResearchPack(Guid candidateId, string summary, string factsJson, string uncertaintyJson)
    {
        if (!TryGet(candidateId, out _)) throw new KeyNotFoundException("Candidate was not found.");
        ResearchPolicy.EnsureSourcesAreSufficient(GetSources(candidateId));
        var pack = new ResearchPack { TopicCandidateId = candidateId, Summary = summary, FactsJson = factsJson, UncertaintyJson = uncertaintyJson };
        database.ResearchPacks.Add(pack);
        Save();
        return pack;
    }

    public bool TryGetResearchPack(Guid candidateId, out ResearchPack? researchPack)
    {
        researchPack = database.ResearchPacks.AsNoTracking().SingleOrDefault(item => item.TopicCandidateId == candidateId);
        return researchPack is not null;
    }

    private void Save()
    {
        try { database.SaveChanges(); }
        catch (DbUpdateException exception) { throw new InvalidOperationException("PostgreSQL could not persist the candidate workflow change.", exception); }
    }
}
