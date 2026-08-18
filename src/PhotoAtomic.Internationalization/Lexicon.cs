namespace PhotoAtomic;

/// <summary>
/// Which settled terms are worth putting in front of the model when it
/// translates the next one.
///
/// Not all of them: a model told to reuse a dozen unrelated words starts
/// working them in, and a mortar has nothing to say about a bundle of herbs.
/// The ones that matter are those sharing a word with the key, because that
/// shared word is exactly what can come back translated two different ways.
/// </summary>
public static class Lexicon
{
    /// <summary>The settled terms that could clash with this key, in the order they were settled.</summary>
    public static IReadOnlyList<GlossaryTerm> RelevantTo(IEnumerable<GlossaryTerm> settled, string key) =>
        [.. settled.Where(term => SharesAWord(term.Source, key))];

    /// <summary>
    /// Whether two names share a word worth reusing. Short words carry no
    /// meaning of their own here ("of", "the"), and a shared "of" would drag in
    /// every name in the room.
    /// </summary>
    public static bool SharesAWord(string one, string other) =>
        Words(one).Intersect(Words(other), StringComparer.OrdinalIgnoreCase).Any();

    private static IEnumerable<string> Words(string name) =>
        name.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 2);
}
