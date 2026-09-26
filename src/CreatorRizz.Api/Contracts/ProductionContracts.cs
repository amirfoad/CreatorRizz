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
public sealed record ReviewRequest(ReviewOutcome Outcome, string ReviewerId, string? Notes);
