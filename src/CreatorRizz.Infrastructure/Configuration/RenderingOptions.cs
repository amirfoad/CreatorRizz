namespace CreatorRizz.Infrastructure.Configuration;

/// <summary>
/// How the render worker takes work off the outbox. The lease and the attempt ceiling live here because
/// they are the two numbers that decide whether a failed render is retried, abandoned, or left for ever.
/// </summary>
public sealed class RenderingOptions
{
    public const string SectionName = "Rendering";

    /// <summary>The FFmpeg binary. Resolved like any other command, so an absolute path is the reliable choice.</summary>
    public string FfmpegPath { get; init; } = "ffmpeg";

    /// <summary>How often an idle worker looks for work.</summary>
    public int PollIntervalSeconds { get; init; } = 5;

    /// <summary>
    /// How long a worker may hold a row before another worker may take it. It has to outlast a normal
    /// render, because a lease that expires mid-render lets a second worker render the same production
    /// at the same time.
    /// </summary>
    public int LeaseSeconds { get; init; } = 1800;

    /// <summary>
    /// How many times one row may be handed out. A render that fails the same way forever must stop
    /// rather than spend a full encode on every poll.
    /// </summary>
    public int MaxAttempts { get; init; } = 3;
}
