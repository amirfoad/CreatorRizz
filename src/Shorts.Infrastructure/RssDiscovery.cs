using System.Xml.Linq;

namespace Shorts.Infrastructure;

public sealed record DiscoveredTopic(string Title, string CanonicalUrl, DateTimeOffset PublishedAt, string Publisher);

public static class RssDiscoveryParser
{
    public static IReadOnlyCollection<DiscoveredTopic> Parse(string xml, Uri feedUri)
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
            .Select(item => new DiscoveredTopic(
                item.Title!,
                item.Link!,
                DateTimeOffset.TryParse(item.Date, out var publishedAt) ? publishedAt : DateTimeOffset.UtcNow,
                feedUri.Host))
            .ToArray();
    }
}
