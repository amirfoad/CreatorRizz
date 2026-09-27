using CreatorRizz.Domain;
using CreatorRizz.Infrastructure.Rendering;
using Xunit;

namespace CreatorRizz.Domain.Tests;

/// <summary>
/// Covers the mapping from a reviewed manifest to an FFmpeg command. The encode itself needs a binary
/// and real media, so what is checked here is the part that can be got wrong silently: a clip placed at
/// the wrong time, or an input the filter graph never reads, produces a file that looks fine and is wrong.
/// </summary>
public sealed class FfmpegArgumentTests
{
    private static readonly Guid FirstAsset = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondAsset = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void TheVoiceTrackIsTheFirstInputAndThePictureIsAssembledFromTheRest()
    {
        var arguments = FfmpegArguments.Build(Manifest(), Clips(), "voice", "captions.srt", "out.mp4");

        // Input 0 is the voice, then one input per clip. Everything the filter graph indexes has to line
        // up with that order or the graph silently concatenates the wrong files.
        Assert.Equal("voice", arguments[arguments.ToList().IndexOf("-i") + 1]);
        Assert.Equal(3, arguments.Count(argument => argument == "-i"));
        Assert.Contains("[0:a]", string.Join(" ", arguments));
        Assert.DoesNotContain("[3:v]", string.Join(" ", arguments));
    }

    [Fact]
    public void ClipsAreOrderedByWhereTheySitOnTheTimelineNotByTheOrderTheyWereDeclared()
    {
        var manifest = new RenderManifest(
            Guid.NewGuid(), 1080, 1920,
            [
                new TimelineClip(FirstAsset, 0, 2000, 4000),
                new TimelineClip(SecondAsset, 500, 2500, 0)
            ],
            [new CaptionCue(0, 1000, "first")],
            "voice");

        var arguments = FfmpegArguments.Build(manifest, Clips(), "voice", "captions.srt", "out.mp4");
        var inputs = arguments.Select((argument, index) => (argument, index))
            .Where(pair => pair.argument.StartsWith("clip-", StringComparison.Ordinal))
            .Select(pair => pair.argument)
            .ToArray();

        // The second clip is declared first but starts earlier, so it has to become the first input.
        Assert.Equal(["clip-000", "clip-001"], inputs);
    }

    [Fact]
    public void EveryClipIsTrimmedToTheWindowTheManifestAskedFor()
    {
        var arguments = FfmpegArguments.Build(Manifest(), Clips(), "voice", "captions.srt", "out.mp4");
        var text = string.Join(" ", arguments);

        // -ss 0.500 -t 2.000 for the 500..2500ms window. A seek placed after -i or a missing -t would
        // read the whole source file and produce a timeline that does not match the approved manifest.
        Assert.Contains("-ss 0.500 -t 2.000", text, StringComparison.Ordinal);
        Assert.Contains("-ss 0.000 -t 1.500", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOutputIsThePathTheCallerAskedForAndNotSomethingDerived()
    {
        var arguments = FfmpegArguments.Build(Manifest(), Clips(), "voice", "captions.srt", "out.mp4");

        Assert.Equal("out.mp4", arguments[^1]);
    }

    [Fact]
    public void SubtitlesAreBurnedInAtTheManifestSizeAndTheAudioIsTrimmedToThePicture()
    {
        var text = string.Join(" ", FfmpegArguments.Build(Manifest(), Clips(), "voice", "captions.srt", "out.mp4"));

        Assert.Contains("scale=1080:1920", text, StringComparison.Ordinal);
        Assert.Contains("subtitles='captions.srt'", text, StringComparison.Ordinal);
        // The longer clip ends at 4.5s on the timeline, so the voice is cut there rather than allowed to
        // run past the last frame.
        Assert.Contains("atrim=0:4.500", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AWindowsSubtitlePathIsEscapedBecauseTheFilterParserReadsAColonAsAStreamSpecifier()
    {
        var arguments = FfmpegArguments.Build(Manifest(), Clips(), "voice", @"C:\temp\creatorrizz\captions.srt", "out.mp4");

        Assert.Contains(@"subtitles='C\:/temp/creatorrizz/captions.srt'", string.Join(" ", arguments), StringComparison.Ordinal);
    }

    [Fact]
    public void AClipThatIsNotInTheManifestIsRefusedBeforeAnyProcessStarts()
    {
        var manifest = Manifest();
        var clips = Clips().Append(new RenderInput(Guid.NewGuid(), "clip-002", 0, 1000, 6000)).ToArray();

        var failure = Assert.Throws<WorkflowRuleViolation>(() => FfmpegArguments.Build(manifest, clips, "voice", "captions.srt", "out.mp4"));

        Assert.Contains("manifest", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnInvalidManifestNeverReachesTheCommandLine()
    {
        var manifest = new RenderManifest(Guid.NewGuid(), 1920, 1080, [new TimelineClip(FirstAsset, 0, 1000, 0)], [], "voice");

        Assert.Throws<WorkflowRuleViolation>(() => FfmpegArguments.Build(manifest, Clips(), "voice", "captions.srt", "out.mp4"));
    }

    private static RenderManifest Manifest() => new(
        Guid.NewGuid(), 1080, 1920,
        [
            new TimelineClip(FirstAsset, 0, 1500, 0),
            new TimelineClip(SecondAsset, 500, 2500, 2500)
        ],
        [new CaptionCue(0, 1500, "a line of narration")],
        "voice");

    private static IReadOnlyCollection<RenderInput> Clips() =>
    [
        new RenderInput(FirstAsset, "clip-000", 0, 1500, 0),
        new RenderInput(SecondAsset, "clip-001", 500, 2500, 2500)
    ];
}
