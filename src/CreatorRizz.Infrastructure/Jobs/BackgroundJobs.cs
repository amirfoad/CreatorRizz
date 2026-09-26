using System.Threading.Channels;

namespace CreatorRizz.Infrastructure.Jobs;

public sealed record BackgroundJob(Guid Id, string Type, string PayloadJson, DateTimeOffset EnqueuedAt);

public interface IBackgroundJobQueue
{
    ValueTask EnqueueAsync(string type, string payloadJson, CancellationToken cancellationToken);
    ValueTask<BackgroundJob> DequeueAsync(CancellationToken cancellationToken);
}

public sealed class InMemoryBackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<BackgroundJob> _channel = Channel.CreateBounded<BackgroundJob>(new BoundedChannelOptions(100)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false
    });

    public ValueTask EnqueueAsync(string type, string payloadJson, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(type)) throw new ArgumentException("A job type is required.", nameof(type));
        return _channel.Writer.WriteAsync(new BackgroundJob(Guid.NewGuid(), type, payloadJson, DateTimeOffset.UtcNow), cancellationToken);
    }

    public ValueTask<BackgroundJob> DequeueAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAsync(cancellationToken);
}
