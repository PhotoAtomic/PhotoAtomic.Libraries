namespace PhotoAtomic;

/// <summary>
/// A store that can also FORGET. Appending is enough to translate and to
/// correct — the last row wins — but not to remove: a sentence the code no
/// longer says, or a variant a repair replaced with fewer rows than it found,
/// stays in the table forever and keeps being matched.
///
/// Kept apart from <see cref="ITranslationStore"/> because most stores have no
/// business deleting, and because a caller should have to ask for it by name.
/// </summary>
public interface IRewritableTranslationStore : ITranslationStore
{
    /// <summary>Replaces the whole table with these rows, in this order.</summary>
    void ReplaceAll(IEnumerable<TranslationRow> rows);
}

/// <summary>
/// One persisted translation. Context carries the row's criteria as a
/// comma-separated list ("menu,0:one,1:female"; null = generic row, always a
/// candidate). Traits carries the facts the row declares about its own text
/// once chosen ("female,starts-with-vowel"), which flow into the matching of
/// the sentence above it.
/// </summary>
public sealed record TranslationRow(
    string Key,
    string? Context,
    string Language,
    string Template,
    string? Traits = null);

/// <summary>
/// Pluggable persistence for translations — a text file today, a database or a
/// remote service tomorrow. Implementations only enumerate what they have and
/// accept new rows; the in-memory table inside Internationalization remains the
/// fast path for lookups.
/// </summary>
public interface ITranslationStore
{
    /// <summary>Every stored row, in storage order; later rows win over earlier ones.</summary>
    IEnumerable<TranslationRow> LoadAll();

    /// <summary>Persists one row. Called on every registration, including future AI fills.</summary>
    void Save(TranslationRow row);
}
