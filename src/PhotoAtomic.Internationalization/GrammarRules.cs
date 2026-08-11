using System.Globalization;

namespace PhotoAtomic;

/// <summary>
/// Deterministic per-language grammar mechanics that no translation row can
/// express, applied after the template is rendered. Today: sentence-position
/// capitalization — the letter opening the sentence and every letter after
/// sentence-ending punctuation goes uppercase. Scripts without letter case
/// are naturally unaffected; a sentence opening with a digit stays untouched
/// ("2 chiavi rotte").
/// </summary>
public static class GrammarRules
{
    public static string ApplySentenceCapitalization(string text, string language)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var culture = ResolveCulture(language);
        var characters = text.ToCharArray();
        var atSentenceStart = true;

        for (var i = 0; i < characters.Length; i++)
        {
            var current = characters[i];

            if (current is '.' or '!' or '?')
            {
                atSentenceStart = true;
                continue;
            }

            if (!atSentenceStart || char.IsWhiteSpace(current))
            {
                continue;
            }

            // Quotes, brackets and other punctuation are transparent: the
            // sentence can open inside them ("la porta" -> "La porta").
            if (char.IsPunctuation(current) || char.IsSymbol(current))
            {
                continue;
            }

            // The first substantial character decides: a letter gets uppercased,
            // anything else (a digit, a symbol) opens the sentence as-is.
            if (char.IsLetter(current))
            {
                characters[i] = culture.TextInfo.ToUpper(current);
            }

            atSentenceStart = false;
        }

        return new string(characters);
    }

    private static CultureInfo ResolveCulture(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
