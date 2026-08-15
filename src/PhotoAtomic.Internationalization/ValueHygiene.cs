namespace PhotoAtomic;

/// <summary>
/// What a translated VALUE must look like to work in the engine — the rules
/// that live below any particular application, and that a translator (human or
/// model) forgets now and then.
///
/// They are here rather than in whoever calls the translator because they are
/// facts about THIS engine: a sentence row is chosen by the traits of the
/// values filling its holes, and a trait that never arrives does not degrade
/// gracefully — it makes the whole sentence fall back to the source language.
/// Checking cheaply beats debugging why one object suddenly speaks English.
///
/// Deciding WHICH values are proper names stays with the application: only it
/// knows that "The Pirate Galley" is a place and "iron lever" is a thing.
/// </summary>
public static class ValueHygiene
{
    /// <summary>
    /// Whether a language declines gender, read from the corpus instead of
    /// assumed: if any value already translated into it declares a GENDER
    /// trait, then a value that declares none is a hole. No list of languages
    /// to maintain, and a language nobody predicted is covered the moment its
    /// first gendered value arrives.
    /// </summary>
    public static bool UsesGrammaticalGender(IEnumerable<TranslationRow> corpus, string language) =>
        corpus.Any(row =>
            string.Equals(row.Language, language, StringComparison.OrdinalIgnoreCase)
            && DeclaresGender(row));

    /// <summary>True when any of the rows declares a grammatical gender.</summary>
    public static bool DeclaresGender(IEnumerable<TranslationRow> rows) => rows.Any(DeclaresGender);

    public static bool DeclaresGender(TranslationRow row) =>
        TraitsOf(row).Any(trait => trait.StartsWith(GenderPrefix, StringComparison.Ordinal));

    /// <summary>
    /// A common noun: lowercase unless it insists otherwise. Models return
    /// "Falò" as readily as "acqua", and a capital inside a value survives
    /// everywhere it appears — "there is the Bucket in the chest". The capital
    /// at the start of a sentence is not the value's business: GrammarRules
    /// puts it there.
    /// </summary>
    public static TranslationRow AsCommonNoun(TranslationRow row) =>
        Declares(row, WellKnownTraits.Capitalize) || string.IsNullOrEmpty(row.Template) || !char.IsUpper(row.Template[0])
            ? row
            : row with { Template = char.ToLowerInvariant(row.Template[0]) + row.Template[1..] };

    /// <summary>
    /// A proper name: keeps its capital wherever it lands, and says so with
    /// the trait rather than by hoping nobody lowercases it.
    /// </summary>
    public static TranslationRow AsProperNoun(TranslationRow row) =>
        Declares(row, WellKnownTraits.Capitalize)
            ? row
            : row with
            {
                Traits = string.IsNullOrWhiteSpace(row.Traits)
                    ? WellKnownTraits.Capitalize
                    : $"{row.Traits},{WellKnownTraits.Capitalize}",
            };

    private const string GenderPrefix = "GENDER-";

    private static bool Declares(TranslationRow row, string trait) =>
        TraitsOf(row).Contains(trait, StringComparer.Ordinal);

    private static IEnumerable<string> TraitsOf(TranslationRow row) =>
        (row.Traits ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
