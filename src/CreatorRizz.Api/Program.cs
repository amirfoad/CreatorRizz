using CreatorRizz.Api;
using CreatorRizz.Api.Endpoints;
using CreatorRizz.Api.Middleware;
using CreatorRizz.Api.OpenApi;
using CreatorRizz.Application;
using CreatorRizz.Infrastructure;
using CreatorRizz.Infrastructure.DependencyInjection;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCreatorRizzInfrastructure(builder.Configuration);
builder.Services.AddScoped<CreatorRizzWorkflow>();
builder.Services.AddCreatorRizzSwagger();
builder.Services.AddHealthChecks().AddCheck<ConfigurationHealthCheck>("configuration", tags: ["ready"]);
builder.Services.AddRateLimiter(options => options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
    RateLimitPartition.GetFixedWindowLimiter("api", _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = 60,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    })));

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseRateLimiter();
app.UseCreatorRizzSwagger();
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapCreatorRizzEndpoints();
app.Run();

public partial class Program;
