using Shorts.Domain;

namespace Shorts.Api.Contracts;

public sealed record CreateCandidateRequest(
    string CanonicalUrl,
    string Title,
    string? Creator,
    DateTimeOffset PublishedAt,
    ViralSignals Signals);

public sealed record CreateSourceRequest(
    string Url,
    string Publisher,
    string? Excerpt,
    int ReliabilityScore);

public sealed record CreateResearchPackRequest(
    string Summary,
    string FactsJson,
    string UncertaintyJson);
