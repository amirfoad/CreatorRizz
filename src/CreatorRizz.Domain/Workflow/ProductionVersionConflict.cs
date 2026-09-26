namespace CreatorRizz.Domain;

/// <summary>
/// Raised when a caller tries to change a production using a version it never read. Someone else
/// already moved the production, so the caller has to read it again and decide for itself. This is
/// deliberately not a <see cref="WorkflowRuleViolation"/>: the transition may be perfectly legal
/// once the caller sees the current state, so retrying blindly is wrong.
/// </summary>
public sealed class ProductionVersionConflict(int expectedVersion, int currentVersion)
    : InvalidOperationException(
        $"This production changed since you read it. You reviewed version {expectedVersion}, but the current version is {currentVersion}. Read the production again and confirm your decision still applies.")
{
    public int ExpectedVersion { get; } = expectedVersion;
    public int CurrentVersion { get; } = currentVersion;
}
