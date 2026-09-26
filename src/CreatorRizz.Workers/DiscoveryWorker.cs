using CreatorRizz.Application;
using CreatorRizz.Application.Abstractions;
using CreatorRizz.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace CreatorRizz.Workers;

/// <summary>
/// Polls every allowlisted feed on a timer. Repeats are absorbed by the candidate repository, so an
/// unchanged feed produces no new candidates and a retried poll is a no-op.
/// </summary>
internal sealed class DiscoveryWorker(
    IEnumerable<IDiscoverySource> sources,
    IServiceScopeFactory scopeFactory,
    IOptions<DiscoveryOptions> options,
    TimeProvider clock,
    ILogger<DiscoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuredSources = sources.ToArray();
        if (configuredSources.Length == 0)
        {
            logger.LogInformation("No discovery feed is configured, so discovery stays idle.");
            return;
        }

        var interval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var source in configuredSources)
            {
                using var scope = scopeFactory.CreateScope();
                var workflow = scope.ServiceProvider.GetRequiredService<CreatorRizzWorkflow>();
                try
                {
                    var found = await workflow.DiscoverAsync(source, stoppingToken);
                    logger.LogInformation("Discovery source {Source} returned {Count} topics.", source.Name, found.Count);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // One unreachable feed must not stop the others from being polled.
                    logger.LogError(exception, "Discovery source {Source} failed.", source.Name);
                }
            }

            await Task.Delay(interval, clock, stoppingToken);
        }
    }
}
