using System.Text.Json;
using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;
using CreatorRizz.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CreatorRizz.Infrastructure.Jobs;

/// <summary>
/// Records work in PostgreSQL rather than in process memory, using the same <see cref="CreatorRizzDbContext"/>
/// scope as the state change that queued it. When the caller wraps both in
/// <see cref="IWorkflowTransaction"/> they commit together.
/// </summary>
public sealed class PostgresProductionJobQueue(CreatorRizzDbContext database) : IProductionJobQueue
{
    public ValueTask EnqueueTextToSpeechAsync(TextToSpeechJob job, string idempotencyKey, CancellationToken cancellationToken) =>
        RecordAsync(ProductionJobKind.TextToSpeech, job.ProductionId, idempotencyKey, job, cancellationToken);

    public ValueTask EnqueueRenderAsync(RenderManifest manifest, string idempotencyKey, CancellationToken cancellationToken) =>
        RecordAsync(ProductionJobKind.Render, manifest.ProductionId, idempotencyKey, manifest, cancellationToken);

    private async ValueTask RecordAsync<TPayload>(
        ProductionJobKind kind,
        Guid productionId,
        string idempotencyKey,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("A job idempotency key is required.", nameof(idempotencyKey));

        // Replaying the same request must not become a second unit of work. The unique index on
        // idempotency_key is the real guard against a concurrent replay; this check is the cheap path
        // that keeps an ordinary retried request from surfacing a constraint violation as an error.
        var alreadyRecorded = await database.JobOutbox
            .AsNoTracking()
            .AnyAsync(entry => entry.IdempotencyKey == idempotencyKey, cancellationToken);
        if (alreadyRecorded) return;

        database.JobOutbox.Add(new JobOutboxEntry
        {
            Kind = kind,
            ProductionId = productionId,
            IdempotencyKey = idempotencyKey,
            PayloadJson = JsonSerializer.Serialize(payload)
        });

        await database.SaveOutboxChangesAsync(cancellationToken);
    }
}
