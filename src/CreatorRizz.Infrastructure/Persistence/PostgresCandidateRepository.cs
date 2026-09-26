using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CreatorRizz.Infrastructure.Persistence;

/// <summary>PostgreSQL implementation of the candidate and research persistence port.</summary>
public sealed class PostgresCandidateRepository(CreatorRizzDbContext database, ViralScoreWeights viralScoreWeights) : ICandidateRepository
{
    public TopicCandidate Add(string canonicalUrl, string title, string? creator, DateTimeOffset publishedAt, ViralSignals signals)
    {
        var url = NormalizeUrl(canonicalUrl);
        RequireTitle(title);
        var fingerprint = CandidateFingerprint.From(title, creator);
        var existing = FindByUrlOrFingerprint(url, fingerprint);
        if (existing is not null)
            throw new InvalidOperationException(
                $"This story is already registered as candidate {existing.Id} at '{existing.CanonicalUrl}'. Discovery registers repeats instead of failing.");

        var candidate = new TopicCandidate
        {
            CanonicalUrl = url,
            Title = title.Trim(),
            Creator = creator,
            Fingerprint = fingerprint,
            PublishedAt = publishedAt,
            ViralScore = ViralScore.Calculate(signals, viralScoreWeights),
            State = ProductionState.Scored
        };
        database.TopicCandidates.Add(candidate);
        database.SaveWorkflowChanges();
        return candidate;
    }

    public TopicCandidate RegisterDiscovered(DiscoveredTopic topic, ViralScoreWeights weights)
    {
        ArgumentNullException.ThrowIfNull(topic);
        var url = NormalizeUrl(topic.CanonicalUrl);
        RequireTitle(topic.Title);
        var fingerprint = CandidateFingerprint.From(topic.Title, topic.Creator);

        var existing = FindByUrlOrFingerprint(url, fingerprint);
        if (existing is not null) return existing;

        var candidate = new TopicCandidate
        {
            CanonicalUrl = url,
            Title = topic.Title.Trim(),
            Creator = topic.Creator,
            Fingerprint = fingerprint,
            PublishedAt = topic.PublishedAt,
            ViralScore = ViralScore.Calculate(topic.Signals, weights),
            State = ProductionState.Scored
        };
        database.TopicCandidates.Add(candidate);
        try
        {
            database.SaveWorkflowChanges();
        }
        catch (InvalidOperationException)
        {
            // A concurrent discovery run registered the same story between the check and the write.
            // Losing that race is the expected outcome of polling, not a failure.
            database.ChangeTracker.Clear();
            var winner = FindByUrlOrFingerprint(url, fingerprint);
            if (winner is null) throw;
            return winner;
        }
        return candidate;
    }

    private TopicCandidate? FindByUrlOrFingerprint(string canonicalUrl, string fingerprint) =>
        database.TopicCandidates.AsNoTracking()
            .SingleOrDefault(item => item.CanonicalUrl == canonicalUrl || item.Fingerprint == fingerprint);

    private static string NormalizeUrl(string canonicalUrl)
    {
        if (string.IsNullOrWhiteSpace(canonicalUrl)) throw new ArgumentException("Canonical URL is required.", nameof(canonicalUrl));
        var url = canonicalUrl.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) throw new ArgumentException("CanonicalUrl must be an absolute URL.", nameof(canonicalUrl));
        return url;
    }

    private static void RequireTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Candidate title is required.", nameof(title));
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
