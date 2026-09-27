using CreatorRizz.Application.Abstractions;
using CreatorRizz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CreatorRizz.Infrastructure.Jobs;

/// <summary>
/// Hands recorded work to workers using a lease instead of a delete. The claim is a single UPDATE with
/// a locked subquery, so two workers can never take the same row, and a worker that dies takes nothing
/// with it: the lease simply expires and the row is offered again.
/// </summary>
public sealed class PostgresProductionJobDispatcher(CreatorRizzDbContext database, TimeProvider clock) : IProductionJobDispatcher
{
    /// <summary>Long enough for the failure text to stay useful, short enough that the row is not a secret.</summary>
    private const int MaxRecordedErrorLength = 2000;

    public async Task<ClaimedProductionJob?> ClaimNextAsync(
        ProductionJobKind kind,
        string workerId,
        TimeSpan leaseDuration,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workerId)) throw new ArgumentException("A worker must identify itself to claim work.", nameof(workerId));
        if (leaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration), "A lease must last longer than zero.");
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts), "A job must be allowed at least one attempt.");

        var now = clock.GetUtcNow();
        var leaseExpiredBefore = now - leaseDuration;

        // FOR UPDATE SKIP LOCKED inside the subquery is what makes this safe with more than one worker:
        // the loser of the race skips the row the winner is holding and takes the next one instead of
        // blocking behind it. Every mapped column is returned because EF materialises a whole entity, and
        // the result is read on the client because EF will not compose further SQL onto an UPDATE.
        var rows = await database.JobOutbox
            .FromSqlInterpolated($$"""
                UPDATE job_outbox AS job
                SET claimed_at = {{now}}, claimed_by = {{workerId}}, attempts = job.attempts + 1
                WHERE job.id = (
                    SELECT candidate.id
                    FROM job_outbox AS candidate
                    WHERE candidate.kind = {{kind.ToString()}}
                      AND candidate.completed_at IS NULL
                      AND candidate.attempts < {{maxAttempts}}
                      AND (candidate.claimed_at IS NULL OR candidate.claimed_at < {{leaseExpiredBefore}})
                    ORDER BY candidate.created_at, candidate.id
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING job.id, job.kind, job.production_id, job.idempotency_key, job.payload_json,
                          job.created_at, job.claimed_at, job.claimed_by, job.completed_at,
                          job.attempts, job.last_error
                """)
            .ToListAsync(cancellationToken);
        var claimed = rows.SingleOrDefault();

        return claimed is null
            ? null
            : new ClaimedProductionJob(claimed.Id, claimed.Kind, claimed.ProductionId, claimed.PayloadJson, claimed.Attempts);
    }

    public Task CompleteAsync(Guid jobId, CancellationToken cancellationToken) =>
        database.JobOutbox
            .Where(entry => entry.Id == jobId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(entry => entry.CompletedAt, clock.GetUtcNow())
                .SetProperty(entry => entry.ClaimedAt, (DateTimeOffset?)null)
                .SetProperty(entry => entry.ClaimedBy, (string?)null), cancellationToken);

    public Task ReleaseAsync(Guid jobId, string error, CancellationToken cancellationToken) =>
        database.JobOutbox
            .Where(entry => entry.Id == jobId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(entry => entry.LastError, Summarize(error))
                .SetProperty(entry => entry.ClaimedAt, (DateTimeOffset?)null)
                .SetProperty(entry => entry.ClaimedBy, (string?)null), cancellationToken);

    /// <summary>
    /// An exception message can be arbitrarily long and can carry a file path or a provider response.
    /// Trimming it keeps the column bounded and keeps a failed row readable in a terminal.
    /// </summary>
    private static string Summarize(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        var single = error.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return single.Length <= MaxRecordedErrorLength ? single : single[..MaxRecordedErrorLength];
    }
}
