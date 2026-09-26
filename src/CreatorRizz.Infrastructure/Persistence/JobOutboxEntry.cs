using CreatorRizz.Application.Abstractions;

namespace CreatorRizz.Infrastructure.Persistence;

/// <summary>
/// A recorded intent to do work, written in the same transaction as the state change that implies it.
/// This is the outbox table: it exists so a production can never be left waiting for work that was
/// never recorded, and so a restart does not lose work that was already accepted.
/// </summary>
/// <remarks>
/// Claim and completion columns are deliberately absent. Nothing consumes this table yet, and adding
/// them before a consumer exists would be a column nobody writes.
/// </remarks>
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
}
