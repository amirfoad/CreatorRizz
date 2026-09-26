using System.Text.Json;
using CreatorRizz.Application.Abstractions;
using CreatorRizz.Domain;

namespace CreatorRizz.Infrastructure.BackgroundJobs;

public sealed class InMemoryProductionJobQueue(IBackgroundJobQueue queue) : IProductionJobQueue
{
    public ValueTask EnqueueTextToSpeechAsync(TextToSpeechJob job, CancellationToken cancellationToken) =>
        queue.EnqueueAsync("tts", JsonSerializer.Serialize(job), cancellationToken);

    public ValueTask EnqueueRenderAsync(RenderManifest manifest, CancellationToken cancellationToken) =>
        queue.EnqueueAsync("render", JsonSerializer.Serialize(manifest), cancellationToken);
}
