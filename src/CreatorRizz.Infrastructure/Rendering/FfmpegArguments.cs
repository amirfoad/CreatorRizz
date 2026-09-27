using CreatorRizz.Domain;

namespace CreatorRizz.Infrastructure.Rendering;

/// <summary>Where one clip's bytes live once it has been pulled out of object storage.</summary>
public sealed record RenderInput(Guid AssetId, string Path, int StartMilliseconds, int EndMilliseconds, int TimelineStartMilliseconds);

/// <summary>
/// Builds the FFmpeg argument list for a manifest. Kept separate from running the process so the
/// mapping from a reviewed manifest to a command can be checked without a media file or a binary.
/// </summary>
public static class FfmpegArguments
{
    /// <summary>
    /// Voice first, then one input per visual clip in timeline order, so input 0 is the audio track and
    /// inputs 1..n line up with the concat filter below.
    /// </summary>
    public static IReadOnlyList<string> Build(
        RenderManifest manifest,
        IReadOnlyCollection<RenderInput> clips,
        string voicePath,
        string subtitlePath,
        string outputPath)
    {
        RenderManifestValidator.Validate(manifest);
        ArgumentNullException.ThrowIfNull(clips);
        if (clips.Count == 0) throw new WorkflowRuleViolation("At least one visual clip is required.");
        if (clips.Any(clip => !manifest.Clips.Any(declared => declared.AssetId == clip.AssetId)))
            throw new WorkflowRuleViolation("A render input does not correspond to any clip in the manifest.");

        var ordered = clips.OrderBy(clip => clip.TimelineStartMilliseconds).ToArray();
        var arguments = new List<string> { "-hide_banner", "-nostdin", "-y" };

        arguments.Add("-i");
        arguments.Add(voicePath);

        foreach (var clip in ordered)
        {
            // -ss before -i seeks by keyframe, which is fast; -t then bounds the read. A clip whose
            // timings were never filled in fails the manifest validator rather than rendering a still.
            arguments.Add("-ss");
            arguments.Add(Seconds(clip.StartMilliseconds));
            arguments.Add("-t");
            arguments.Add(Seconds(clip.EndMilliseconds - clip.StartMilliseconds));
            arguments.Add("-i");
            arguments.Add(clip.Path);
        }

        arguments.Add("-filter_complex");
        arguments.Add(BuildFilterGraph(manifest, ordered, subtitlePath, voicePath));
        arguments.Add("-map");
        arguments.Add("[v]");
        arguments.Add("-map");
        arguments.Add("[a]");
        arguments.Add("-c:v");
        arguments.Add("libx264");
        arguments.Add("-pix_fmt");
        arguments.Add("yuv420p");
        arguments.Add("-preset");
        arguments.Add("medium");
        arguments.Add("-crf");
        arguments.Add("23");
        arguments.Add("-c:a");
        arguments.Add("aac");
        arguments.Add("-b:a");
        arguments.Add("128k");
        arguments.Add("-movflags");
        arguments.Add("+faststart");
        arguments.Add("-shortest");
        arguments.Add(outputPath);
        return arguments;
    }

    /// <summary>
    /// Scales every clip to the manifest size without distortion, concatenates them in timeline order,
    /// then burns the subtitles in. Captions are positioned in the lower third because a Short is
    /// watched with the bottom of the frame covered by the platform's own UI.
    /// </summary>
    private static string BuildFilterGraph(RenderManifest manifest, IReadOnlyCollection<RenderInput> ordered, string subtitlePath, string voicePath)
    {
        var filters = new List<string>();
        for (var index = 0; index < ordered.Count; index++)
        {
            filters.Add(
                $"[{index + 1}:v]scale={manifest.Width}:{manifest.Height}:force_original_aspect_ratio=decrease," +
                $"pad={manifest.Width}:{manifest.Height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30[v{index}]");
        }

        var concatInputs = string.Join(string.Empty, ordered.Select((_, index) => $"[v{index}]"));
        filters.Add($"{concatInputs}concat=n={ordered.Count}:v=1:a=0[cat]");

        // The subtitle path is escaped because it is a filesystem path and FFmpeg's filter parser treats
        // a colon as a stream specifier. Windows paths are the reason this is not a plain concatenation.
        var escapedSubtitles = subtitlePath.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
        filters.Add($"[cat]subtitles='{escapedSubtitles}':force_style='FontSize=18,MarginV=120'[v]");

        // The voice is trimmed to the assembled picture so a narration longer than the visuals cannot
        // produce a Short whose audio runs past the last frame.
        return string.Join(";", filters) + $";[0:a]atrim=0:{Seconds(TimelineLengthMilliseconds(ordered))},asetpts=N/SR/TB[a]";
    }

    private static int TimelineLengthMilliseconds(IReadOnlyCollection<RenderInput> ordered) =>
        ordered.Max(clip => clip.TimelineStartMilliseconds + clip.EndMilliseconds - clip.StartMilliseconds);

    private static string Seconds(int milliseconds) =>
        (milliseconds / 1000d).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
}
