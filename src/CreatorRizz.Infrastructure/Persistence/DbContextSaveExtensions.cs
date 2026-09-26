using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CreatorRizz.Infrastructure.Persistence;

/// <summary>
/// Single place where a PostgreSQL write failure becomes a domain-meaningful error so callers
/// never have to read provider exceptions.
/// </summary>
internal static class DbContextSaveExtensions
{
    public static void SaveWorkflowChanges(this CreatorRizzDbContext database)
    {
        try
        {
            database.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            // DbUpdateConcurrencyException derives from DbUpdateException, so without this it would be
            // relabelled as a generic persistence failure. The caller needs the real cause to report
            // which version actually won.
            throw;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation)
        {
            throw new InvalidOperationException(
                $"A record that must be unique already exists. Database constraint: {violation.ConstraintName}.", exception);
        }
        catch (DbUpdateException exception)
        {
            throw new InvalidOperationException("PostgreSQL could not persist the workflow change.", exception);
        }
    }

    public static async Task SaveOutboxChangesAsync(this CreatorRizzDbContext database, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation)
        {
            throw new InvalidOperationException(
                $"A record that must be unique already exists. Database constraint: {violation.ConstraintName}.", exception);
        }
        catch (DbUpdateException exception)
        {
            throw new InvalidOperationException("PostgreSQL could not record the queued work.", exception);
        }
    }
}
