using CreatorRizz.Application;
using CreatorRizz.Infrastructure;
using CreatorRizz.Infrastructure.DependencyInjection;
using CreatorRizz.Workers;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddCreatorRizzInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CreatorRizz.Application.CreatorRizzWorkflow>();
builder.Services.AddHostedService<DiscoveryWorker>();
builder.Services.AddHostedService<ResearchWorker>();
await builder.Build().RunAsync();
