using System.Security.Claims;
using CreatorRizz.Api.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CreatorRizz.Domain.Tests;

/// <summary>
/// Covers the review gate as the application actually configures it. Before this, a review decision
/// needed nothing but a request header the caller chose, so the gate could be passed by anyone who
/// could reach the port, and the audit trail recorded whatever name the body asked for.
/// </summary>
public sealed class ReviewerGateTests
{
    [Fact]
    public async Task AValidatedReviewerMayDecideAReview()
    {
        var authorization = BuildAuthorization();

        Assert.True(await IsAllowed(authorization, ReviewerAccess.PolicyName, Token("reviewer-7", ReviewerAccess.Role)));
    }

    [Fact]
    public async Task AValidTokenWithoutTheReviewerRoleMayNotDecideAReview()
    {
        var authorization = BuildAuthorization();

        Assert.False(await IsAllowed(authorization, ReviewerAccess.PolicyName, Token("operator-1", "operator")));
    }

    [Fact]
    public async Task TheReviewerRoleAloneIsNotAToken()
    {
        var authorization = BuildAuthorization();

        // A role claim with no authenticated identity is exactly what a hand-written header was.
        var forged = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ReviewerAccess.Role, ReviewerAccess.Role), new Claim(ReviewerAccess.SubjectClaim, "reviewer-7")],
            authenticationType: null));

        Assert.False(await IsAllowed(authorization, ReviewerAccess.PolicyName, forged));
    }

    [Fact]
    public async Task AnAnonymousCallerCannotReachAnyEndpoint()
    {
        var provider = BuildProvider();
        var fallback = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy;
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.NotNull(fallback);
        Assert.False((await authorization.AuthorizeAsync(anonymous, resource: null, fallback)).Succeeded);
    }

    [Fact]
    public async Task AnyAuthenticatedTokenReachesTheOrdinaryEndpoints()
    {
        var provider = BuildProvider();
        var fallback = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy;
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        // The fallback only demands a valid token. Passing a review gate still needs the reviewer role.
        Assert.NotNull(fallback);
        Assert.True((await authorization.AuthorizeAsync(
            Token("operator-1", "operator"), resource: null, fallback)).Succeeded);
    }

    [Fact]
    public void TheReviewerIsNamedByTheTokenRatherThanTheRequest()
    {
        Assert.Equal("reviewer-7", ReviewerAccess.ReadId(Token("reviewer-7", ReviewerAccess.Role)));
    }

    [Fact]
    public void ATokenWithoutASubjectCannotBeAttributedAndIsRefused()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ReviewerAccess.Role, ReviewerAccess.Role)], "Test"));

        var failure = Assert.Throws<InvalidOperationException>(() => ReviewerAccess.ReadId(anonymous));

        Assert.Contains("subject", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartupRefusesWhenNoTokenSourceIsConfigured()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddCreatorRizzAuthentication(ConfigurationWith(("Authentication:Schemes:Bearer:Authority", "")));

        var failure = Assert.Throws<OptionsValidationException>(() => BearerOptions(services));

        Assert.Contains("No token source is configured", failure.Message);
    }

    [Fact]
    public void StartupRefusesWhenTheAudienceIsMissing()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddCreatorRizzAuthentication(ConfigurationWith(
            ("Authentication:Schemes:Bearer:Authority", "https://login.example.com"),
            ("Authentication:Schemes:Bearer:Audience", "")));

        var failure = Assert.Throws<OptionsValidationException>(() => BearerOptions(services));

        Assert.Contains("Audience", failure.Message);
    }

    [Fact]
    public void SuppliedSigningKeysAreAcceptedAsATokenSource()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddCreatorRizzAuthentication(ConfigurationWith(
            ("Authentication:Schemes:Bearer:Audience", "creatorrizz-api"),
            ("Authentication:Schemes:Bearer:TokenValidationParameters:IssuerSigningKeys:0:Key", "a-signing-key")));

        Assert.Equal("creatorrizz-api", BearerOptions(services).Audience);
    }

    /// <summary>
    /// The per-scheme instance, which is the one the JWT handler reads. The unnamed instance is a
    /// different object and would happily pass validation over an empty configuration.
    /// </summary>
    private static JwtBearerOptions BearerOptions(IServiceCollection services) =>
        services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

    private static IAuthorizationService BuildAuthorization() =>
        BuildProvider().GetRequiredService<IAuthorizationService>();

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddCreatorRizzAuthentication(ConfigurationWith(
            ("Authentication:Schemes:Bearer:Authority", "https://login.example.com"),
            ("Authentication:Schemes:Bearer:Audience", "creatorrizz-api")));
        return services.BuildServiceProvider();
    }

    private static async Task<bool> IsAllowed(IAuthorizationService authorization, string policy, ClaimsPrincipal principal) =>
        (await authorization.AuthorizeAsync(principal, null, policy)).Succeeded;

    /// <summary>
    /// A principal shaped the way a validated token arrives: the token's own claim names, and the
    /// reviewer role read through the role claim type the API configures.
    /// </summary>
    private static ClaimsPrincipal Token(string subject, params string[] roles)
    {
        var claims = roles.Select(role => new Claim(ReviewerAccess.Role, role)).ToList();
        claims.Add(new Claim(ReviewerAccess.SubjectClaim, subject));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test", ClaimTypes.Name, ReviewerAccess.Role));
    }

    private static IConfiguration ConfigurationWith(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)))
            .Build();
}
