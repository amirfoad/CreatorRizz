using System.Diagnostics;
using CreatorRizz.Domain;
using CreatorRizz.Infrastructure.Storage;

namespace CreatorRizz.Infrastructure.Rendering;

/// <summary>
/// What a finished render produced. The byte count is measured from the file rather than reported by
/// the encoder, because a zero length here would be indistinguishable from a render that produced
/// nothing at all.
/// </summary>
public sealed record RenderResult(string OutputPath, long LengthInBytes);

public interface IRenderExecutor
{
    Task<RenderResult> RenderAsync(
        RenderManifest manifest,
        IReadOnlyDictionary<Guid, string> assetObjectKeys,
        string outputPath,
        CancellationToken cancellationToken);
}

/// <summary>
/// Turns a reviewed render manifest into a playable file by handing the sources to FFmpeg. The manifest
/// is validated again here even though the workflow already validated it: this is the last point before
/// an external process acts on the timings, and a manifest that reached storage must not be trusted
/// because it was valid on the day it was written.
/// </summary>
public sealed class FfmpegRenderExecutor(IObjectStorage storage, string ffmpegPath) : IRenderExecutor
{
    /// <summary>How much of FFmpeg's stderr to keep when it fails. Enough to diagnose, not enough to flood a log.</summary>
    private const int DiagnosticTailLength = 4000;

    public async Task<RenderResult> RenderAsync(
        RenderManifest manifest,
        IReadOnlyDictionary<Guid, string> assetObjectKeys,
        string outputPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assetObjectKeys);
        RenderManifestValidator.Validate(manifest);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        var workingDirectory = Path.Combine(Path.GetTempPath(), $"creatorrizz-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        try
        {
            var clips = await MaterializeClipsAsync(manifest, assetObjectKeys, workingDirectory, cancellationToken);
            var voicePath = Path.Combine(workingDirectory, "voice");
            await CopyToFileAsync(manifest.VoiceObjectKey, voicePath, cancellationToken);

            var subtitlePath = Path.Combine(workingDirectory, "captions.srt");
            await File.WriteAllTextAsync(subtitlePath, SrtSubtitleWriter.Write(manifest.Captions), cancellationToken);

            var stderr = await RunAsync(
                FfmpegArguments.Build(manifest, clips, voicePath, subtitlePath, outputPath), cancellationToken);

            if (!File.Exists(outputPath)) throw new InvalidOperationException($"FFmpeg reported success but wrote no file.{Environment.NewLine}{stderr}");
            return new RenderResult(outputPath, new FileInfo(outputPath).Length);
        }
        finally
        {
            TryDelete(workingDirectory);
        }
    }

    private async Task<IReadOnlyCollection<RenderInput>> MaterializeClipsAsync(
        RenderManifest manifest,
        IReadOnlyDictionary<Guid, string> assetObjectKeys,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var inputs = new List<RenderInput>();
        foreach (var clip in manifest.Clips)
        {
            // The manifest names assets, not storage keys, so the mapping is resolved here. A clip the
            // production does not own is refused rather than rendered from whatever key was supplied.
            if (!assetObjectKeys.TryGetValue(clip.AssetId, out var objectKey))
                throw new WorkflowRuleViolation($"Clip references asset {clip.AssetId}, which is not attached to this production.");

            // Object keys are content hashes with no extension, so the format has to come from probing
            // the bytes. Naming the files by position keeps the mapping back to the manifest obvious.
            var path = Path.Combine(workingDirectory, $"clip-{inputs.Count:D3}");
            await CopyToFileAsync(objectKey, path, cancellationToken);
            inputs.Add(new RenderInput(clip.AssetId, path, clip.StartMilliseconds, clip.EndMilliseconds, clip.TimelineStartMilliseconds));
        }
        return inputs;
    }

    private async Task CopyToFileAsync(string objectKey, string path, CancellationToken cancellationToken)
    {
        await using var source = await storage.OpenReadAsync(objectKey, cancellationToken);
        await using var destination = File.Create(path);
        await source.CopyToAsync(destination, cancellationToken);
    }

    /// <summary>
    /// Runs FFmpeg and returns its stderr. FFmpeg reports every problem on stderr, so the exit code
    /// alone is not enough to tell a render apart from a warning-laden success.
    /// </summary>
    private async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"FFmpeg could not be started from '{ffmpegPath}'. Install it and set Rendering:FfmpegPath to the binary.");

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0) throw new InvalidOperationException($"FFmpeg exited with {process.ExitCode}.{Environment.NewLine}{Tail(stderr)}");
        return Tail(stderr);
    }

    private static string Tail(string stderr) =>
        stderr.Length <= DiagnosticTailLength ? stderr : stderr[^DiagnosticTailLength..];

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a completed render over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
