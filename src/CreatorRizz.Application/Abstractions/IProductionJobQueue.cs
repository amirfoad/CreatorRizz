using CreatorRizz.Domain;

namespace CreatorRizz.Application.Abstractions;

public interface IProductionJobQueue
{
    ValueTask EnqueueTextToSpeechAsync(TextToSpeechJob job, CancellationToken cancellationToken);
    ValueTask EnqueueRenderAsync(RenderManifest manifest, CancellationToken cancellationToken);
}

public sealed record TextToSpeechJob(Guid ProductionId, string Text, string VoiceId, decimal Speed);
