using CreatorRizz.Api.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace CreatorRizz.Api.OpenApi;

public static class SwaggerExtensions
{
    public static IServiceCollection AddCreatorRizzSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "CreatorRizz API",
                Version = "v1",
                Description = "Controlled workflow API for commentary and storytelling YouTube CreatorRizz."
            });
            // Without this the UI can only send anonymous requests, so every endpoint would look broken.
            options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "A token from the configured identity provider, with the reviewer role for review decisions."
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = JwtBearerDefaults.AuthenticationScheme }
                }] = Array.Empty<string>()
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
