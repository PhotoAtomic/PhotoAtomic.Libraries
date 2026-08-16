using System.Globalization;

namespace PhotoAtomic;

/// <summary>
/// A number that wants to be written in WORDS: "two coins" rather than "2
/// coins", "due monete", "dà dhà bhonn".
///
/// <code>
/// T($"You found {Spelled(count)} coins")
/// </code>
///
/// It is a translatable VALUE, so the words come from the same table as
/// everything else: a row for "2" in Italian says "due", and a language that
/// spells numbers differently — or does not spell them at all — decides for
/// itself without a line of code changing. Nobody hard-codes number words,
/// least of all in a language nobody on the team speaks.
///
/// The thing it must not lose on the way is its PLURAL CATEGORY. A number in
/// a hole tells the sentence around it how to agree, and wrapping it in a type
/// would normally hide that: "{0} coins" would stop knowing it holds a two.
/// So <see cref="PluralRules.CategoryOf"/> unwraps this on sight, and the
/// sentence gets its fact exactly as it would from a bare int. It matters
/// most where it is least expected — Scottish Gaelic has a category for TWO,
/// and puts the noun after it in the lenited singular.
///
/// Untranslated, it renders as the digits it came from: a sentence with a
/// number in it is never wrong, only less charming.
/// </summary>
[Translatable("text")]
public readonly record struct Spelled(decimal Amount) : IFormattable
{
    public static implicit operator Spelled(int amount) => new(amount);

    public static implicit operator Spelled(long amount) => new(amount);

    public static implicit operator Spelled(decimal amount) => new(amount);

    public static implicit operator Spelled(double amount) => new((decimal)amount);

    /// <summary>
    /// The digits, which are also the KEY the word is looked up by: the row
    /// ("2", "text") holds "due". Formatted with the invariant culture on
    /// purpose — the key of a translation must not change with the ambient
    /// language, or "1000" and "1,000" would be two different words to learn.
    /// </summary>
    public override string ToString() => Amount.ToString(CultureInfo.InvariantCulture);

    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();
}
