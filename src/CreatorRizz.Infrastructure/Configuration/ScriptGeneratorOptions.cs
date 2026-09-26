namespace CreatorRizz.Infrastructure.Configuration;

/// <summary>
/// Points at any endpoint that speaks the OpenAI chat completions shape, so the same adapter covers a
/// hosted model and a locally hosted one without a second code path.
/// </summary>
public sealed class ScriptGeneratorOptions
{
    public const string SectionName = "ScriptGenerator";

    public string BaseUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string PromptVersion { get; init; } = "script-v1";
    public int TimeoutSeconds { get; init; } = 120;

    /// <summary>
    /// False when credentials are missing. Startup then registers the refusing generator instead of
    /// failing, so a checkout without a key still runs and still refuses at the gate rather than
    /// refusing to boot.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model);

    public void EnsureUsable()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUrl))
            throw new InvalidOperationException($"'{SectionName}:BaseUrl' must be an absolute URL.");
        // The key travels as a bearer token, so plaintext is only tolerated for a model running on this
        // machine, where the request never leaves it.
        if (baseUrl.Scheme != Uri.UriSchemeHttps && !(baseUrl.Scheme == Uri.UriSchemeHttp && baseUrl.IsLoopback))
            throw new InvalidOperationException(
                $"'{SectionName}:BaseUrl' must use https, or http on a loopback address for a local model. It is '{baseUrl}'.");
        if (string.IsNullOrWhiteSpace(Model)) throw new InvalidOperationException($"'{SectionName}:Model' is required.");
        if (string.IsNullOrWhiteSpace(ApiKey)) throw new InvalidOperationException($"'{SectionName}:ApiKey' is required.");
    }
}
