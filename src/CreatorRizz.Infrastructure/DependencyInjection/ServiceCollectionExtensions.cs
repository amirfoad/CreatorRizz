using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using CreatorRizz.Infrastructure.Persistence;
using CreatorRizz.Infrastructure.Jobs;
using CreatorRizz.Infrastructure.Configuration;
using CreatorRizz.Infrastructure.Discovery;
using CreatorRizz.Infrastructure.Scripting;
using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using Microsoft.EntityFrameworkCore;

namespace CreatorRizz.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
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

        services.AddSingleton(provider => provider.GetRequiredService<IOptions<DiscoveryOptions>>().Value.Weights.ToWeights());
        services.AddScoped<ICandidateRepository>(provider => new PostgresCandidateRepository(
            provider.GetRequiredService<CreatorRizzDbContext>(),
            provider.GetRequiredService<ViralScoreWeights>()));
        services.AddScoped<IProductionRepository, PostgresProductionRepository>();
        services.AddScoped<IScriptGenerator, DisabledScriptGenerator>();
        services.AddSingleton<IBackgroundJobQueue, InMemoryBackgroundJobQueue>();
        services.AddSingleton<IProductionJobQueue, InMemoryProductionJobQueue>();

        services.AddHttpClient();
        foreach (var feed in configuration.GetSection(DiscoveryOptions.SectionName).Get<DiscoveryOptions>()?.Feeds ?? [])
        {
            services.AddSingleton<IDiscoverySource>(provider => new RssDiscoverySource(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient(),
                feed,
                provider.GetRequiredService<TimeProvider>()));
        }

        return services;
    }
}
