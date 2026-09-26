using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using CreatorRizz.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CreatorRizz.Infrastructure.Scripting;

/// <summary>
/// Talks to any endpoint that speaks the OpenAI chat completions shape. The provider is asked for a
/// script and a claim map in one reply, and the claim map is checked against the sources it was given
/// before the script is returned.
/// </summary>
/// <remarks>
/// A model that cites a source it was not given has produced text a reviewer cannot check, so the whole
/// reply is refused rather than the bad claim being dropped. Losing the reply costs a retry; accepting
/// it puts an unauditable script in front of the script review gate.
/// </remarks>
public sealed class OpenAiCompatibleScriptGenerator(HttpClient http, ScriptGeneratorOptions options) : IScriptGenerator
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<GeneratedScript> GenerateAsync(ScriptGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Summary)) throw new ArgumentException("A research pack summary is required.", nameof(request));
        if (request.Sources.Count == 0) throw new ArgumentException("At least one source is required.", nameof(request));

        var usable = request.Sources
            .Where(source => source.ReliabilityScore >= ResearchPolicy.MinimumReliabilityScore && !string.IsNullOrWhiteSpace(source.Excerpt))
            .ToArray();

        // BaseUrl is the provider's API root, which by the OpenAI convention already ends in the version
        // segment, so only the operation is appended. Resolving "v1/chat/completions" against it would
        // produce a doubled path like /v1/v1/chat/completions.
        var endpoint = new Uri($"{options.BaseUrl.TrimEnd('/')}/chat/completions");
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new ChatCompletionRequest(
                options.Model,
                [
                    new ChatMessage("system", SystemPrompt),
                    new ChatMessage("user", BuildUserMessage(request, usable))
                ],
                new Dictionary<string, string> { ["type"] = "json_object" }))
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new ScriptGenerationFailedException(
                $"The script provider at '{options.BaseUrl}' could not be reached: {exception.Message}", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ScriptGenerationFailedException(
                $"The script provider at '{options.BaseUrl}' did not answer within {options.TimeoutSeconds} seconds.", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new ScriptGenerationFailedException(
                    $"The script provider returned {(int)response.StatusCode} for model '{options.Model}'. " +
                    "Check the model name, the key and the provider's rate limit before retrying.");

            var body = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(ReadOptions, cancellationToken);
            return Build(options, usable, body);
        }
    }

    /// <summary>
    /// Turns a reply into a script, or explains why it cannot. Every failure here is the provider's
    /// fault, never the workflow's, so all of them surface as <see cref="ScriptGenerationFailedException"/>.
    /// </summary>
    private static GeneratedScript Build(ScriptGeneratorOptions options, SourceItem[] usable, ChatCompletionResponse? body)
    {
        var content = body?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
            throw new ScriptGenerationFailedException(
                $"The script provider returned no message content for model '{options.Model}'. The model may have been filtered or exhausted its output budget.");

        GeneratedPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<GeneratedPayload>(Unwrap(content), ReadOptions);
        }
        catch (JsonException exception)
        {
            throw new ScriptGenerationFailedException(
                $"The script provider did not return the requested JSON object: {exception.Message}", exception);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Script))
            throw new ScriptGenerationFailedException("The script provider returned a payload with no script text.");
        if (payload.Claims is null || payload.Claims.Count == 0)
            throw new ScriptGenerationFailedException(
                "The script provider returned a script with no claim map. A script that cites nothing cannot enter the script review gate.");

        var urlsById = usable.ToDictionary(source => source.Id, source => source.Url);
        var claims = new List<object>(payload.Claims.Count);
        foreach (var claim in payload.Claims)
        {
            var sourceId = Guid.TryParse(claim.SourceId, out var parsed) ? parsed : Guid.Empty;
            if (sourceId == Guid.Empty || !urlsById.TryGetValue(sourceId, out var url))
                throw new ScriptGenerationFailedException(
                    $"The script provider cited source '{claim.SourceId}', which is not one of the sources it was given. " +
                    "The claim map would not be checkable, so the script was discarded.");
            if (string.IsNullOrWhiteSpace(claim.Claim))
                throw new ScriptGenerationFailedException("The script provider returned a claim with no text.");
            claims.Add(new { Claim = claim.Claim, SourceId = sourceId, Url = url });
        }

        var claimMap = JsonSerializer.Serialize(new { Claims = claims }, ReadOptions);
        ResearchPolicy.EnsureScriptHasClaimMap(payload.Script, claimMap);
        return new GeneratedScript(payload.Script, claimMap, FirstNonBlank(body?.Model, options.Model), options.PromptVersion);
    }

    private static string BuildUserMessage(ScriptGenerationRequest request, SourceItem[] usable) => JsonSerializer.Serialize(new
    {
        Title = request.Title,
        ResearchSummary = request.Summary,
        Sources = usable.Select(source => new
        {
            SourceId = source.Id,
            Url = source.Url,
            Publisher = source.Publisher,
            ReliabilityScore = source.ReliabilityScore,
            Fact = source.Excerpt
        })
    }, ReadOptions);

    private const string SystemPrompt = """
        You write narration for short vertical videos, grounded only in the sources you are given.

        Rules:
        - Use only facts that appear in the sources. Never add a fact, number, date, name or quote that is not in them.
        - If the sources disagree or leave a gap open, say so in the narration instead of resolving it yourself.
        - Write for a spoken voice: plain sentences, no headings, no markdown, no lists, no stage directions.
        - Cite the id of the source that supports each factual assertion.

        Reply with a single JSON object and nothing else:
        {"script": "the narration", "claims": [{"sourceId": "the id from the sources list", "claim": "the assertion that source supports"}]}
        """;

    /// <summary>Strips a markdown fence some servers wrap JSON in, so a fenced reply is not a parse failure.</summary>
    private static string Unwrap(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```")) return trimmed;
        var firstNewLine = trimmed.IndexOf('\n');
        if (firstNewLine < 0) return trimmed;
        var closing = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return closing <= firstNewLine ? trimmed : trimmed[(firstNewLine + 1)..closing].Trim();
    }

    private static string FirstNonBlank(string? preferred, string fallback) => string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

    private sealed record ChatCompletionRequest(string Model, IReadOnlyCollection<ChatMessage> Messages, object ResponseFormat);
    private sealed record ChatMessage(string Role, string Content);
    private sealed record ChatCompletionResponse(string? Model, IReadOnlyList<ChatChoice>? Choices);
    private sealed record ChatChoice(ChatMessage? Message);
    private sealed record GeneratedPayload(string? Script, IReadOnlyList<GeneratedClaim>? Claims);
    private sealed record GeneratedClaim(string? SourceId, string? Claim);
}

public static class ScriptGeneratorServiceCollectionExtensions
{
    public const string HttpClientName = "script-generator";

    /// <summary>
    /// Registers the refusing generator when no credentials are configured and the real adapter when
    /// they are, so an unconfigured deployment fails at the draft with an actionable message instead of
    /// refusing to start.
    /// </summary>
    public static IServiceCollection AddScriptGenerator(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ScriptGeneratorOptions>()
            .Bind(configuration.GetSection(ScriptGeneratorOptions.SectionName))
            .Validate(options => options.TimeoutSeconds > 0, "Script generator timeout must be positive.")
            .ValidateOnStart();

        var options = configuration.GetSection(ScriptGeneratorOptions.SectionName).Get<ScriptGeneratorOptions>() ?? new ScriptGeneratorOptions();
        if (!options.IsConfigured)
        {
            services.AddScoped<IScriptGenerator, DisabledScriptGenerator>();
            return services;
        }

        options.EnsureUsable();
        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds))
            // A redirect would replay the bearer token against a host the operator never named.
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<IScriptGenerator>(provider => new OpenAiCompatibleScriptGenerator(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName), options));
        return services;
    }
}
