using CreatorRizz.Application.Abstractions;

namespace CreatorRizz.Infrastructure.Persistence;

/// <summary>
/// A recorded intent to do work, written in the same transaction as the state change that implies it.
/// This is the outbox table: it exists so a production can never be left waiting for work that was
/// never recorded, and so a restart does not lose work that was already accepted.
/// </summary>
public sealed class JobOutboxEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required ProductionJobKind Kind { get; init; }
    public Guid ProductionId { get; init; }

    /// <summary>
    /// Identifies one request to do work. Recording the same key twice is one unit of work, not two,
    /// which is what makes a replayed transaction safe to run.
    /// </summary>
    public required string IdempotencyKey { get; init; }

    public required string PayloadJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When a worker leased this row, and null while nobody holds it. A lease rather than a deletion is
    /// what makes a crashed worker's work retryable: the row comes back on its own instead of needing
    /// someone to notice it is stuck.
    /// </summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    /// <summary>Which worker holds the lease, so a row stuck in one process is attributable.</summary>
    public string? ClaimedBy { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// How many times the row has been handed out. Bounded by the dispatcher, because a job that fails
    /// the same way forever must stop rather than spend a render on every poll.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Why the last attempt failed, kept on the row because "this job is stuck" is not a diagnosis.
    /// </summary>
    public string? LastError { get; set; }
}
