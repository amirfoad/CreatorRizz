using CreatorRizz.Application.Abstractions;
using CreatorRizz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CreatorRizz.Infrastructure.Jobs;

/// <summary>
/// Runs work inside a database transaction on the same context the work uses, so a state change and
/// the work it implies either both commit or both roll back. Not disposing a committed transaction
/// before returning is a bug, so the lifetime is scoped to this one call.
/// </summary>
public sealed class PostgresWorkflowTransaction(CreatorRizzDbContext database) : IWorkflowTransaction
{
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var result = await work(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
