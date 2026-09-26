using System.Security.Cryptography;
using System.Text;

namespace CreatorRizz.Domain;

/// <summary>
/// Identifies a story independently of the URL it was published under, so the same headline found in
/// two feeds becomes one candidate. This is a heuristic on the headline, not proof of identity: two
/// unrelated stories with an identical headline collide and one is dropped, and a headline rewritten
/// between feeds does not collide at all. It is recorded on the candidate so a dropped story can be
/// traced back to the collision.
/// </summary>
public static class CandidateFingerprint
{
    public static string From(string title, string? creator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var normalized = $"{Normalize(title)}|{Normalize(creator)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        var previousWasSeparator = false;
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                builder.Append(' ');
                previousWasSeparator = true;
            }
        }
        return builder.ToString().Trim();
    }
}
