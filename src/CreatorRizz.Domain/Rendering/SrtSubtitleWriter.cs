using System.Text;

namespace CreatorRizz.Domain;

public static class SrtSubtitleWriter
{
    public static string Write(IReadOnlyCollection<CaptionCue> captions)
    {
        CaptionCueValidator.EnsureValid(captions);
        var builder = new StringBuilder();
        foreach (var (caption, index) in captions.OrderBy(caption => caption.StartMilliseconds).Select((caption, index) => (caption, index + 1)))
        {
            builder.AppendLine(index.ToString());
            builder.Append(FormatTime(caption.StartMilliseconds)).Append(" --> ").AppendLine(FormatTime(caption.EndMilliseconds));
            builder.AppendLine(caption.Text.Replace("\r\n", "\n").Replace('\r', '\n'));
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string FormatTime(int milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(milliseconds);
        return $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2},{time.Milliseconds:D3}";
    }
}
