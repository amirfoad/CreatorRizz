using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shorts.Infrastructure.Persistence;

namespace Shorts.Infrastructure;

public sealed class ExternalServicesOptions
{
    public const string SectionName = "ExternalServices";
    public required string DatabaseConnectionString { get; init; }
    public required string RedisConnectionString { get; init; }
    public required string ObjectStorageEndpoint { get; init; }
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShortsInfrastructure(this IServiceCollection services, IConfiguration configuration)
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
        services.AddSingleton<IBackgroundJobQueue, InMemoryBackgroundJobQueue>();
        services.AddSingleton<InMemoryCandidateStore>();
        services.AddSingleton<InMemoryProductionStore>();
        return services;
    }
}
