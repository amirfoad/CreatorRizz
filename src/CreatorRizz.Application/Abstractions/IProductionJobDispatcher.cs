namespace CreatorRizz.Application.Abstractions;

/// <summary>
/// One unit of work handed to a worker. The payload stays JSON on purpose: what a job needs depends on
/// its kind, and a dispatcher that understood every payload would be a second copy of the domain.
/// </summary>
public sealed record ClaimedProductionJob(Guid Id, ProductionJobKind Kind, Guid ProductionId, string PayloadJson, int Attempt);

/// <summary>
/// Leases recorded work to workers. Kept apart from <see cref="IProductionJobQueue"/>, which only ever
/// records intent: writing a row and running a row fail in different ways, and a queue that claimed
/// its own work would be responsible for both.
/// </summary>
public interface IProductionJobDispatcher
{
    /// <summary>
    /// Takes the oldest unfinished row of one kind whose lease has expired, or null when there is
    /// nothing to take. Rows that already used up their attempts are left alone rather than retried.
    /// </summary>
    Task<ClaimedProductionJob?> ClaimNextAsync(
        ProductionJobKind kind,
        string workerId,
        TimeSpan leaseDuration,
        int maxAttempts,
        CancellationToken cancellationToken);

    /// <summary>Records that the work is done, so the row stops being offered to any worker.</summary>
    Task CompleteAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Hands the row back with the reason it failed. The row goes back on the queue instead of being
    /// deleted, because the failure is the only record of what went wrong with that production.
    /// </summary>
    Task ReleaseAsync(Guid jobId, string error, CancellationToken cancellationToken);
}
