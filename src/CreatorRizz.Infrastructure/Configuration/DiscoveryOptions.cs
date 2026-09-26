using CreatorRizz.Domain;
using Microsoft.Extensions.Configuration;

namespace CreatorRizz.Infrastructure.Configuration;

public sealed class DiscoveryOptions
{
    public const string SectionName = "Discovery";

    public int PollIntervalSeconds { get; init; } = 900;
    public List<DiscoveryFeedOptions> Feeds { get; init; } = [];
    public ViralScoreWeightsOptions Weights { get; init; } = new();
}

/// <summary>One RSS feed the discovery worker is allowed to read.</summary>
public sealed class DiscoveryFeedOptions
{
    public required string Name { get; init; }
    public required string Url { get; init; }
    public string[] AllowedHosts { get; init; } = [];

    public void EnsureAllowed()
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"Discovery feed '{Name}' is not an absolute URL.");
        EnsureHostAllowed(uri, "which is not in its own allowlist. Add the host to AllowedHosts before it can be fetched.");
    }

    /// <summary>
    /// Checks one hop of a fetch, not just the configured URL. An allowlisted feed can answer with a
    /// redirect to anywhere, so a host that was never allowlisted would otherwise be pulled in.
    /// </summary>
    public void EnsureHostAllowed(Uri uri, string problem)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException($"Discovery feed '{Name}' resolves to '{uri}', which is not an http or https URL.");
        if (!AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Discovery feed '{Name}' resolves to '{uri.Host}', {problem}");
    }
}

public sealed class ViralScoreWeightsOptions
{
    public decimal ViewVelocity { get; init; } = 35m;
    public decimal Engagement { get; init; } = 20m;
    public decimal Recency { get; init; } = 15m;
    public decimal CreatorRelevance { get; init; } = 10m;
    public decimal CrossSource { get; init; } = 10m;
    public decimal StoryPotential { get; init; } = 10m;

    public ViralScoreWeights ToWeights() => new(ViewVelocity, Engagement, Recency, CreatorRelevance, CrossSource, StoryPotential);

    /// <summary>
    /// Configuration binding drops a key it does not recognise without a word, which would leave the
    /// defaults in place and score every candidate with weights the operator never wrote. A misspelled
    /// weight name has to fail loudly instead.
    /// </summary>
    public void EnsureNoUnknownKeys(IConfiguration section)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            nameof(ViewVelocity), nameof(Engagement), nameof(Recency),
            nameof(CreatorRelevance), nameof(CrossSource), nameof(StoryPotential)
        };
        var unknown = section.GetChildren()
            .Select(child => child.Key)
            .Where(key => !known.Contains(key))
            .ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException(
                $"Unknown viral score weight(s) in '{DiscoveryOptions.SectionName}:Weights': {string.Join(", ", unknown)}. Valid names are {string.Join(", ", known)}.");
    }
}

public static class DiscoveryOptionsExtensions
{
    public static ViralScoreWeights ToWeights(this IConfiguration configuration)
    {
        var section = configuration.GetSection(DiscoveryOptions.SectionName);
        var options = section.Get<DiscoveryOptions>();
        var weights = section.GetSection("Weights");
        if (weights.Exists()) (options?.Weights ?? new ViralScoreWeightsOptions()).EnsureNoUnknownKeys(weights);
        return options?.Weights.ToWeights() ?? ViralScoreWeights.Version1;
    }
}
