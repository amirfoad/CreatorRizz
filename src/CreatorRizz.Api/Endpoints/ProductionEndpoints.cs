using CreatorRizz.Api.Authentication;
using CreatorRizz.Api.Contracts;
using CreatorRizz.Api.Middleware;
using CreatorRizz.Api.Results;
using CreatorRizz.Application;
using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using CreatorRizz.Infrastructure.Storage;
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
        productions.MapGet("/{id:guid}/script-generations", GetScriptGenerations);
        productions.MapPost("/{id:guid}/scripts", CreateScript);
        productions.MapPost("/{id:guid}/tts", QueueTts);
        productions.MapPost("/{id:guid}/render", QueueRender);
        productions.MapGet("/{id:guid}/audit-events", GetAuditEvents);
        productions.MapPost("/{id:guid}/assets", async (Guid id, AttachAssetRequest request, HttpContext context, CreatorRizzWorkflow workflow, IObjectStorage storage) =>
        {
            try
            {
                using var stream = new MemoryStream(request.Data);
                var stored = await storage.PutAsync(stream, context.RequestAborted);
                ApiResults.Execute(() => workflow.AttachAsset(id, ProductionVersionPreconditionMiddleware.ReadExpectedVersion(context), stored, request.Type, request.SourceUrl, request.RightsStatus, request.NarrativePurpose, request.LicenseEvidence));
                return HttpResults.Ok();
            }
            catch (KeyNotFoundException) { return HttpResults.NotFound(); }
            catch (ArgumentException exception) { return HttpResults.BadRequest(new { error = exception.Message }); }
            catch (WorkflowRuleViolation exception) { return HttpResults.Conflict(new { error = exception.Message }); }
            catch (InvalidOperationException exception) { return HttpResults.Conflict(new { error = exception.Message }); }
        });
        productions.MapPost("/{id:guid}/assets/prepare", BeginAssetPreparation);
        productions.MapPost("/{id:guid}/assets/ready", DeclareAssetsReady);
        productions.MapPost("/{id:guid}/render/complete", CompleteRendering);
        productions.MapPost("/{id:guid}/reviews/{kind}/submit", SubmitReview);
        productions.MapPost("/{id:guid}/reviews/{kind}", DecideReview).RequireAuthorization(ReviewerAccess.PolicyName);

        return app;
    }

    private static IResult Create(CreateProductionRequest request, HttpContext context, CreatorRizzWorkflow workflow)
    {
        var production = ApiResults.ExecuteWithResult(
            () => workflow.CreateProduction(request.CandidateId),
            created => HttpResults.Created($"/productions/{created.Id}", new ProductionResponse(created.Id, created.State, created.Version)));
        return production;
    }

    private static async Task<IResult> DraftFromResearch(Guid id, HttpContext context, CreatorRizzWorkflow workflow, CancellationToken cancellationToken)
    {
        try
        {
            var script = await workflow.DraftScriptFromResearchAsync(id, ProductionVersionPreconditionMiddleware.ReadExpectedVersion(context), cancellationToken);
            return HttpResults.Ok(script);
        }
        catch (KeyNotFoundException) { return HttpResults.NotFound(); }
        catch (ArgumentException exception) { return HttpResults.BadRequest(new { error = exception.Message }); }
        catch (WorkflowRuleViolation exception) { return HttpResults.Conflict(new { error = exception.Message }); }
        // A provider failure is upstream of this service, so it is not reported as a refused draft.
        catch (ScriptGenerationFailedException exception) { return ApiResults.ProviderFailure(exception); }
        catch (InvalidOperationException exception) { return HttpResults.Conflict(new { error = exception.Message }); }
    }

    private static IResult Get(Guid id, HttpContext context, CreatorRizzWorkflow workflow)
    {
        if (!workflow.TryGetProduction(id, out var production) || production is null) return HttpResults.NotFound();
        ProductionVersionPreconditionMiddleware.WriteVersionHeader(context, production.Version);
        return HttpResults.Ok(new ProductionResponse(production.Id, production.State, production.Version));
    }

    private static IResult GetAssets(Guid id, CreatorRizzWorkflow workflow) =>
        workflow.TryGetProduction(id, out _) ? HttpResults.Ok(workflow.GetAssets(id)) : HttpResults.NotFound();

    private static IResult GetScripts(Guid id, CreatorRizzWorkflow workflow) =>
        workflow.TryGetProduction(id, out _) ? HttpResults.Ok(workflow.GetScripts(id)) : HttpResults.NotFound();

    private static IResult GetScriptGenerations(Guid id, CreatorRizzWorkflow workflow) =>
        workflow.TryGetProduction(id, out _) ? HttpResults.Ok(workflow.GetScriptGenerations(id)) : HttpResults.NotFound();

    private static IResult CreateScript(Guid id, CreateScriptRequest request, HttpContext context, CreatorRizzWorkflow workflow) =>
        ApiResults.ExecuteWithResult(
            () => workflow.AddScript(id, ProductionVersionPreconditionMiddleware.ReadExpectedVersion(context), request.Body, request.ClaimMapJson),
            HttpResults.Ok);

    private static async Task<IResult> QueueTts(Guid id, QueueTtsRequest request, CreatorRizzWorkflow workflow, CancellationToken cancellationToken)
    {
        try { await workflow.QueueTextToSpeechAsync(id, request.VoiceId, request.Speed, cancellationToken); return HttpResults.Accepted($"/productions/{id}", new { status = "queued" }); }
        catch (KeyNotFoundException) { return HttpResults.NotFound(); }
        catch (ArgumentOutOfRangeException exception) { return HttpResults.BadRequest(new { error = exception.Message }); }
        catch (WorkflowRuleViolation exception) { return HttpResults.Conflict(new { error = exception.Message }); }
    }

    private static async Task<IResult> QueueRender(Guid id, RenderManifest manifest, HttpContext context, CreatorRizzWorkflow workflow, CancellationToken cancellationToken)
    {
        try
        {
            await workflow.QueueRenderAsync(id, ProductionVersionPreconditionMiddleware.ReadExpectedVersion(context), manifest, cancellationToken);
            return HttpResults.Accepted($"/productions/{id}", new { status = "queued" });
        }
        catch (KeyNotFoundException) { return HttpResults.NotFound(); }
        catch (ProductionVersionConflict exception) { return ApiResults.VersionConflict(exception); }
        catch (WorkflowRuleViolation exception) { return HttpResults.Conflict(new { error = exception.Message }); }
    }

    private static IResult GetAuditEvents(Guid id, CreatorRizzWorkflow workflow) =>
        workflow.TryGetProduction(id, out _) ? HttpResults.Ok(workflow.GetAuditEvents(id)) : HttpResults.NotFound();

    private static IResult BeginAssetPreparation(Guid id, HttpContext context, CreatorRizzWorkflow workflow) =>
        ApiResults.Execute(() => workflow.BeginAssetPreparation(id, ProductionVersionPreconditionMiddleware.ReadExpectedVersion(context)));

    private static IResult DeclareAssetsReady(Guid id, HttpContext context, CreatorRizzWorkflow workflow) =>
        ApiResults.Execute(() => workflow.DeclareAssetsReady(id, ProductionVersionPreconditionMiddleware.ReadExpectedVersion(context)));

    private static IResult CompleteRendering(Guid id, HttpContext context, CreatorRizzWorkflow workflow) =>
        ApiResults.Execute(() => workflow.CompleteRendering(id, ProductionVersionPreconditionMiddleware.ReadExpectedVersion(context)));

    private static IResult SubmitReview(Guid id, string kind, HttpContext context, CreatorRizzWorkflow workflow) =>
        ApiResults.Execute(() => workflow.SubmitReview(id, ProductionVersionPreconditionMiddleware.ReadExpectedVersion(context), ApiResults.ParseReviewKind(kind)));

    private static IResult DecideReview(Guid id, string kind, ReviewRequest request, HttpContext context, CreatorRizzWorkflow workflow) =>
        ApiResults.Execute(() => workflow.DecideReview(id, ProductionVersionPreconditionMiddleware.ReadExpectedVersion(context),
            ApiResults.ParseReviewKind(kind), request.Outcome, ReviewerAccess.ReadId(context.User), request.Notes));
}
