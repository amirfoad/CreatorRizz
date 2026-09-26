namespace Shorts.Domain;

public sealed record TimelineClip(Guid AssetId, int StartMilliseconds, int EndMilliseconds, int TimelineStartMilliseconds);
public sealed record CaptionCue(int StartMilliseconds, int EndMilliseconds, string Text);
public sealed record RenderManifest(
    Guid ProductionId,
    int Width,
    int Height,
    IReadOnlyCollection<TimelineClip> Clips,
    IReadOnlyCollection<CaptionCue> Captions,
    string VoiceObjectKey);

public static class RenderManifestValidator
{
    public static void Validate(RenderManifest manifest)
    {
        if (manifest.Width != 1080 || manifest.Height != 1920) throw new WorkflowRuleViolation("MVP renders must be 1080x1920.");
        if (string.IsNullOrWhiteSpace(manifest.VoiceObjectKey)) throw new WorkflowRuleViolation("A voice track is required.");
        if (manifest.Clips.Count == 0) throw new WorkflowRuleViolation("At least one visual clip is required.");
        if (manifest.Clips.Any(x => x.StartMilliseconds < 0 || x.EndMilliseconds <= x.StartMilliseconds || x.TimelineStartMilliseconds < 0))
            throw new WorkflowRuleViolation("Clip timings must be positive and ordered.");
        if (manifest.Captions.Any(x => x.StartMilliseconds < 0 || x.EndMilliseconds <= x.StartMilliseconds || string.IsNullOrWhiteSpace(x.Text)))
            throw new WorkflowRuleViolation("Caption cues must have ordered timings and text.");
    }
}
