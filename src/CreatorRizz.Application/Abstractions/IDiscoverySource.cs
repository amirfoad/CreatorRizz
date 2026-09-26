using CreatorRizz.Domain;

namespace CreatorRizz.Application.Abstractions;

/// <summary>A story a discovery source found, with the viral signals that source can actually support.</summary>
public sealed record DiscoveredTopic(
    string Title,
    string CanonicalUrl,
    string? Creator,
    DateTimeOffset PublishedAt,
    ViralSignals Signals);

/// <summary>
/// A place candidates come from. Adapters translate a provider's own shape into
/// <see cref="DiscoveredTopic"/>; nothing above this boundary knows what an RSS feed or a Data API is.
/// </summary>
public interface IDiscoverySource
{
    string Name { get; }

    ValueTask<IReadOnlyCollection<DiscoveredTopic>> DiscoverAsync(CancellationToken cancellationToken);
}
