using System.Text.Json;
using Shorts.Api.Contracts;
using Shorts.Api.Results;
using Shorts.Domain;
using Shorts.Infrastructure;
using Shorts.Infrastructure.Persistence;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace Shorts.Api.Endpoints;

public static class ProductionEndpoints
{
    public static IEndpointRouteBuilder MapProductionEndpoints(this IEndpointRouteBuilder app)
    {
        var productions = app.MapGroup("/productions");

        productions.MapPost("", Create);
        productions.MapPost("/{id:guid}/scripts/draft-from-research", DraftFromResearch);
        productions.MapGet("/{id:guid}", Get);
        productions.MapGet("/{id:guid}/assets", GetAssets);
        productions.MapGet("/{id:guid}/scripts", GetScripts);
        productions.MapPost("/{id:guid}/scripts", CreateScript);
        productions.MapPost("/{id:guid}/tts", QueueTts);
        productions.MapPost("/{id:guid}/render", QueueRender);
        productions.MapGet("/{id:guid}/audit-events", GetAuditEvents);
        productions.MapPost("/{id:guid}/assets", AttachAsset);
        productions.MapPost("/{id:guid}/reviews/{kind}/submit", SubmitReview);
        productions.MapPost("/{id:guid}/reviews/{kind}", DecideReview);

        return app;
    }

    private static IResult Create(CreateProductionRequest request, InMemoryProductionStore store, InMemoryCandidateStore candidateStore)
    {
        if (!candidateStore.TryGet(request.CandidateId, out _)) return HttpResults.NotFound();
        var production = store.Create(request.CandidateId);
        return HttpResults.Created($"/productions/{production.Id}", new ProductionResponse(production.Id, production.State, production.Version));
    }

    private static IResult DraftFromResearch(Guid id, InMemoryProductionStore productionStore, InMemoryCandidateStore candidateStore)
    {
        if (!productionStore.TryGet(id, out var production) || production is null) return HttpResults.NotFound();
        if (!candidateStore.TryGet(production.TopicCandidateId, out var candidate) || candidate is null) return HttpResults.NotFound();
        if (!candidateStore.TryGetResearchPack(candidate.Id, out var researchPack) || researchPack is null)
            return HttpResults.Conflict(new { error = "Research pack is required before drafting a script." });

        return ApiResults.ExecuteWithResult(
            () => ScriptDraftComposer.Compose(candidate, researchPack, candidateStore.GetSources(candidate.Id)),
            draft => HttpResults.Ok(productionStore.AddScript(id, draft.Body, draft.ClaimMapJson)));
    }

    private static IResult Get(Guid id, InMemoryProductionStore store) =>
        store.TryGet(id, out var production) && production is not null
            ? HttpResults.Ok(new ProductionResponse(production.Id, production.State, production.Version))
            : HttpResults.NotFound();

    private static IResult GetAssets(Guid id, InMemoryProductionStore store) =>
        store.TryGet(id, out _) ? HttpResults.Ok(store.GetAssets(id)) : HttpResults.NotFound();

    private static IResult GetScripts(Guid id, InMemoryProductionStore store) =>
        store.TryGet(id, out _) ? HttpResults.Ok(store.GetScripts(id)) : HttpResults.NotFound();

    private static IResult CreateScript(Guid id, CreateScriptRequest request, InMemoryProductionStore store) =>
        ApiResults.ExecuteWithResult(() => store.AddScript(id, request.Body, request.ClaimMapJson), HttpResults.Ok);

    private static async Task<IResult> QueueTts(Guid id, QueueTtsRequest request, InMemoryProductionStore store, IBackgroundJobQueue queue, CancellationToken cancellationToken)
    {
        if (!store.TryGet(id, out var production) || production is null) return HttpResults.NotFound();
        if (production.State != ProductionState.ScriptApproved) return HttpResults.Conflict(new { error = "Script approval is required before TTS." });
        var script = store.GetScripts(id).LastOrDefault();
        if (script is null) return HttpResults.Conflict(new { error = "A script is required before TTS." });

        var payload = new TextToSpeechRequest(id, script.Body, request.VoiceId, request.Speed);
        if (payload.Speed is < 0.5m or > 2m) return HttpResults.BadRequest(new { error = "Speech speed must be between 0.5 and 2.0." });
        await queue.EnqueueAsync("tts", JsonSerializer.Serialize(new { payload.ProductionId, payload.Text, voiceId = payload.EffectiveVoiceId, payload.Speed }), cancellationToken);
        return HttpResults.Accepted($"/productions/{id}", new { voiceId = payload.EffectiveVoiceId, status = "queued" });
    }

    private static async Task<IResult> QueueRender(Guid id, RenderManifest manifest, InMemoryProductionStore store, IBackgroundJobQueue queue, CancellationToken cancellationToken)
    {
        try
        {
            store.BeginRendering(id, manifest);
            await queue.EnqueueAsync("render", JsonSerializer.Serialize(manifest), cancellationToken);
            return HttpResults.Accepted($"/productions/{id}", new { status = "queued" });
        }
        catch (KeyNotFoundException) { return HttpResults.NotFound(); }
        catch (WorkflowRuleViolation exception) { return HttpResults.Conflict(new { error = exception.Message }); }
    }

    private static IResult GetAuditEvents(Guid id, InMemoryProductionStore store) =>
        store.TryGet(id, out _) ? HttpResults.Ok(store.GetAuditEvents(id)) : HttpResults.NotFound();

    private static IResult AttachAsset(Guid id, AttachAssetRequest request, InMemoryProductionStore store) =>
        ApiResults.Execute(() => store.AttachAsset(id, new Asset
        {
            ObjectKey = request.ObjectKey,
            Type = request.Type,
            SourceUrl = request.SourceUrl,
            RightsStatus = request.RightsStatus,
            LicenseEvidence = request.LicenseEvidence,
            Checksum = request.Checksum
        }));

    private static IResult SubmitReview(Guid id, string kind, InMemoryProductionStore store) =>
        ApiResults.Execute(() => store.Submit(id, ApiResults.ParseReviewKind(kind)));

    private static IResult DecideReview(Guid id, string kind, ReviewRequest request, HttpContext context, InMemoryProductionStore store)
    {
        var role = context.Request.Headers["X-Role"].ToString();
        if (!string.Equals(role, "Reviewer", StringComparison.OrdinalIgnoreCase)) return HttpResults.Forbid();
        return ApiResults.Execute(() => store.Decide(id, ApiResults.ParseReviewKind(kind), request.Outcome, request.ReviewerId, request.Notes));
    }
}
