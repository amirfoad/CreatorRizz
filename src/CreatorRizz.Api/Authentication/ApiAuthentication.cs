using CreatorRizz.Api.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace CreatorRizz.Api.Authentication;

/// <summary>
/// Puts a validated token in front of every endpoint except the health probes.
/// </summary>
/// <remarks>
/// The fallback policy is the point. A new endpoint is protected by default and only becomes public
/// if someone says so out loud, so an endpoint that forgot its authorization is closed rather than
/// anonymously writable. That was exactly how the review gates were passed before this existed.
/// The API validates tokens and never issues them, so it holds no signing secret of its own.
/// </remarks>
public static class ApiAuthentication
{
    public static IServiceCollection AddCreatorRizzAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var bearer = configuration.GetSection($"Authentication:Schemes:{JwtBearerDefaults.AuthenticationScheme}");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep the token's own claim names. Without this, sub and role are silently rewritten to
                // long claim URIs and the reviewer policy would mean something other than what it reads.
                options.MapInboundClaims = false;
                options.TokenValidationParameters.RoleClaimType = ReviewerAccess.Role;
            });

        // The SDK's `dotnet user-jwts` tool stores its key under SigningKeys, which the JWT handler never
        // reads. Mapping it across is what lets a developer run the API with no identity provider at all.
        // It only applies when no Authority is set, so a deployment with a real provider is untouched.
        services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, LocalSigningKeysFromUserJwts>();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Bind(bearer)
            .Validate(options => !string.IsNullOrWhiteSpace(options.Audience),
                $"'{bearer.Path}:Audience' is required. A token minted for another service must not be able to call this one.")
            .Validate(_ => NamesATokenSource(bearer),
                $"No token source is configured. Set '{bearer.Path}:Authority' to the identity provider that issues tokens, " +
                $"or supply signing keys under '{bearer.Path}:TokenValidationParameters:IssuerSigningKeys' " +
                $"(the 'dotnet user-jwts create --project src/CreatorRizz.Api' command writes keys the API can use). " +
                "Startup fails rather than serving an API that cannot tell a real token from a forged one.")
            .ValidateOnStart();

        services.AddAuthorization(options =>
        {
            options.AddPolicy(ReviewerAccess.PolicyName, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(ReviewerAccess.Role)
                .RequireClaim(ReviewerAccess.SubjectClaim));
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }

    /// <summary>
    /// A token source is either a provider to fetch signing keys from, or signing keys the operator
    /// supplied. With neither, there is no way to validate anything.
    /// </summary>
    private static bool NamesATokenSource(IConfigurationSection bearer) =>
        !string.IsNullOrWhiteSpace(bearer[nameof(JwtBearerOptions.Authority)]) ||
        bearer
            .GetSection($"{nameof(JwtBearerOptions.TokenValidationParameters)}:{nameof(TokenValidationParameters.IssuerSigningKeys)}")
            .GetChildren()
            .Any() ||
        bearer.GetSection(LocalSigningKeysFromUserJwts.Section).GetChildren().Any();
}

/// <summary>
/// Maps the symmetric keys that `dotnet user-jwts create` stores in user secrets onto the validation
/// parameters the JWT handler actually reads. Without this the tool reports a token, the API starts,
/// and then rejects the token it just handed out.
/// </summary>
internal sealed class LocalSigningKeysFromUserJwts(IConfiguration configuration) : IPostConfigureOptions<JwtBearerOptions>
{
    public const string Section = "SigningKeys";

    /// <summary>Config key under <see cref="Section"/> holding the issuer that minted the key.</summary>
    private const string IssuerKey = "Issuer";

    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme) return;
        if (!string.IsNullOrWhiteSpace(options.Authority)) return;

        var keys = configuration
            .GetSection($"Authentication:Schemes:{JwtBearerDefaults.AuthenticationScheme}:{Section}")
            .GetChildren()
            .Select(key => new { Value = key["Value"], Issuer = key[IssuerKey] })
            .Where(key => !string.IsNullOrWhiteSpace(key.Value))
            .Select(key => new SymmetricSecurityKey(Convert.FromBase64String(key.Value!)) { KeyId = key.Issuer })
            .Cast<SecurityKey>()
            .ToArray();

        if (keys.Length == 0) return;

        options.TokenValidationParameters.IssuerSigningKeys = keys;
        // The tool signs with a fixed issuer, and a symmetric key alone cannot say who minted it.
        options.TokenValidationParameters.ValidIssuer = configuration
            .GetSection($"Authentication:Schemes:{JwtBearerDefaults.AuthenticationScheme}:{Section}:0:{IssuerKey}")
            .Value;
    }
}
