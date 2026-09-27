using System.Text.Json;
using CreatorRizz.Application;
using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using CreatorRizz.Infrastructure.Configuration;
using CreatorRizz.Infrastructure.Rendering;
using CreatorRizz.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace CreatorRizz.Workers;

/// <summary>
/// Runs the render work that the API recorded. Until this existed the outbox was a promise nobody kept:
/// rows were written, the production sat in Rendering, and nothing ever picked the row up.
/// </summary>
/// <remarks>
/// Render jobs only. A TextToSpeech row is left in the table because there is no provider to run it and
/// nowhere to record the result, and claiming work whose output cannot be seen is worse than leaving it
/// visibly pending.
/// </remarks>
internal sealed class RenderJobWorker(
    IServiceScopeFactory scopeFactory,
    IRenderExecutor renderer,
    IObjectStorage storage,
    IOptions<RenderingOptions> options,
    ILogger<RenderJobWorker> logger) : BackgroundService
{
    private static readonly ProductionJobKind Handled = ProductionJobKind.Render;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var workerId = $"{Environment.MachineName}:{Environment.ProcessId}";
        var lease = TimeSpan.FromSeconds(settings.LeaseSeconds);
        var idle = TimeSpan.FromSeconds(settings.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var job = await ClaimAsync(workerId, lease, settings.MaxAttempts, stoppingToken);
            if (job is null)
            {
                await Task.Delay(idle, stoppingToken);
                continue;
            }
            await RunAsync(job, stoppingToken);
        }
    }

    private async Task RunAsync(ClaimedProductionJob job, CancellationToken stoppingToken)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"creatorrizz-render-{job.Id:N}.mp4");
        try
        {
            var manifest = JsonSerializer.Deserialize<RenderManifest>(job.PayloadJson)
                ?? throw new WorkflowRuleViolation("The recorded render payload is empty.");

            // A production that is no longer rendering has already had this work applied. Re-rendering it
            // would overwrite a reviewed outcome, so the row is closed rather than retried.
            if (!await IsStillRenderingAsync(job.ProductionId, stoppingToken))
            {
                logger.LogInformation(
                    "Render job {JobId} for production {ProductionId} no longer has a render in progress; closing it.",
                    job.Id, job.ProductionId);
                await CompleteAsync(job.Id, stoppingToken);
                return;
            }

            var assetObjectKeys = await ReadAssetObjectKeysAsync(job.ProductionId, stoppingToken);
            var rendered = await renderer.RenderAsync(manifest, assetObjectKeys, outputPath, stoppingToken);

            await using (var content = File.OpenRead(rendered.OutputPath))
            {
                var stored = await storage.PutAsync(content, stoppingToken);
                logger.LogInformation(
                    "Rendered production {ProductionId} to object {ObjectKey} ({Bytes} bytes).",
                    job.ProductionId, stored.ObjectKey, stored.Length);
            }

            await CompleteRenderingAsync(job.ProductionId, stoppingToken);
            await CompleteAsync(job.Id, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Render job {JobId} for production {ProductionId} failed on attempt {Attempt}.",
                job.Id, job.ProductionId, job.Attempt);
            await ReleaseAsync(job.Id, exception, stoppingToken);
        }
        finally
        {
            TryDelete(outputPath);
        }
    }

    private Task<ClaimedProductionJob?> ClaimAsync(string workerId, TimeSpan lease, int maxAttempts, CancellationToken cancellationToken) =>
        InScope(services => services.GetRequiredService<IProductionJobDispatcher>()
            .ClaimNextAsync(Handled, workerId, lease, maxAttempts, cancellationToken));

    private Task CompleteAsync(Guid jobId, CancellationToken cancellationToken) =>
        InScope(services => services.GetRequiredService<IProductionJobDispatcher>().CompleteAsync(jobId, cancellationToken));

    private Task ReleaseAsync(Guid jobId, Exception failure, CancellationToken cancellationToken) =>
        InScope(services => services.GetRequiredService<IProductionJobDispatcher>()
            .ReleaseAsync(jobId, failure.Message, cancellationToken));

    private Task<bool> IsStillRenderingAsync(Guid productionId, CancellationToken cancellationToken) =>
        InScope(services =>
        {
            var workflow = services.GetRequiredService<CreatorRizzWorkflow>();
            return Task.FromResult(
                workflow.TryGetProduction(productionId, out var production) &&
                production is not null &&
                production.State == ProductionState.Rendering);
        });

    private Task<IReadOnlyDictionary<Guid, string>> ReadAssetObjectKeysAsync(Guid productionId, CancellationToken cancellationToken) =>
        InScope(services =>
        {
            var workflow = services.GetRequiredService<CreatorRizzWorkflow>();
            IReadOnlyDictionary<Guid, string> keys = workflow.GetAssets(productionId)
                .ToDictionary(asset => asset.Id, asset => asset.ObjectKey);
            return Task.FromResult(keys);
        });

    /// <summary>
    /// Reads the production's current version and hands that to the workflow, rather than deriving the
    /// version from the one the caller queued at. "Queued version plus one" is the guess the If-Match
    /// rule exists to keep out of this codebase, and a guess here would be a silent way to skip the guard.
    /// </summary>
    private Task CompleteRenderingAsync(Guid productionId, CancellationToken cancellationToken) =>
        InScope(services =>
        {
            var workflow = services.GetRequiredService<CreatorRizzWorkflow>();
            if (!workflow.TryGetProduction(productionId, out var production) || production is null)
                throw new KeyNotFoundException("Production was not found.");
            workflow.CompleteRendering(productionId, production.Version);
            return Task.CompletedTask;
        });

    /// <summary>
    /// One scope per unit of work. The DbContext is scoped, and a render holds no database state of its
    /// own, so a connection is not kept open for the length of an encode.
    /// </summary>
    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = scopeFactory.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private async Task InScope(Func<IServiceProvider, Task> work)
    {
        using var scope = scopeFactory.CreateScope();
        await work(scope.ServiceProvider);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
