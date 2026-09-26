namespace CreatorRizz.Api.Endpoints;

public static class EndpointMappings
{
    public static IEndpointRouteBuilder MapCreatorRizzEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapCandidateEndpoints();
        app.MapProductionEndpoints();
        return app;
    }
}
