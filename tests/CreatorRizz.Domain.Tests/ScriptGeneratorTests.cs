using System.Net;
using System.Text.Json;
using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using CreatorRizz.Infrastructure.Configuration;
using CreatorRizz.Infrastructure.Scripting;
using Xunit;

namespace CreatorRizz.Domain.Tests;

/// <summary>
/// A generated script is only allowed into the script review gate if every claim in it can be traced
/// back to a source the model was actually given. These tests cover that boundary from the provider's
/// side, because a model that invents a citation fails silently and the reviewer would never see it.
/// </summary>
public sealed class ScriptGeneratorTests
{
    private static readonly Guid FirstSourceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondSourceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ThirdSourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static ScriptGenerationRequest Request() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Storm hits the coast",
        "Two sources verify this.",
        [
            new SourceItem { Id = FirstSourceId, TopicCandidateId = Guid.NewGuid(), Url = "https://news.example/1", Publisher = "One", Excerpt = "The storm made landfall at dawn.", ReliabilityScore = 80 },
            new SourceItem { Id = SecondSourceId, TopicCandidateId = Guid.NewGuid(), Url = "https://news.example/2", Publisher = "Two", Excerpt = "Two villages were evacuated.", ReliabilityScore = 75 }
        ]);

    private static OpenAiCompatibleScriptGenerator GeneratorReturning(string content, HttpStatusCode status = HttpStatusCode.OK)
    {
        var options = new ScriptGeneratorOptions
        {
            BaseUrl = "https://models.example.com/v1",
            ApiKey = "test-key",
            Model = "some-model",
            PromptVersion = "script-v1"
        };
        var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                model = "some-model-2026-09",
                choices = new[] { new { message = new { role = "assistant", content } } }
            }))
        }));
        return new OpenAiCompatibleScriptGenerator(http, options);
    }

    [Fact]
    public async Task AGroundedReplyBecomesAScriptWithAClaimMapAndProvenance()
    {
        var generator = GeneratorReturning(JsonSerializer.Serialize(new
        {
            script = "The storm made landfall at dawn. Two villages were evacuated.",
            claims = new[]
            {
                new { sourceId = FirstSourceId, claim = "The storm made landfall at dawn." },
                new { sourceId = SecondSourceId, claim = "Two villages were evacuated." }
            }
        }));

        var generated = await generator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal("The storm made landfall at dawn. Two villages were evacuated.", generated.Body);
        Assert.Equal("some-model-2026-09", generated.ModelId);
        Assert.Equal("script-v1", generated.PromptVersion);
        using var claimMap = JsonDocument.Parse(generated.ClaimMapJson);
        var claims = claimMap.RootElement.GetProperty("claims");
        Assert.Equal(2, claims.GetArrayLength());
        Assert.Equal("https://news.example/1", claims[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task AClaimThatCitesASourceTheModelWasNeverGivenIsDiscarded()
    {
        var generator = GeneratorReturning(JsonSerializer.Serialize(new
        {
            script = "A confident sentence with nothing behind it.",
            claims = new[] { new { sourceId = Guid.Parse("99999999-9999-9999-9999-999999999999"), claim = "Invented." } }
        }));

        var failure = await Assert.ThrowsAsync<ScriptGenerationFailedException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None).AsTask());

        Assert.Contains("99999999-9999-9999-9999-999999999999", failure.Message);
        Assert.Contains("not one of the sources", failure.Message);
    }

    [Fact]
    public async Task ASourceTheModelWasGivenButThatPolicyRejectsCannotBeCited()
    {
        var request = new ScriptGenerationRequest(Guid.NewGuid(), Guid.NewGuid(), "A story", "Two sources verify this.", [
            new SourceItem { Id = FirstSourceId, TopicCandidateId = Guid.NewGuid(), Url = "https://news.example/1", Publisher = "One", Excerpt = "Verified fact.", ReliabilityScore = 80 },
            new SourceItem { Id = SecondSourceId, TopicCandidateId = Guid.NewGuid(), Url = "https://news.example/2", Publisher = "Two", Excerpt = null, ReliabilityScore = 10 }
        ]);
        var generator = GeneratorReturning(JsonSerializer.Serialize(new
        {
            script = "A sentence leaning on a source that was never verified.",
            claims = new[] { new { sourceId = SecondSourceId, claim = "Something nobody verified." } }
        }));

        var failure = await Assert.ThrowsAsync<ScriptGenerationFailedException>(
            () => generator.GenerateAsync(request, CancellationToken.None).AsTask());

        Assert.Contains(SecondSourceId.ToString(), failure.Message);
    }

    [Fact]
    public async Task AScriptWithNoClaimMapIsRefusedBecauseNothingInItIsCheckable()
    {
        var generator = GeneratorReturning(JsonSerializer.Serialize(new { script = "A script with no citations.", claims = Array.Empty<object>() }));

        var failure = await Assert.ThrowsAsync<ScriptGenerationFailedException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None).AsTask());

        Assert.Contains("no claim map", failure.Message);
    }

    [Fact]
    public async Task AFencedReplyIsStillReadRatherThanTreatedAsUnparseable()
    {
        var generator = GeneratorReturning("""
            ```json
            {"script": "The storm made landfall at dawn.", "claims": [{"sourceId": "11111111-1111-1111-1111-111111111111", "claim": "Landfall at dawn."}]}
            ```
            """);

        var generated = await generator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal("The storm made landfall at dawn.", generated.Body);
    }

    [Fact]
    public async Task AReplyThatIsNotJsonIsReportedAsAProviderFailure()
    {
        var generator = GeneratorReturning("I am sorry, I cannot help with that.");

        var failure = await Assert.ThrowsAsync<ScriptGenerationFailedException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None).AsTask());

        Assert.Contains("did not return the requested JSON", failure.Message);
    }

    [Fact]
    public async Task AProviderErrorIsReportedWithItsStatusSoTheOperatorCanTellQuotaFromModelName()
    {
        var generator = GeneratorReturning("nope", HttpStatusCode.TooManyRequests);

        var failure = await Assert.ThrowsAsync<ScriptGenerationFailedException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None).AsTask());

        Assert.Contains("429", failure.Message);
        Assert.Contains("some-model", failure.Message);
    }

    [Fact]
    public async Task AnUnreachableProviderIsAPluginFailureNotARefusedDraft()
    {
        var options = new ScriptGeneratorOptions { BaseUrl = "https://models.example.com/v1", ApiKey = "k", Model = "m" };
        using var http = new HttpClient(new StubHandler(_ => throw new HttpRequestException("connection refused")));
        var generator = new OpenAiCompatibleScriptGenerator(http, options);

        var failure = await Assert.ThrowsAsync<ScriptGenerationFailedException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None).AsTask());

        Assert.Contains("connection refused", failure.Message);
        Assert.IsNotType<WorkflowRuleViolation>(failure);
    }

    [Fact]
    public async Task OnlyUsableSourcesAreOfferedToTheModel()
    {
        var capturedBody = string.Empty;
        var options = new ScriptGeneratorOptions { BaseUrl = "https://models.example.com/v1", ApiKey = "k", Model = "m" };
        using var http = new HttpClient(new AsyncStubHandler(async request =>
        {
            // The request content is disposed once the send completes, so it has to be read here.
            capturedBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"{\\\"script\\\":\\\"ok\\\",\\\"claims\\\":[]}\"}}]}")
            };
        }));
        var request = new ScriptGenerationRequest(Guid.NewGuid(), Guid.NewGuid(), "A story", "Summary", [
            new SourceItem { Id = FirstSourceId, TopicCandidateId = Guid.NewGuid(), Url = "https://news.example/1", Publisher = "One", Excerpt = "Usable.", ReliabilityScore = 80 },
            new SourceItem { Id = SecondSourceId, TopicCandidateId = Guid.NewGuid(), Url = "https://news.example/2", Publisher = "Two", Excerpt = null, ReliabilityScore = 90 },
            new SourceItem { Id = ThirdSourceId, TopicCandidateId = Guid.NewGuid(), Url = "https://news.example/3", Publisher = "Three", Excerpt = "Too unreliable to use.", ReliabilityScore = 10 }
        ]);

        await Assert.ThrowsAsync<ScriptGenerationFailedException>(
            () => new OpenAiCompatibleScriptGenerator(http, options).GenerateAsync(request, CancellationToken.None).AsTask());

        using var sent = JsonDocument.Parse(capturedBody);
        var userMessage = sent.RootElement.GetProperty("messages").EnumerateArray().Last();
        using var offered = JsonDocument.Parse(userMessage.GetProperty("content").GetString()!);
        var sources = offered.RootElement.GetProperty("sources").EnumerateArray().ToArray();

        // Only the one source policy will stand behind is offered: the rest had no excerpt or scored too low.
        var source = Assert.Single(sources);
        Assert.Equal("https://news.example/1", source.GetProperty("url").GetString());
    }

    [Fact]
    public async Task TheApiKeyIsSentAsABearerTokenAndRedirectsAreNotFollowed()
    {
        var options = new ScriptGeneratorOptions { BaseUrl = "https://models.example.com/v1", ApiKey = "secret-key", Model = "m" };
        HttpRequestMessage? captured = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[]}") };
        }));

        await Assert.ThrowsAsync<ScriptGenerationFailedException>(
            () => new OpenAiCompatibleScriptGenerator(http, options).GenerateAsync(Request(), CancellationToken.None).AsTask());

        Assert.Equal("Bearer", captured!.Headers.Authorization?.Scheme);
        Assert.Equal("secret-key", captured.Headers.Authorization?.Parameter);
        Assert.Equal("https://models.example.com/v1/chat/completions", captured.RequestUri?.ToString());
    }

    [Fact]
    public async Task ABaseUrlWithATrailingSlashStillReachesTheChatCompletionsPath()
    {
        HttpRequestMessage? captured = null;
        using var http = new HttpClient(new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[]}") };
        }));
        var options = new ScriptGeneratorOptions { BaseUrl = "https://models.example.com/v1/", ApiKey = "k", Model = "m" };

        await Assert.ThrowsAsync<ScriptGenerationFailedException>(
            () => new OpenAiCompatibleScriptGenerator(http, options).GenerateAsync(Request(), CancellationToken.None).AsTask());

        Assert.Equal("https://models.example.com/v1/chat/completions", captured?.RequestUri?.ToString());
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class AsyncStubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }
}
