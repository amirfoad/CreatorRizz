using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;

namespace CreatorRizz.Api.Contracts;

public sealed record CreateCandidateRequest(
    string CanonicalUrl,
    string Title,
    string? Creator,
    DateTimeOffset PublishedAt,
    ViralSignals Signals);

/// <summary>A story as a feed reported it, translated at the boundary into the discovery port's shape.</summary>
public sealed record RegisterDiscoveredTopicRequest(
    string CanonicalUrl,
    string Title,
    string? Creator,
    DateTimeOffset PublishedAt,
    ViralSignals Signals)
{
    public DiscoveredTopic ToDiscoveredTopic() => new(Title, CanonicalUrl, Creator, PublishedAt, Signals);
}

public sealed record CreateSourceRequest(
    string Url,
    string Publisher,
    string? Excerpt,
    int ReliabilityScore);

public sealed record CreateResearchPackRequest(
    string Summary,
    string FactsJson,
    string UncertaintyJson);
