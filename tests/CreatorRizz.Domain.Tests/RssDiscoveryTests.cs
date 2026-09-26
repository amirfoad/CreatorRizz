using System.Net;
using CreatorRizz.Infrastructure.Configuration;
using CreatorRizz.Infrastructure.Discovery;
using Xunit;

namespace CreatorRizz.Domain.Tests;

/// <summary>
/// The allowlist only means something if it holds for the whole fetch. A handler that follows redirects
/// on its own will read a host the operator never approved, so these tests drive the source through a
/// client that hands back the redirect instead of following it.
/// </summary>
public sealed class RssDiscoveryTests
{
    private const string FeedXml = """
        <rss><channel>
          <item><title>Storm hits the coast</title><link>https://news.example/story/1</link><pubDate>Mon, 01 Sep 2026 08:00:00 GMT</pubDate></item>
        </channel></rss>
        """;

    private static DiscoveryFeedOptions Feed(string url, params string[] allowedHosts) =>
        new() { Name = "Local", Url = url, AllowedHosts = allowedHosts };

    [Fact]
    public async Task ARedirectToAHostOutsideTheAllowlistIsRefused()
    {
        using var http = new HttpClient(new RedirectingHandler(_ => RedirectTo("https://elsewhere.invalid/feed.xml")));
        var source = new RssDiscoverySource(http, Feed("https://feeds.example/feed.xml", "feeds.example"), TimeProvider.System);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.DiscoverAsync(CancellationToken.None).AsTask());

        Assert.Contains("elsewhere.invalid", failure.Message);
    }

    [Fact]
    public async Task ARedirectToAnAllowedHostIsFollowedAndTheFinalHostBecomesThePublisher()
    {
        var requested = new List<Uri>();
        using var http = new HttpClient(new RedirectingHandler(request =>
        {
            requested.Add(request.RequestUri!);
            return request.RequestUri!.Host == "feeds.example"
                ? RedirectTo("https://mirror.example/feed.xml")
                : Ok(FeedXml);
        }));
        var source = new RssDiscoverySource(http, Feed("https://feeds.example/feed.xml", "feeds.example", "mirror.example"), TimeProvider.System);

        var topics = await source.DiscoverAsync(CancellationToken.None);

        Assert.Equal(["feeds.example", "mirror.example"], requested.Select(uri => uri.Host));
        var topic = Assert.Single(topics);
        Assert.Equal("mirror.example", topic.Creator);
        Assert.Equal("Storm hits the coast", topic.Title);
    }

    [Fact]
    public async Task ARelativeRedirectStaysOnTheAllowlistedHost()
    {
        var requested = new List<Uri>();
        using var http = new HttpClient(new RedirectingHandler(request =>
        {
            requested.Add(request.RequestUri!);
            return request.RequestUri!.AbsolutePath == "/feed.xml" ? RedirectTo("/current.xml") : Ok(FeedXml);
        }));
        var source = new RssDiscoverySource(http, Feed("https://feeds.example/feed.xml", "feeds.example"), TimeProvider.System);

        var topics = await source.DiscoverAsync(CancellationToken.None);

        Assert.Equal(["/feed.xml", "/current.xml"], requested.Select(uri => uri.AbsolutePath));
        Assert.Equal("feeds.example", Assert.Single(topics).Creator);
    }

    [Fact]
    public async Task ARedirectLoopIsRefusedInsteadOfRunningForever()
    {
        using var http = new HttpClient(new RedirectingHandler(_ => RedirectTo("https://feeds.example/next.xml")));
        var source = new RssDiscoverySource(http, Feed("https://feeds.example/feed.xml", "feeds.example"), TimeProvider.System);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.DiscoverAsync(CancellationToken.None).AsTask());

        Assert.Contains("redirected more than", failure.Message);
    }

    [Fact]
    public async Task AFailingFeedIsReportedAsAFailureAndNotParsedAsIfItWereAFeed()
    {
        using var http = new HttpClient(new RedirectingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var source = new RssDiscoverySource(http, Feed("https://feeds.example/feed.xml", "feeds.example"), TimeProvider.System);

        await Assert.ThrowsAsync<HttpRequestException>(() => source.DiscoverAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public void AnRssItemCarriesNoEngagementSoThoseSignalsStayAtZero()
    {
        var parsed = RssDiscoveryParser.Parse(FeedXml, new Uri("https://mirror.example/feed.xml"));

        var item = Assert.Single(parsed);
        Assert.Equal("mirror.example", item.Publisher);
    }

    private static HttpResponseMessage RedirectTo(string location) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };

    private static HttpResponseMessage Ok(string xml) =>
        new(HttpStatusCode.OK) { Content = new StringContent(xml) };

    private sealed class RedirectingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
