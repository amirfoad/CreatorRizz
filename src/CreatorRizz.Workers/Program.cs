using CreatorRizz.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddCreatorRizzInfrastructure(builder.Configuration);
builder.Services.AddHostedService<HeartbeatWorker>();
await builder.Build().RunAsync();

internal sealed class HeartbeatWorker(ILogger<HeartbeatWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("CreatorRizz worker is ready for queued jobs.");
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
