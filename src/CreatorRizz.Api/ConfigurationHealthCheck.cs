using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using CreatorRizz.Infrastructure;
using CreatorRizz.Infrastructure.Configuration;

namespace CreatorRizz.Api;

public sealed class ConfigurationHealthCheck(IOptions<ExternalServicesOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var value = options.Value;
        var ready = !string.IsNullOrWhiteSpace(value.DatabaseConnectionString)
            && !string.IsNullOrWhiteSpace(value.RedisConnectionString)
            && !string.IsNullOrWhiteSpace(value.ObjectStorageEndpoint);
        return Task.FromResult(ready ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Required external-service configuration is missing."));
    }
}
