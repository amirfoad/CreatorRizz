using CreatorRizz.Api.Contracts;
using CreatorRizz.Api.Results;
using CreatorRizz.Application;
using CreatorRizz.Domain;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace CreatorRizz.Api.Endpoints;

public static class ProductionEndpoints
{
    public static IEndpointRouteBuilder MapProductionEndpoints(this IEndpointRouteBuilder app)
    {
        var productions = app.MapGroup("/productions").WithTags("Productions");

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

    private static IResult Create(CreateProductionRequest request, CreatorRizzWorkflow workflow)
    {
        return ApiResults.ExecuteWithResult(
            () => workflow.CreateProduction(request.CandidateId),
            production => HttpResults.Created($"/productions/{production.Id}", new ProductionResponse(production.Id, production.State, production.Version)));
    }

    private static IResult DraftFromResearch(Guid id, CreatorRizzWorkflow workflow) =>
        ApiResults.ExecuteWithResult(() => workflow.DraftScriptFromResearch(id), HttpResults.Ok);

    private static IResult Get(Guid id, CreatorRizzWorkflow workflow) =>
        workflow.TryGetProduction(id, out var production) && production is not null
            ? HttpResults.Ok(new ProductionResponse(production.Id, production.State, production.Version))
            : HttpResults.NotFound();

    private static IResult GetAssets(Guid id, CreatorRizzWorkflow workflow) =>
        workflow.TryGetProduction(id, out _) ? HttpResults.Ok(workflow.GetAssets(id)) : HttpResults.NotFound();

    private static IResult GetScripts(Guid id, CreatorRizzWorkflow workflow) =>
        workflow.TryGetProduction(id, out _) ? HttpResults.Ok(workflow.GetScripts(id)) : HttpResults.NotFound();

    private static IResult CreateScript(Guid id, CreateScriptRequest request, CreatorRizzWorkflow workflow) =>
        ApiResults.ExecuteWithResult(() => workflow.AddScript(id, request.Body, request.ClaimMapJson), HttpResults.Ok);

    private static async Task<IResult> QueueTts(Guid id, QueueTtsRequest request, CreatorRizzWorkflow workflow, CancellationToken cancellationToken)
    {
        try { await workflow.QueueTextToSpeechAsync(id, request.VoiceId, request.Speed, cancellationToken); return HttpResults.Accepted($"/productions/{id}", new { status = "queued" }); }
        catch (KeyNotFoundException) { return HttpResults.NotFound(); }
        catch (ArgumentOutOfRangeException exception) { return HttpResults.BadRequest(new { error = exception.Message }); }
        catch (WorkflowRuleViolation exception) { return HttpResults.Conflict(new { error = exception.Message }); }
    }

    private static async Task<IResult> QueueRender(Guid id, RenderManifest manifest, CreatorRizzWorkflow workflow, CancellationToken cancellationToken)
    {
        try
        {
            await workflow.QueueRenderAsync(id, manifest, cancellationToken);
            return HttpResults.Accepted($"/productions/{id}", new { status = "queued" });
        }
        catch (KeyNotFoundException) { return HttpResults.NotFound(); }
        catch (WorkflowRuleViolation exception) { return HttpResults.Conflict(new { error = exception.Message }); }
    }

    private static IResult GetAuditEvents(Guid id, CreatorRizzWorkflow workflow) =>
        workflow.TryGetProduction(id, out _) ? HttpResults.Ok(workflow.GetAuditEvents(id)) : HttpResults.NotFound();

    private static IResult AttachAsset(Guid id, AttachAssetRequest request, CreatorRizzWorkflow workflow) =>
        ApiResults.Execute(() => workflow.AttachAsset(id, new Asset
        {
            ObjectKey = request.ObjectKey,
            Type = request.Type,
            SourceUrl = request.SourceUrl,
            RightsStatus = request.RightsStatus,
            LicenseEvidence = request.LicenseEvidence,
            Checksum = request.Checksum
        }));

    private static IResult SubmitReview(Guid id, string kind, CreatorRizzWorkflow workflow) =>
        ApiResults.Execute(() => workflow.SubmitReview(id, ApiResults.ParseReviewKind(kind)));

    private static IResult DecideReview(Guid id, string kind, ReviewRequest request, HttpContext context, CreatorRizzWorkflow workflow)
    {
        var role = context.Request.Headers["X-Role"].ToString();
        if (!string.Equals(role, "Reviewer", StringComparison.OrdinalIgnoreCase)) return HttpResults.Forbid();
        return ApiResults.Execute(() => workflow.DecideReview(id, ApiResults.ParseReviewKind(kind), request.Outcome, request.ReviewerId, request.Notes));
    }
}
