namespace PhotoAtomic;

/// <summary>
/// The conventional trait vocabulary. Tags are free strings — the engine only
/// matches them — but rows, prompts and code must agree on spelling, and these
/// constants are the single place where the convention lives.
/// </summary>
public static class WellKnownTraits
{
    /// <summary>Grammatical gender of a translated value.</summary>
    public const string GenderMale = "GENDER-male";

    /// <summary>Grammatical gender of a translated value.</summary>
    public const string GenderFemale = "GENDER-female";

    /// <summary>The translated value starts with a vowel sound (elision: "l'arancia").</summary>
    public const string StartsWithVowel = "starts-with-vowel";

    /// <summary>
    /// The translated value keeps an uppercase initial wherever it appears —
    /// proper names, or nouns in languages that capitalize them (German).
    /// Values without this trait are lowercase; sentence-position capitalization
    /// is applied mechanically by <see cref="GrammarRules"/>.
    /// </summary>
    public const string Capitalize = "Capitalize";
}
