namespace CreatorRizz.Domain;

public sealed record CaptionCue(int StartMilliseconds, int EndMilliseconds, string Text);

public static class CaptionCueValidator
{
    public static void EnsureValid(IEnumerable<CaptionCue> captions)
    {
        if (captions.Any(caption =>
                caption.StartMilliseconds < 0 ||
                caption.EndMilliseconds <= caption.StartMilliseconds ||
                string.IsNullOrWhiteSpace(caption.Text)))
        {
            throw new WorkflowRuleViolation("Caption cues must have ordered timings and text.");
        }
    }
}
