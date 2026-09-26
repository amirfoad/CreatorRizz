using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CreatorRizz.Infrastructure.Persistence;
using CreatorRizz.Infrastructure.Jobs;
using CreatorRizz.Infrastructure.Configuration;
using CreatorRizz.Infrastructure.Discovery;
using CreatorRizz.Infrastructure.Scripting;
using CreatorRizz.Infrastructure.Storage;
using CreatorRizz.Infrastructure.Tts;
using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using Microsoft.EntityFrameworkCore;

namespace CreatorRizz.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public const string DiscoveryHttpClientName = "discovery";
    public static IServiceCollection AddCreatorRizzInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ExternalServicesOptions>()
            .Bind(configuration.GetSection(ExternalServicesOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options =>
                !string.IsNullOrWhiteSpace(options.DatabaseConnectionString) &&
                !string.IsNullOrWhiteSpace(options.RedisConnectionString) &&
                !string.IsNullOrWhiteSpace(options.ObjectStorageEndpoint),
                "External service endpoints must be configured.")
            .ValidateOnStart();
        services.AddOptions<DiscoveryOptions>()
            .Bind(configuration.GetSection(DiscoveryOptions.SectionName))
            .Validate(options => options.PollIntervalSeconds > 0, "Discovery poll interval must be positive.")
            .ValidateOnStart();

        services.AddDbContext<CreatorRizzDbContext>(options =>
            options.UseNpgsql(configuration.GetSection(ExternalServicesOptions.SectionName).Get<ExternalServicesOptions>()!.DatabaseConnectionString));

        // Read through the root IConfiguration, not the bound options. Only this path rejects a
        // misspelled weight name; bound options would keep the defaults and score every candidate with
        // weights the operator never wrote.
        var viralScoreWeights = configuration.ToWeights();
        viralScoreWeights.EnsureValid();
        services.AddSingleton(viralScoreWeights);
        services.AddScoped<ICandidateRepository>(provider => new PostgresCandidateRepository(
            provider.GetRequiredService<CreatorRizzDbContext>(),
            provider.GetRequiredService<ViralScoreWeights>()));
        services.AddScoped<IProductionRepository, PostgresProductionRepository>();
        services.AddScriptGenerator(configuration);
        services.AddSingleton<ITextToSpeechProvider, DisabledTextToSpeechProvider>();
        // Scoped, and sharing the DbContext scope with the repository, because a render's state change
        // and the record of the render commit in one transaction.
        services.AddScoped<IWorkflowTransaction>(provider => new PostgresWorkflowTransaction(
            provider.GetRequiredService<CreatorRizzDbContext>()));
        services.AddScoped<IProductionJobQueue, PostgresProductionJobQueue>();
        services.AddSingleton<IObjectStorage>(provider => new LocalObjectStorage(
            Path.Combine(AppContext.BaseDirectory, "storage")));

        // Redirects are walked by RssDiscoverySource so every hop is allowlist checked, which only works
        // if the handler does not follow them on its own.
        services.AddHttpClient(DiscoveryHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        foreach (var feed in configuration.GetSection(DiscoveryOptions.SectionName).Get<DiscoveryOptions>()?.Feeds ?? [])
        {
            services.AddSingleton<IDiscoverySource>(provider => new RssDiscoverySource(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient(DiscoveryHttpClientName),
                feed,
                provider.GetRequiredService<TimeProvider>()));
        }

        return services;
    }
}
