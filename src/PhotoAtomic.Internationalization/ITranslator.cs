namespace PhotoAtomic;

/// <summary>
/// Everything a translator — human tooling or AI — needs to translate one key:
/// the source template, the languages, the legend naming each hole, and the
/// facts of the call that missed (context tags, CLDR categories, traits).
/// </summary>
public sealed record TranslationRequest(
    string Key,
    string SourceLanguage,
    string TargetLanguage,
    IReadOnlyList<string> Legend,
    IReadOnlyList<string> Facts);

/// <summary>
/// Produces translation rows for a missing key. Implementations may return
/// several rows (e.g. one per CLDR plural category) with criteria and traits;
/// the engine registers them all and persists them through the attached store.
/// </summary>
public interface ITranslator
{
    Task<IReadOnlyList<TranslationRow>> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default);
}
