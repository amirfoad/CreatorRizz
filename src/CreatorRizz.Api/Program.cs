using CreatorRizz.Api;
using CreatorRizz.Api.Endpoints;
using CreatorRizz.Api.Middleware;
using CreatorRizz.Api.OpenApi;
using CreatorRizz.Application;
using CreatorRizz.Infrastructure;
using CreatorRizz.Infrastructure.DependencyInjection;
using CreatorRizz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCreatorRizzInfrastructure(builder.Configuration);
builder.Services.AddScoped<CreatorRizzWorkflow>();
builder.Services.AddCreatorRizzSwagger();

// Rights and review states travel as their exact names. Numeric enum values would let a client
// silently ask for the most permissive status, for example RightsStatus.Owned, by sending 0.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
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

await ApplyCreatorRizzMigrationsAsync(app);

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ProductionVersionPreconditionMiddleware>();
app.UseRateLimiter();
app.UseCreatorRizzSwagger();
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapCreatorRizzEndpoints();
app.Run();

/// <summary>
/// Brings the PostgreSQL schema up to the EF Core migrations before the API accepts traffic. The
/// API owns schema changes, so a migration failure must stop startup rather than serve errors.
/// </summary>
static async Task ApplyCreatorRizzMigrationsAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<CreatorRizzDbContext>();
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CreatorRizz.Migrations");
    logger.LogInformation("Applying PostgreSQL migrations.");
    await database.Database.MigrateAsync();
    logger.LogInformation("PostgreSQL schema is up to date.");
}

public partial class Program;
