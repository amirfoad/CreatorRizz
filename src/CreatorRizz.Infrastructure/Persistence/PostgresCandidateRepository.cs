using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CreatorRizz.Infrastructure.Persistence;

/// <summary>PostgreSQL implementation of the candidate and research persistence port.</summary>
public sealed class PostgresCandidateRepository(CreatorRizzDbContext database) : ICandidateRepository
{
    public TopicCandidate Add(string canonicalUrl, string title, string? creator, DateTimeOffset publishedAt, ViralSignals signals)
    {
        if (string.IsNullOrWhiteSpace(canonicalUrl)) throw new ArgumentException("Canonical URL is required.", nameof(canonicalUrl));
        canonicalUrl = canonicalUrl.Trim();
        if (!Uri.TryCreate(canonicalUrl, UriKind.Absolute, out _)) throw new ArgumentException("CanonicalUrl must be an absolute URL.");
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Candidate title is required.");

        var candidate = new TopicCandidate { CanonicalUrl = canonicalUrl, Title = title.Trim(), Creator = creator, PublishedAt = publishedAt, ViralScore = ViralScore.Calculate(signals), State = ProductionState.Scored };
        database.TopicCandidates.Add(candidate);
        database.SaveWorkflowChanges();
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
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("Source URL is required.", nameof(url));
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) throw new ArgumentException("Source URL must be absolute.");
        if (string.IsNullOrWhiteSpace(publisher)) throw new ArgumentException("Source publisher is required.", nameof(publisher));
        if (reliabilityScore is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(reliabilityScore), "Reliability score must be between 0 and 100.");
        database.SourceItems.Add(new SourceItem { TopicCandidateId = candidateId, Url = url, Publisher = publisher, Excerpt = excerpt, ReliabilityScore = reliabilityScore });
        database.SaveWorkflowChanges();
    }

    public IReadOnlyCollection<SourceItem> GetSources(Guid candidateId) => database.SourceItems.AsNoTracking().Where(item => item.TopicCandidateId == candidateId).ToArray();

    public ResearchPack CreateResearchPack(Guid candidateId, string summary, string factsJson, string uncertaintyJson)
    {
        if (!TryGet(candidateId, out _)) throw new KeyNotFoundException("Candidate was not found.");
        ResearchPolicy.EnsureSourcesAreSufficient(GetSources(candidateId));
        EnsureIsJson(factsJson, nameof(factsJson));
        EnsureIsJson(uncertaintyJson, nameof(uncertaintyJson));
        var pack = new ResearchPack { TopicCandidateId = candidateId, Summary = summary, FactsJson = factsJson, UncertaintyJson = uncertaintyJson };
        database.ResearchPacks.Add(pack);
        database.SaveWorkflowChanges();
        return pack;
    }

    public bool TryGetResearchPack(Guid candidateId, out ResearchPack? researchPack)
    {
        researchPack = database.ResearchPacks.AsNoTracking().SingleOrDefault(item => item.TopicCandidateId == candidateId);
        return researchPack is not null;
    }

    /// <summary>
    /// The facts and uncertainty columns are jsonb, so a malformed value has to fail here with an
    /// actionable message instead of surfacing as a PostgreSQL syntax error.
    /// </summary>
    private static void EnsureIsJson(string value, string parameterName)
    {
        try { JsonDocument.Parse(value); }
        catch (JsonException exception) { throw new ArgumentException("Research facts and uncertainty must be valid JSON.", parameterName, exception); }
    }
}
