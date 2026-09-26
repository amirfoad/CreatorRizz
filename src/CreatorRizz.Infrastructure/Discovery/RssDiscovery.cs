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
/// <remarks>
/// The <see cref="HttpClient"/> must be created with <c>AllowAutoRedirect = false</c>. Redirects are
/// walked by hand so every hop is checked against the allowlist; a handler that followed them silently
/// would fetch a host the operator never approved before this class saw a response.
/// </remarks>
public sealed class RssDiscoverySource(HttpClient http, DiscoveryFeedOptions feed, TimeProvider clock) : IDiscoverySource
{
    private const int MaxRedirects = 5;

    public string Name => feed.Name;

    public async ValueTask<IReadOnlyCollection<DiscoveredTopic>> DiscoverAsync(CancellationToken cancellationToken)
    {
        feed.EnsureAllowed();
        var (xml, finalUri) = await ReadFeedAsync(cancellationToken);
        return RssDiscoveryParser.Parse(xml, finalUri)
            .Select(item => new DiscoveredTopic(item.Title, item.Url, item.Publisher, item.PublishedAt, RecencyOnlySignal(item.PublishedAt)))
            .ToArray();
    }

    /// <summary>
    /// Follows redirects one at a time, re-checking the allowlist each time, and reports the URL the
    /// body actually came from so the publisher recorded on a candidate is not the one we asked for.
    /// </summary>
    private async Task<(string Xml, Uri FinalUri)> ReadFeedAsync(CancellationToken cancellationToken)
    {
        var uri = new Uri(feed.Url);
        for (var hop = 0; ; hop++)
        {
            feed.EnsureHostAllowed(uri, "which is not in its own allowlist. Add the host to AllowedHosts before it can be fetched.");
            using var response = await http.GetAsync(uri, cancellationToken);
            if (!IsRedirect(response.StatusCode))
            {
                response.EnsureSuccessStatusCode();
                return (await response.Content.ReadAsStringAsync(cancellationToken), uri);
            }

            var location = response.Headers.Location
                ?? throw new InvalidOperationException($"Discovery feed '{feed.Name}' answered {response.StatusCode} without a Location header.");
            if (hop >= MaxRedirects)
                throw new InvalidOperationException($"Discovery feed '{feed.Name}' redirected more than {MaxRedirects} times. Refusing to keep following it.");
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }
    }

    private static bool IsRedirect(System.Net.HttpStatusCode status) => status is
        System.Net.HttpStatusCode.MovedPermanently or
        System.Net.HttpStatusCode.Found or
        System.Net.HttpStatusCode.SeeOther or
        System.Net.HttpStatusCode.TemporaryRedirect or
        System.Net.HttpStatusCode.PermanentRedirect;

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
