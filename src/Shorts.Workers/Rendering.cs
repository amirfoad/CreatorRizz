using System.Diagnostics;
using Shorts.Domain;

namespace Shorts.Workers;

public interface IRenderExecutor
{
    Task<RenderResult> RenderAsync(RenderManifest manifest, string outputPath, CancellationToken cancellationToken);
}

public sealed record RenderResult(string OutputPath, TimeSpan Duration);

public sealed class FfmpegRenderExecutor(string ffmpegPath) : IRenderExecutor
{
    public async Task<RenderResult> RenderAsync(RenderManifest manifest, string outputPath, CancellationToken cancellationToken)
    {
        RenderManifestValidator.Validate(manifest);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-version");
        process.Start();
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException("FFmpeg preflight failed.");
        throw new NotSupportedException("Media assembly is enabled after FFmpeg is installed and object storage is connected.");
    }
}
