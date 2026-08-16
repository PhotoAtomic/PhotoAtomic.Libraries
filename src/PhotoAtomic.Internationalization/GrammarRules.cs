using System.Globalization;
using System.Text;

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
    /// <summary>
    /// Whether a translated value begins with a vowel sound, i.e. whether the
    /// sentence around it must elide ("l'acqua", "l'eau"). Mechanical, so it
    /// is derived instead of asked: models forget to declare the trait, and a
    /// missing trait silently loses the elided rows. Latin-script vowels, with
    /// the silent H of Italian, French and Spanish; other languages keep the
    /// plain vowel test, which is a safe approximation.
    /// </summary>
    public static bool StartsWithVowelSound(string text, string language)
    {
        var trimmed = text.TrimStart('¡', '¿', '"', '\'', '«', '(', ' ');
        if (trimmed.Length == 0)
        {
            return false;
        }

        var first = char.ToLowerInvariant(trimmed[0]);
        if (first == 'h' && SilentH.Contains(TwoLetter(language)))
        {
            return trimmed.Length > 1 && IsVowel(char.ToLowerInvariant(trimmed[1]));
        }

        return IsVowel(first);
    }

    /// <summary>
    /// Elides the little words that must contract before a vowel — "la acqua"
    /// becomes "l'acqua", "de eau" becomes "d'eau". Mechanical and local, so
    /// the engine does it after rendering instead of asking translators for a
    /// variant row per vowel: the rows stay few and the model keeps its
    /// attention for gender and meaning, where judgement is really needed.
    /// Languages without elision are left untouched.
    /// </summary>
    public static string ApplyElision(string text, string language)
    {
        if (!Elisions.TryGetValue(TwoLetter(language), out var pairs) || text.Length == 0)
        {
            return text;
        }

        var result = text;
        foreach (var (word, elided) in pairs)
        {
            result = ElideWord(result, word, elided, language);
        }

        return result;
    }

    // Word -> its elided form, per language. Ordered longest-first so that
    // "nella" is tried before "la" would ever see it.
    private static readonly Dictionary<string, (string Word, string Elided)[]> Elisions = new(StringComparer.Ordinal)
    {
        ["it"] =
        [
            ("nella", "nell'"), ("della", "dell'"), ("sulla", "sull'"), ("alla", "all'"), ("dalla", "dall'"),
            ("nello", "nell'"), ("dello", "dell'"), ("sullo", "sull'"), ("allo", "all'"), ("dallo", "dall'"),
            ("nel", "nell'"), ("del", "dell'"), ("sul", "sull'"), ("al", "all'"), ("dal", "dall'"),
            ("una", "un'"), ("la", "l'"), ("lo", "l'"), ("il", "l'"),
        ],
        ["fr"] =
        [
            ("que", "qu'"), ("ne", "n'"), ("de", "d'"), ("le", "l'"), ("la", "l'"), ("je", "j'"), ("se", "s'"),
        ],
    };

    /// <summary>Replaces "word " with its elided form when the next word opens with a vowel sound.</summary>
    private static string ElideWord(string text, string word, string elided, string language)
    {
        var result = new StringBuilder(text.Length);
        var index = 0;

        while (index < text.Length)
        {
            var found = text.IndexOf(word + " ", index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                result.Append(text, index, text.Length - index);
                break;
            }

            var next = found + word.Length + 1;
            var standsAlone = found == 0 || !char.IsLetter(text[found - 1]);

            if (standsAlone && next < text.Length && StartsWithVowelSound(text[next..], language))
            {
                result.Append(text, index, found - index);

                // Keep the original capitalization of the article ("La" -> "L'").
                result.Append(char.IsUpper(text[found])
                    ? char.ToUpperInvariant(elided[0]) + elided[1..]
                    : elided);

                index = next;
                continue;
            }

            result.Append(text, index, next - index);
            index = next;
        }

        return result.ToString();
    }

    private static readonly string[] SilentH = ["it", "fr", "es", "pt", "ca"];

    private static bool IsVowel(char letter) =>
        letter is 'a' or 'e' or 'i' or 'o' or 'u'
        // Accented forms count: "è", "à", "î" open a word just as plainly.
        || "àáâãäåèéêëìíîïòóôõöùúûü".Contains(letter);

    private static string TwoLetter(string language) =>
        language.Length >= 2 ? language[..2].ToLowerInvariant() : language.ToLowerInvariant();

    /// <summary>
    /// Which holes of a template open a sentence — the start of the text, or
    /// anything after a full stop.
    ///
    /// This is where the capital goes, and NOWHERE ELSE. The reason automatic
    /// capitalization exists at all is that values are stored lowercase, so
    /// that "la chiave" reads right in the middle of a sentence; a value that
    /// lands at the front therefore needs a capital nobody wrote. The TEMPLATE
    /// needs nothing: someone — an author, a translator, a model — already
    /// decided how it opens, and rewriting their first letter is a silent
    /// correction of a deliberate choice. It also hid their mistakes: a
    /// translation that opened in lowercase used to be quietly patched here
    /// instead of being reported by the lint, which is the part of the system
    /// whose job is judging.
    ///
    /// Quotes and brackets stay transparent, so a value opening inside them is
    /// still an opening ("«{0}» disse lei").
    /// </summary>
    public static IReadOnlyList<int> HolesOpeningASentence(string template)
    {
        var opening = new List<int>();
        var atSentenceStart = true;

        for (var i = 0; i < template.Length; i++)
        {
            var current = template[i];

            // A doubled brace is a literal one: punctuation, hence transparent.
            if (current is '{' or '}' && i + 1 < template.Length && template[i + 1] == current)
            {
                i++;
                continue;
            }

            if (current == '{' && template.IndexOf('}', i + 1) is var close and >= 0)
            {
                var inside = template[(i + 1)..close];
                var digits = new string(inside.TakeWhile(char.IsDigit).ToArray());

                if (digits.Length > 0)
                {
                    if (atSentenceStart)
                    {
                        opening.Add(int.Parse(digits));
                    }

                    atSentenceStart = false;
                    i = close;
                    continue;
                }
            }

            if (current is '.' or '!' or '?')
            {
                atSentenceStart = true;
                continue;
            }

            if (char.IsWhiteSpace(current) || char.IsPunctuation(current) || char.IsSymbol(current))
            {
                continue;
            }

            atSentenceStart = false;
        }

        return opening;
    }

    /// <summary>
    /// A value about to open a sentence, capitalized. Leading punctuation is
    /// stepped over — a value may arrive quoted — and a value that starts with
    /// a digit opens the sentence as it is.
    /// </summary>
    public static string CapitalizeInitial(string value, string language)
    {
        var culture = ResolveCulture(language);

        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsLetter(value[i]))
            {
                return value[..i] + culture.TextInfo.ToUpper(value[i]) + value[(i + 1)..];
            }

            if (!char.IsWhiteSpace(value[i]) && !char.IsPunctuation(value[i]) && !char.IsSymbol(value[i]))
            {
                break; // a digit, or anything else that is not a letter to raise
            }
        }

        return value;
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
