using Microsoft.Extensions.DependencyInjection;

namespace Shorts.Api.OpenApi;

public static class SwaggerExtensions
{
    public static IServiceCollection AddCreatorRizzSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "CreatorRizz API",
                Version = "v1",
                Description = "Controlled workflow API for commentary and storytelling YouTube Shorts."
            });
        });

        return services;
    }

    public static WebApplication UseCreatorRizzSwagger(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return app;

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "CreatorRizz API v1");
            options.DocumentTitle = "CreatorRizz API";
        });

        return app;
    }
}
