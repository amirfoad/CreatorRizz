using Shorts.Infrastructure;
using Shorts.Api;
using Shorts.Domain;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddShortsInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<ProductionStore>();
builder.Services.AddSingleton<CandidateStore>();
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
app.MapHealthChecks("/health");
app.MapGet("/candidates", (CandidateStore store) => Results.Ok(store.List()));
app.MapPost("/candidates", (CreateCandidateRequest request, CandidateStore store) =>
    ExecuteWithResult(() => store.Add(request), candidate => Results.Created($"/candidates/{candidate.Id}", candidate)));
app.MapPost("/candidates/{id:guid}/sources", (Guid id, CreateSourceRequest request, CandidateStore store) =>
    Execute(() => store.AddSource(id, request)));
app.MapGet("/candidates/{id:guid}/sources", (Guid id, CandidateStore store) =>
    store.TryGet(id, out _) ? Results.Ok(store.GetSources(id)) : Results.NotFound());
app.MapPost("/candidates/{id:guid}/research-pack", (Guid id, CreateResearchPackRequest request, CandidateStore store) =>
    ExecuteWithResult(() => store.CreateResearchPack(id, request), Results.Ok));
app.MapGet("/candidates/{id:guid}/research-pack", (Guid id, CandidateStore store) =>
    store.TryGetResearchPack(id, out var pack) && pack is not null ? Results.Ok(pack) : Results.NotFound());
app.MapPost("/productions", (CreateProductionRequest request, ProductionStore store) =>
{
    var production = store.Create(request.CandidateId);
    return Results.Created($"/productions/{production.Id}", new ProductionResponse(production.Id, production.State, production.Version));
});
app.MapPost("/productions/{id:guid}/scripts/draft-from-research", (Guid id, ProductionStore productionStore, CandidateStore candidateStore) =>
{
    if (!productionStore.TryGet(id, out var production) || production is null) return Results.NotFound();
    if (!candidateStore.TryGet(production.TopicCandidateId, out var candidate) || candidate is null) return Results.NotFound();
    if (!candidateStore.TryGetResearchPack(candidate.Id, out var researchPack) || researchPack is null)
        return Results.Conflict(new { error = "Research pack is required before drafting a script." });
    return ExecuteWithResult(
        () => ScriptDraftComposer.Compose(candidate, researchPack, candidateStore.GetSources(candidate.Id)),
        draft => Results.Ok(productionStore.AddScript(id, draft.Body, draft.ClaimMapJson)));
});
app.MapGet("/productions/{id:guid}", (Guid id, ProductionStore store) =>
    store.TryGet(id, out var production) && production is not null
        ? Results.Ok(new ProductionResponse(production.Id, production.State, production.Version))
        : Results.NotFound());
app.MapGet("/productions/{id:guid}/assets", (Guid id, ProductionStore store) =>
    store.TryGet(id, out _) ? Results.Ok(store.GetAssets(id)) : Results.NotFound());
app.MapGet("/productions/{id:guid}/scripts", (Guid id, ProductionStore store) =>
    store.TryGet(id, out _) ? Results.Ok(store.GetScripts(id)) : Results.NotFound());
app.MapPost("/productions/{id:guid}/scripts", (Guid id, CreateScriptRequest request, ProductionStore store) =>
    ExecuteWithResult(() => store.AddScript(id, request.Body, request.ClaimMapJson), Results.Ok));
app.MapPost("/productions/{id:guid}/tts", async (Guid id, QueueTtsRequest request, ProductionStore store, IBackgroundJobQueue queue, CancellationToken cancellationToken) =>
{
    if (!store.TryGet(id, out var production) || production is null) return Results.NotFound();
    if (production.State != ProductionState.ScriptApproved) return Results.Conflict(new { error = "Script approval is required before TTS." });
    var script = store.GetScripts(id).LastOrDefault();
    if (script is null) return Results.Conflict(new { error = "A script is required before TTS." });
    var payload = new TextToSpeechRequest(id, script.Body, request.VoiceId, request.Speed);
    if (payload.Speed is < 0.5m or > 2m) return Results.BadRequest(new { error = "Speech speed must be between 0.5 and 2.0." });
    await queue.EnqueueAsync("tts", JsonSerializer.Serialize(new { payload.ProductionId, payload.Text, voiceId = payload.EffectiveVoiceId, payload.Speed }), cancellationToken);
    return Results.Accepted($"/productions/{id}", new { voiceId = payload.EffectiveVoiceId, status = "queued" });
});
app.MapPost("/productions/{id:guid}/render", async (Guid id, RenderManifest manifest, ProductionStore store, IBackgroundJobQueue queue, CancellationToken cancellationToken) =>
{
    try
    {
        store.BeginRendering(id, manifest);
        await queue.EnqueueAsync("render", JsonSerializer.Serialize(manifest), cancellationToken);
        return Results.Accepted($"/productions/{id}", new { status = "queued" });
    }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (WorkflowRuleViolation exception) { return Results.Conflict(new { error = exception.Message }); }
});
app.MapGet("/productions/{id:guid}/audit-events", (Guid id, ProductionStore store) =>
    store.TryGet(id, out _) ? Results.Ok(store.GetAuditEvents(id)) : Results.NotFound());
app.MapPost("/productions/{id:guid}/assets", (Guid id, AttachAssetRequest request, ProductionStore store) =>
    Execute(() => store.AttachAsset(id, new Asset
    {
        ObjectKey = request.ObjectKey,
        Type = request.Type,
        SourceUrl = request.SourceUrl,
        RightsStatus = request.RightsStatus,
        LicenseEvidence = request.LicenseEvidence,
        Checksum = request.Checksum
    })));
app.MapPost("/productions/{id:guid}/reviews/{kind}/submit", (Guid id, string kind, ProductionStore store) =>
    Execute(() => store.Submit(id, ParseKind(kind))));
app.MapPost("/productions/{id:guid}/reviews/{kind}", (Guid id, string kind, ReviewRequest request, HttpContext context, ProductionStore store) =>
{
    var role = context.Request.Headers["X-Role"].ToString();
    if (!string.Equals(role, "Reviewer", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
    return Execute(() => store.Decide(id, ParseKind(kind), request.Outcome, request.ReviewerId, request.Notes));
});
app.Run();

static ReviewKind ParseKind(string value) => Enum.TryParse<ReviewKind>(value, true, out var kind)
    ? kind
    : throw new WorkflowRuleViolation("Unknown review kind.");
static IResult Execute(Action action)
{
    try { action(); return Results.NoContent(); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (WorkflowRuleViolation exception) { return Results.Conflict(new { error = exception.Message }); }
}
static IResult ExecuteWithResult<T>(Func<T> action, Func<T, IResult> success)
{
    try { return success(action()); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { error = exception.Message }); }
}

public sealed record CreateCandidateRequest(string CanonicalUrl, string Title, string? Creator, DateTimeOffset PublishedAt, ViralSignals Signals);
public sealed record CreateSourceRequest(string Url, string Publisher, string? Excerpt, int ReliabilityScore);
public sealed record CreateResearchPackRequest(string Summary, string FactsJson, string UncertaintyJson);
public sealed record CreateProductionRequest(Guid CandidateId);
public sealed record CreateScriptRequest(string Body, string ClaimMapJson);
public sealed record QueueTtsRequest(string? VoiceId, decimal Speed = 1m);
public sealed record AttachAssetRequest(string ObjectKey, string Type, string? SourceUrl, RightsStatus RightsStatus, string? LicenseEvidence, string? Checksum);
public sealed record ProductionResponse(Guid Id, ProductionState State, int Version);
public sealed record ReviewRequest(ReviewOutcome Outcome, string ReviewerId, string? Notes);

public partial class Program;
