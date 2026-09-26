using CreatorRizz.Infrastructure.Configuration;
using CreatorRizz.Infrastructure.DependencyInjection;
using CreatorRizz.Infrastructure.Scripting;
using CreatorRizz.Infrastructure.Tts;
using CreatorRizz.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CreatorRizz.Domain.Tests;

/// <summary>
/// Covers the composition root itself. Validating an option through a helper is not the same as
/// validating it on the path the application actually starts up with, and only the second one can keep
/// a bad value out of a running system.
/// </summary>
public sealed class InfrastructureCompositionTests
{
    private static IConfiguration ConfigurationWith(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ExternalServices:DatabaseConnectionString"] = "Host=localhost;Database=shorts;Username=shorts;Password=shorts",
            ["ExternalServices:RedisConnectionString"] = "localhost:6379",
            ["ExternalServices:ObjectStorageEndpoint"] = "http://localhost:9000"
        };
        foreach (var (key, value) in overrides) values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void AMisspelledWeightNameStopsStartupInsteadOfSilentlyKeepingTheDefaults()
    {
        var services = new ServiceCollection();

        var failure = Assert.Throws<InvalidOperationException>(() => services.AddCreatorRizzInfrastructure(
            ConfigurationWith(("Discovery:Weights:Engagement", "50"), ("Discovery:Weights:Novelty", "20"))));

        Assert.Contains("Novelty", failure.Message);
        Assert.Contains("ViewVelocity", failure.Message);
    }

    [Fact]
    public void WeightsThatDoNotTotalOneHundredStopStartup()
    {
        var services = new ServiceCollection();

        var failure = Assert.Throws<ArgumentException>(() => services.AddCreatorRizzInfrastructure(
            ConfigurationWith(("Discovery:Weights:ViewVelocity", "35"), ("Discovery:Weights:Engagement", "5"))));

        Assert.Contains("must total 100", failure.Message);
    }

    [Fact]
    public void TheRefusingTtsProviderIsRegisteredSoThePortIsPartOfTheGraph()
    {
        var services = new ServiceCollection();
        services.AddCreatorRizzInfrastructure(ConfigurationWith());
        using var provider = services.BuildServiceProvider();

        Assert.IsType<DisabledTextToSpeechProvider>(provider.GetRequiredService<ITextToSpeechProvider>());
    }

    [Fact]
    public async Task TheRefusingTtsProviderSaysWhatIsMissingRatherThanFailingSilently()
    {
        ITextToSpeechProvider provider = new DisabledTextToSpeechProvider();
        var request = new TextToSpeechRequest(Guid.NewGuid(), "Narration", "voice", 1m);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SynthesizeAsync(request, CancellationToken.None));

        Assert.Contains("No TTS provider is configured", failure.Message);
    }

    [Fact]
    public void APlaintextModelEndpointIsRefusedSoTheApiKeyIsNotSentInTheClear()
    {
        var services = new ServiceCollection();

        var failure = Assert.Throws<InvalidOperationException>(() => services.AddCreatorRizzInfrastructure(
            ConfigurationWith(
                ("ScriptGenerator:BaseUrl", "http://models.example.com"),
                ("ScriptGenerator:ApiKey", "secret"),
                ("ScriptGenerator:Model", "some-model"))));

        Assert.Contains("https", failure.Message);
    }

    [Fact]
    public void ALocalModelOnLoopbackIsAllowedOverPlaintext()
    {
        var services = new ServiceCollection();

        services.AddCreatorRizzInfrastructure(ConfigurationWith(
            ("ScriptGenerator:BaseUrl", "http://127.0.0.1:11434/v1"),
            ("ScriptGenerator:ApiKey", "local"),
            ("ScriptGenerator:Model", "some-model")));

        using var provider = services.BuildServiceProvider();
        Assert.IsType<OpenAiCompatibleScriptGenerator>(provider.GetRequiredService<IScriptGenerator>());
    }

    [Fact]
    public void WithoutCredentialsTheRefusingGeneratorIsRegisteredSoAKeyIsNotRequiredToStart()
    {
        var services = new ServiceCollection();
        services.AddCreatorRizzInfrastructure(ConfigurationWith());
        using var provider = services.BuildServiceProvider();

        Assert.IsType<DisabledScriptGenerator>(provider.GetRequiredService<IScriptGenerator>());
    }

    [Fact]
    public void AFeedThatIsNotOnItsOwnAllowlistIsRefused()
    {
        var feed = new DiscoveryFeedOptions { Name = "Local", Url = "http://127.0.0.1:8099/feed.xml", AllowedHosts = ["elsewhere.invalid"] };

        var failure = Assert.Throws<InvalidOperationException>(() => feed.EnsureAllowed());

        Assert.Contains("127.0.0.1", failure.Message);
        Assert.Contains("AllowedHosts", failure.Message);
    }

    [Fact]
    public void ARedirectTargetOutsideTheAllowlistIsRefusedByHost()
    {
        var feed = new DiscoveryFeedOptions { Name = "Local", Url = "http://127.0.0.1:8099/feed.xml", AllowedHosts = ["127.0.0.1"] };

        Assert.Throws<InvalidOperationException>(() => feed.EnsureHostAllowed(
            new Uri("https://elsewhere.invalid/feed.xml"), "which is not in its own allowlist."));
        feed.EnsureHostAllowed(new Uri("http://127.0.0.1:8099/mirror.xml"), "which is not in its own allowlist.");
    }

    [Fact]
    public void ARedirectToANonHttpSchemeIsRefused()
    {
        var feed = new DiscoveryFeedOptions { Name = "Local", Url = "http://127.0.0.1:8099/feed.xml", AllowedHosts = ["127.0.0.1"] };

        var failure = Assert.Throws<InvalidOperationException>(
            () => feed.EnsureHostAllowed(new Uri("file:///etc/passwd"), "which is not in its own allowlist."));

        Assert.Contains("http", failure.Message);
    }
}
