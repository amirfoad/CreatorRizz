using CreatorRizz.Api.Contracts;
using CreatorRizz.Api.Results;
using CreatorRizz.Application;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace CreatorRizz.Api.Endpoints;

public static class CandidateEndpoints
{
    public static IEndpointRouteBuilder MapCandidateEndpoints(this IEndpointRouteBuilder app)
    {
        var candidates = app.MapGroup("/candidates").WithTags("Candidates");

        candidates.MapGet("", (CreatorRizzWorkflow workflow) => HttpResults.Ok(workflow.ListCandidates()));
        candidates.MapPost("", (CreateCandidateRequest request, CreatorRizzWorkflow workflow) =>
            ApiResults.ExecuteWithResult(
                () => workflow.CreateCandidate(request.CanonicalUrl, request.Title, request.Creator, request.PublishedAt, request.Signals),
                candidate => HttpResults.Created($"/candidates/{candidate.Id}", candidate)));
        candidates.MapPost("/{id:guid}/sources", (Guid id, CreateSourceRequest request, CreatorRizzWorkflow workflow) =>
            ApiResults.Execute(() => workflow.AddSource(id, request.Url, request.Publisher, request.Excerpt, request.ReliabilityScore)));
        candidates.MapGet("/{id:guid}/sources", (Guid id, CreatorRizzWorkflow workflow) =>
            workflow.CandidateExists(id) ? HttpResults.Ok(workflow.GetSources(id)) : HttpResults.NotFound());
        candidates.MapPost("/{id:guid}/research-pack", (Guid id, CreateResearchPackRequest request, CreatorRizzWorkflow workflow) =>
            ApiResults.ExecuteWithResult(() => workflow.CreateResearchPack(id, request.Summary, request.FactsJson, request.UncertaintyJson), HttpResults.Ok));
        candidates.MapGet("/{id:guid}/research-pack", (Guid id, CreatorRizzWorkflow workflow) =>
            workflow.TryGetResearchPack(id, out var pack) && pack is not null ? HttpResults.Ok(pack) : HttpResults.NotFound());

        // Lets a feed be replayed by hand and shows what discovery would do with a story, without
        // waiting for the next poll. Repeats return the stored candidate instead of failing.
        candidates.MapPost("/discovery", (RegisterDiscoveredTopicRequest request, CreatorRizzWorkflow workflow) =>
            ApiResults.ExecuteWithResult(() => workflow.RegisterDiscovered(request.ToDiscoveredTopic()), HttpResults.Ok));

        return app;
    }
}
