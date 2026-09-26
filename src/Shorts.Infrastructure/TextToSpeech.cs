namespace Shorts.Infrastructure;

public sealed record TextToSpeechRequest(Guid ProductionId, string Text, string VoiceId, decimal Speed);
public sealed record TextToSpeechResult(string ObjectKey, TimeSpan Duration, IReadOnlyCollection<SpeechCue> Cues);
public sealed record SpeechCue(int StartMilliseconds, int EndMilliseconds, string Text);

public interface ITextToSpeechProvider
{
    Task<TextToSpeechResult> SynthesizeAsync(TextToSpeechRequest request, CancellationToken cancellationToken);
}

public sealed class DisabledTextToSpeechProvider : ITextToSpeechProvider
{
    public Task<TextToSpeechResult> SynthesizeAsync(TextToSpeechRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) throw new ArgumentException("Narration text is required.", nameof(request));
        if (request.Speed is < 0.5m or > 2m) throw new ArgumentOutOfRangeException(nameof(request), "Speech speed must be between 0.5 and 2.0.");
        throw new InvalidOperationException("No TTS provider is configured. Add a managed provider and its API key before synthesizing speech.");
    }
}
