using CreatorRizz.Domain;

namespace CreatorRizz.Application.Abstractions;

/// <summary>
/// What a recorded unit of work is for. Stored as text so the outbox table stays readable when a job
/// is stuck and someone has to look at the row directly.
/// </summary>
public enum ProductionJobKind
{
    TextToSpeech,
    Render
}

public interface IProductionJobQueue
{
    ValueTask EnqueueTextToSpeechAsync(TextToSpeechJob job, string idempotencyKey, CancellationToken cancellationToken);
    ValueTask EnqueueRenderAsync(RenderManifest manifest, string idempotencyKey, CancellationToken cancellationToken);
}

public sealed record TextToSpeechJob(Guid ProductionId, string Text, string VoiceId, decimal Speed);

/// <summary>
/// Builds the keys that say "this operator asked for this work, having read the production at this
/// version". The key is built from the version the caller read rather than an assumed next version,
/// so a caller never has to guess how the version moved.
/// </summary>
public static class ProductionJobKey
{
    public static string ForTextToSpeech(Guid productionId, int versionReadByCaller) => $"tts:{productionId}:{versionReadByCaller}";
    public static string ForRender(Guid productionId, int versionReadByCaller) => $"render:{productionId}:{versionReadByCaller}";
}
