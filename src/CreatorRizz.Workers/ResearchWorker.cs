using CreatorRizz.Application;
using CreatorRizz.Domain;
using CreatorRizz.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace CreatorRizz.Workers;

/// <summary>
/// Builds a research pack for every discovered candidate that has enough usable sources. A candidate
/// without them is skipped, not forced through, so thin evidence never becomes a reviewable script.
/// </summary>
internal sealed class ResearchWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DiscoveryOptions> options,
    TimeProvider clock,
    ILogger<ResearchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            using (var scope = scopeFactory.CreateScope())
            {
                var workflow = scope.ServiceProvider.GetRequiredService<CreatorRizzWorkflow>();
                foreach (var candidate in workflow.ListCandidates())
                {
                    try
                    {
                        var pack = workflow.BuildResearchPackFromSources(candidate.Id);
                        if (pack is not null)
                            logger.LogInformation("Built a research pack for candidate {CandidateId} from its registered sources.", candidate.Id);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        logger.LogDebug(exception, "Candidate {CandidateId} does not have enough usable sources yet.", candidate.Id);
                    }
                }
            }

            await Task.Delay(interval, clock, stoppingToken);
        }
    }
}
