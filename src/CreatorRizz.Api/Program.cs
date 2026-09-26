using CreatorRizz.Api;
using CreatorRizz.Api.Authentication;
using CreatorRizz.Api.Endpoints;
using CreatorRizz.Api.Middleware;
using CreatorRizz.Api.OpenApi;
using CreatorRizz.Application;
using CreatorRizz.Infrastructure;
using CreatorRizz.Infrastructure.DependencyInjection;
using CreatorRizz.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCreatorRizzInfrastructure(builder.Configuration);
builder.Services.AddScoped<CreatorRizzWorkflow>();
builder.Services.AddCreatorRizzSwagger();

builder.Services.AddCreatorRizzAuthentication(builder.Configuration);

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
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
// After authentication, so a caller without a token is told 401 rather than being walked through the
// state machine's preconditions. The version gate is not a way to probe an endpoint you may not call.
app.UseMiddleware<ProductionVersionPreconditionMiddleware>();
app.UseCreatorRizzSwagger();
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
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
