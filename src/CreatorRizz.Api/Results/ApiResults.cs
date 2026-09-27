using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace CreatorRizz.Api.Results;

internal static class ApiResults
{
    public static ReviewKind ParseReviewKind(string value) => Enum.TryParse<ReviewKind>(value, true, out var kind)
        ? kind
        : throw new WorkflowRuleViolation("Unknown review kind.");

    /// <summary>
    /// A filter the caller named does not exist. This is 400, not the 404 an unknown production gets:
    /// the request is well-formed but asks about something this service does not have.
    /// </summary>
    public static IResult UnknownFilter(string filter, string value) =>
        HttpResults.BadRequest(new { error = $"Unknown {filter} '{value}'.", filter });

    public static IResult Execute(Action action)
    {
        try { action(); return HttpResults.NoContent(); }
        catch (KeyNotFoundException) { return HttpResults.NotFound(); }
        catch (ArgumentException exception) { return HttpResults.BadRequest(new { error = exception.Message }); }
        catch (ProductionVersionConflict exception) { return VersionConflict(exception); }
        catch (WorkflowRuleViolation exception) { return HttpResults.Conflict(new { error = exception.Message }); }
    }

    public static IResult ExecuteWithResult<T>(Func<T> action, Func<T, IResult> success)
    {
        try { return success(action()); }
        catch (KeyNotFoundException) { return HttpResults.NotFound(); }
        catch (ArgumentException exception) { return HttpResults.BadRequest(new { error = exception.Message }); }
        catch (ProductionVersionConflict exception) { return VersionConflict(exception); }
        catch (WorkflowRuleViolation exception) { return HttpResults.Conflict(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return HttpResults.Conflict(new { error = exception.Message }); }
    }

    /// <summary>
    /// A version conflict reports the winning version so the client can re-read once instead of
    /// guessing or retrying blindly.
    /// </summary>
    public static IResult VersionConflict(ProductionVersionConflict exception) =>
        HttpResults.Conflict(new { error = exception.Message, currentVersion = exception.CurrentVersion });

    /// <summary>
    /// A provider failure is upstream of this service, so it answers 502 rather than the 409 the same
    /// message would get as a refused draft. A client that reads 409 stops; one that reads 502 retries.
    /// </summary>
    public static IResult ProviderFailure(ScriptGenerationFailedException exception) =>
        HttpResults.Problem(exception.Message, statusCode: StatusCodes.Status502BadGateway);
}
