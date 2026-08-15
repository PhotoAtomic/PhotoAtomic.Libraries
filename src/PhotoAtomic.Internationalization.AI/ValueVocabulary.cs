namespace PhotoAtomic;

/// <summary>
/// What the already-translated VALUES of a language look like: which trait
/// combinations actually occur, and a real word for each one.
///
/// This is what lets sentence translation stay language-agnostic. We cannot
/// know how a language reacts to the words dropped into its holes — Italian
/// wants "lo specchio" but "il tavolo", Hungarian harmonises its suffixes to
/// the vowels of the word, Irish lenites after certain articles — and no
/// mechanical post-processing will ever cover that. So instead of guessing,
/// we observe: the traits the value translations declared become the cases to
/// ask for, and a concrete example of each is handed to the model, which is
/// the one party that actually knows the grammar.
/// </summary>
public sealed class ValueVocabulary
{
    private readonly Dictionary<string, List<(string[] Traits, string Example)>> byLanguage = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds the vocabulary from every VALUE row in a store (keys without holes).</summary>
    public static ValueVocabulary FromStore(ITranslationStore store) =>
        FromRows(store.LoadAll());

    public static ValueVocabulary FromRows(IEnumerable<TranslationRow> rows)
    {
        var vocabulary = new ValueVocabulary();

        foreach (var row in rows.Where(row => !row.Key.Contains('{')))
        {
            var traits = Split(row.Traits);
            if (traits.Length == 0)
            {
                continue; // nothing declared: it teaches us nothing about the language
            }

            var states = vocabulary.byLanguage.TryGetValue(row.Language, out var known)
                ? known
                : vocabulary.byLanguage[row.Language] = [];

            if (!states.Any(state => Same(state.Traits, traits)))
            {
                states.Add((traits, row.Template));
            }
        }

        foreach (var states in vocabulary.byLanguage.Values)
        {
            // Fewest traits first: the plain cases lead, the special ones follow.
            states.Sort((left, right) => left.Traits.Length != right.Traits.Length
                ? left.Traits.Length - right.Traits.Length
                : string.CompareOrdinal(string.Join(',', left.Traits), string.Join(',', right.Traits)));
        }

        return vocabulary;
    }

    /// <summary>The trait combinations observed in a language, each with a word that carries them.</summary>
    public IReadOnlyList<(string[] Traits, string Example)> StatesOf(string language) =>
        byLanguage.TryGetValue(language, out var states) ? states : [];

    /// <summary>A word carrying exactly these traits, when the language has one.</summary>
    public string? ExampleOf(string language, IEnumerable<string> traits)
    {
        var wanted = traits.ToArray();
        return StatesOf(language).FirstOrDefault(state => Same(state.Traits, wanted)).Example;
    }

    private static string[] Split(string? traits) =>
        (traits ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(trait => trait, StringComparer.Ordinal)
            .ToArray();

    private static bool Same(string[] left, string[] right) =>
        left.Length == right.Length && !left.Except(right, StringComparer.Ordinal).Any();
}
