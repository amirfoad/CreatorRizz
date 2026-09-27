using CreatorRizz.Application;
using CreatorRizz.Infrastructure;
using CreatorRizz.Infrastructure.Configuration;
using CreatorRizz.Infrastructure.DependencyInjection;
using CreatorRizz.Infrastructure.Rendering;
using CreatorRizz.Infrastructure.Storage;
using CreatorRizz.Workers;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddCreatorRizzInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CreatorRizz.Application.CreatorRizzWorkflow>();
builder.Services.AddHostedService<DiscoveryWorker>();
builder.Services.AddHostedService<ResearchWorker>();
builder.Services.AddOptions<RenderingOptions>()
    .Bind(builder.Configuration.GetSection(RenderingOptions.SectionName))
    .Validate(options => options.PollIntervalSeconds > 0, "Render poll interval must be positive.")
    .Validate(options => options.LeaseSeconds > 0, "Render lease must be longer than zero.")
    .Validate(options => options.MaxAttempts >= 1, "A render job must be allowed at least one attempt.")
    .ValidateOnStart();

// Only the worker renders, so the executor is wired here rather than in the shared composition root.
// A path that does not exist surfaces on the first job instead of stopping every worker at startup.
builder.Services.AddSingleton<IRenderExecutor>(provider => new FfmpegRenderExecutor(
    provider.GetRequiredService<IObjectStorage>(),
    provider.GetRequiredService<IOptions<RenderingOptions>>().Value.FfmpegPath));
builder.Services.AddHostedService<RenderJobWorker>();

await builder.Build().RunAsync();
