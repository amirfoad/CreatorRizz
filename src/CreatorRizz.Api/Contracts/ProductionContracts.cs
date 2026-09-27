using CreatorRizz.Domain;

namespace CreatorRizz.Api.Contracts;

public sealed record CreateProductionRequest(Guid CandidateId);
public sealed record CreateScriptRequest(string Body, string ClaimMapJson);
public sealed record QueueTtsRequest(string? VoiceId, decimal Speed = 1m);
public sealed record AttachAssetRequest(
    Guid ProductionId,
    string Type,
    string? SourceUrl,
    RightsStatus RightsStatus,
    string NarrativePurpose,
    string? LicenseEvidence,
    byte[] Data);
public sealed record ProductionResponse(Guid Id, ProductionState State, int Version);

/// <summary>
/// One row of the backlog or of the review queue. The state is echoed as-is because the caller has to
/// pick a state filter and a version to send back as <c>If-Match</c>, and guessing either from a
/// separate <c>status</c> string is how a stale write happens.
/// </summary>
public sealed record ProductionListItemResponse(Guid Id, Guid TopicCandidateId, ProductionState State, int Version, DateTimeOffset CreatedAt, ReviewKind? AwaitingReview);

public sealed record ProductionListResponse(IReadOnlyCollection<ProductionListItemResponse> Items, int TotalCount, int Skip, bool HasMore);

/// <summary>
/// The reviewer is deliberately absent. It is read from the validated token, so a caller cannot record
/// a review under someone else's name by editing a field.
/// </summary>
public sealed record ReviewRequest(ReviewOutcome Outcome, string? Notes);
