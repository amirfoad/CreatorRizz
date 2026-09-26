using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CreatorRizz.Infrastructure.Persistence;
using CreatorRizz.Infrastructure.BackgroundJobs;
using CreatorRizz.Application.Abstractions;

namespace CreatorRizz.Infrastructure;

public sealed class ExternalServicesOptions
{
    public const string SectionName = "ExternalServices";
    public required string DatabaseConnectionString { get; init; }
    public required string RedisConnectionString { get; init; }
    public required string ObjectStorageEndpoint { get; init; }
}

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
        services.AddSingleton<IBackgroundJobQueue, InMemoryBackgroundJobQueue>();
        services.AddSingleton<InMemoryCandidateStore>();
        services.AddSingleton<InMemoryProductionStore>();
        services.AddSingleton<ICandidateRepository>(provider => provider.GetRequiredService<InMemoryCandidateStore>());
        services.AddSingleton<IProductionRepository>(provider => provider.GetRequiredService<InMemoryProductionStore>());
        services.AddSingleton<IProductionJobQueue, InMemoryProductionJobQueue>();
        return services;
    }
}
