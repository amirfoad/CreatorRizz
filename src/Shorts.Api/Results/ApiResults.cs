using Shorts.Domain;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace Shorts.Api.Results;

internal static class ApiResults
{
    public static ReviewKind ParseReviewKind(string value) => Enum.TryParse<ReviewKind>(value, true, out var kind)
        ? kind
        : throw new WorkflowRuleViolation("Unknown review kind.");

    public static IResult Execute(Action action)
    {
        try { action(); return HttpResults.NoContent(); }
        catch (KeyNotFoundException) { return HttpResults.NotFound(); }
        catch (WorkflowRuleViolation exception) { return HttpResults.Conflict(new { error = exception.Message }); }
    }

    public static IResult ExecuteWithResult<T>(Func<T> action, Func<T, IResult> success)
    {
        try { return success(action()); }
        catch (ArgumentException exception) { return HttpResults.BadRequest(new { error = exception.Message }); }
        catch (WorkflowRuleViolation exception) { return HttpResults.Conflict(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return HttpResults.Conflict(new { error = exception.Message }); }
    }
}
