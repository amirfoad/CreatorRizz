using System.Security.Claims;

namespace CreatorRizz.Api.Authentication;

/// <summary>
/// Who may pass a review gate, and how the audit trail names them. The role and the subject both come
/// from a validated token, never from a request header or a request body, because both of those are
/// chosen by the caller and would let anyone record a review under someone else's name.
/// </summary>
public static class ReviewerAccess
{
    /// <summary>Authorization policy that also demands the Reviewer role, on top of a valid token.</summary>
    public const string PolicyName = "reviewer";

    public const string Role = "reviewer";
    public const string SubjectClaim = "sub";

    /// <summary>
    /// The identity recorded on the review decision and the audit event. The subject claim is the
    /// stable identifier an identity provider guarantees; a display name is not, and a name in the
    /// request body is not a name at all.
    /// </summary>
    public static string ReadId(ClaimsPrincipal reviewer)
    {
        ArgumentNullException.ThrowIfNull(reviewer);
        var subject = reviewer.FindFirstValue(SubjectClaim);
        if (string.IsNullOrWhiteSpace(subject))
            throw new InvalidOperationException("The authenticated token carries no subject claim, so the reviewer cannot be named.");
        return subject;
    }
}
