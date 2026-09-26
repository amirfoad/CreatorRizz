using Shorts.Domain;

namespace Shorts.Infrastructure;

public sealed record YouTubeUploadRequest(Guid ProductionId, string VideoObjectKey, string Title, string Description, DateTimeOffset? ScheduledAt);
public sealed record YouTubeUploadResult(string VideoId, string PrivacyStatus);

public interface IYouTubePublisher
{
    Task<YouTubeUploadResult> UploadPrivateAsync(YouTubeUploadRequest request, CancellationToken cancellationToken);
}

public sealed class DisabledYouTubePublisher : IYouTubePublisher
{
    public Task<YouTubeUploadResult> UploadPrivateAsync(YouTubeUploadRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("YouTube publishing is not configured. Connect OAuth credentials before enabling uploads.");
}
