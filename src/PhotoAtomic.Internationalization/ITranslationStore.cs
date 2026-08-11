namespace PhotoAtomic;

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
