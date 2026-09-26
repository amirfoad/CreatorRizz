using System.Globalization;

namespace CreatorRizz.Api.Middleware;

/// <summary>
/// Requires an <c>If-Match</c> production version on every mutating production request. Enforced
/// here so no individual endpoint can forget it, and so two reviewers acting on the same production
/// cannot silently overwrite each other. <c>POST /productions</c> is excluded because it creates the
/// production the version would belong to.
/// </summary>
public sealed class ProductionVersionPreconditionMiddleware(RequestDelegate next)
{
    private const string VersionItem = "creatorrizz.expected-production-version";

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresExpectedVersion(context.Request))
        {
            if (!TryReadExpectedVersion(context.Request, out var expectedVersion))
            {
                context.Response.StatusCode = StatusCodes.Status428PreconditionRequired;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Send an If-Match header with the production version you read, quoted, for example If-Match: \"7\". Read the production first if you have not."
                });
                return;
            }

            context.Items[VersionItem] = expectedVersion;
        }

        await next(context);
    }

    public static int ReadExpectedVersion(HttpContext context) =>
        (int)context.Items[VersionItem]!;

    public static void WriteVersionHeader(HttpContext context, int version) =>
        context.Response.Headers.ETag = $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    private static bool RequiresExpectedVersion(HttpRequest request)
    {
        if (request.Method is not ("POST" or "PUT" or "PATCH")) return false;
        return request.Path.StartsWithSegments("/productions", StringComparison.OrdinalIgnoreCase)
            && request.Path.Value?.EndsWith("/productions", StringComparison.OrdinalIgnoreCase) != true;
    }

    private static bool TryReadExpectedVersion(HttpRequest request, out int expectedVersion)
    {
        expectedVersion = 0;
        var header = request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(header)) return false;
        return int.TryParse(header.Trim().Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out expectedVersion);
    }
}
