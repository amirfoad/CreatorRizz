using Shorts.Api.Contracts;
using Shorts.Api.Results;
using Shorts.Infrastructure.Persistence;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace Shorts.Api.Endpoints;

public static class CandidateEndpoints
{
    public static IEndpointRouteBuilder MapCandidateEndpoints(this IEndpointRouteBuilder app)
    {
        var candidates = app.MapGroup("/candidates");

        candidates.MapGet("", (InMemoryCandidateStore store) => HttpResults.Ok(store.List()));
        candidates.MapPost("", (CreateCandidateRequest request, InMemoryCandidateStore store) =>
            ApiResults.ExecuteWithResult(
                () => store.Add(request.CanonicalUrl, request.Title, request.Creator, request.PublishedAt, request.Signals),
                candidate => HttpResults.Created($"/candidates/{candidate.Id}", candidate)));
        candidates.MapPost("/{id:guid}/sources", (Guid id, CreateSourceRequest request, InMemoryCandidateStore store) =>
            ApiResults.Execute(() => store.AddSource(id, request.Url, request.Publisher, request.Excerpt, request.ReliabilityScore)));
        candidates.MapGet("/{id:guid}/sources", (Guid id, InMemoryCandidateStore store) =>
            store.TryGet(id, out _) ? HttpResults.Ok(store.GetSources(id)) : HttpResults.NotFound());
        candidates.MapPost("/{id:guid}/research-pack", (Guid id, CreateResearchPackRequest request, InMemoryCandidateStore store) =>
            ApiResults.ExecuteWithResult(() => store.CreateResearchPack(id, request.Summary, request.FactsJson, request.UncertaintyJson), HttpResults.Ok));
        candidates.MapGet("/{id:guid}/research-pack", (Guid id, InMemoryCandidateStore store) =>
            store.TryGetResearchPack(id, out var pack) && pack is not null ? HttpResults.Ok(pack) : HttpResults.NotFound());

        return app;
    }
}
