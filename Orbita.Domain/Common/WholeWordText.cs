using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Orbita.Domain.Common;

/// <summary>
/// Whole-word, case- and accent-insensitive matching of short phrases an owner types —
/// shared by ORB-C06's blocked topics and ORB-C08's routing keywords, so both behave the
/// same way and a word that routes also blocks exactly where you would expect.
///
/// Whole-word because substring matching fires "talla" on "pantalla" and "cita" on
/// "felicitaciones"; accent-insensitive because "diagnostico" typed in a hurry means
/// "diagnóstico". See AgentGuardrailsTests for the cases that pin it.
/// </summary>
public static class WholeWordText
{
    public static bool Contains(string? haystack, string? phrase)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(phrase))
        {
            return false;
        }

        var needle = Normalize(phrase);

        return needle.Length > 0
            && Regex.IsMatch(
                Normalize(haystack),
                $@"(?<![\w]){Regex.Escape(needle)}(?![\w])",
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
    }

    /// <summary>Lowercased, accent-stripped, whitespace-collapsed.</summary>
    public static string Normalize(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(character);
            lastWasSpace = false;
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
