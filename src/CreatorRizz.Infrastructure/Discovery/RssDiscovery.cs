using System.Xml.Linq;
using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using CreatorRizz.Infrastructure.Configuration;

namespace CreatorRizz.Infrastructure.Discovery;

public static class RssDiscoveryParser
{
    public static IReadOnlyCollection<RssItem> Parse(string xml, Uri feedUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        var document = XDocument.Parse(xml, LoadOptions.None);
        return document.Descendants("item")
            .Select(item => new
            {
                Title = item.Element("title")?.Value.Trim(),
                Link = item.Element("link")?.Value.Trim(),
                Date = item.Element("pubDate")?.Value.Trim()
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Title) && Uri.TryCreate(item.Link, UriKind.Absolute, out _))
            .Select(item => new RssItem(
                item.Title!,
                item.Link!,
                feedUri.Host,
                DateTimeOffset.TryParse(item.Date, out var publishedAt) ? publishedAt : DateTimeOffset.UtcNow))
            .ToArray();
    }
}

public sealed record RssItem(string Title, string Url, string Publisher, DateTimeOffset PublishedAt);

/// <summary>
/// Polls one allowlisted RSS feed. A feed that is not on the allowlist is never fetched, so adding a
/// source to configuration cannot silently start pulling an arbitrary site.
/// </summary>
public sealed class RssDiscoverySource(HttpClient http, DiscoveryFeedOptions feed, TimeProvider clock) : IDiscoverySource
{
    public string Name => feed.Name;

    public async ValueTask<IReadOnlyCollection<DiscoveredTopic>> DiscoverAsync(CancellationToken cancellationToken)
    {
        feed.EnsureAllowed();
        using var response = await http.GetAsync(feed.Url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var items = RssDiscoveryParser.Parse(await response.Content.ReadAsStringAsync(cancellationToken), new Uri(feed.Url));
        return items
            .Select(item => new DiscoveredTopic(item.Title, item.Url, item.Publisher, item.PublishedAt, RecencyOnlySignal(item.PublishedAt)))
            .ToArray();
    }

    /// <summary>
    /// An RSS item carries no view count, engagement or story potential, so those signals stay at zero
    /// rather than being invented. The resulting score reflects recency alone and is not comparable to
    /// a candidate from a source that reports real engagement numbers.
    /// </summary>
    private ViralSignals RecencyOnlySignal(DateTimeOffset publishedAt)
    {
        var ageInDays = Math.Max(0, (clock.GetUtcNow() - publishedAt).TotalDays);
        var recency = Math.Clamp(100m - ((decimal)ageInDays * 100m / 30m), 0m, 100m);
        return new ViralSignals(0m, 0m, decimal.Round(recency, 2), 0m, 0m, 0m);
    }
}
