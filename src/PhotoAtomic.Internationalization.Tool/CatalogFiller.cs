namespace PhotoAtomic;

/// <summary>Outcome of a pre-translation pass.</summary>
public sealed record FillReport(int Translated, int Skipped, int Failed);

/// <summary>
/// Pre-translates a catalog into a store for a set of target languages,
/// delta-style: a (key, language) pair that already has any row in the store
/// is skipped, so reruns only pay for what is new. Failures leave the pair
/// untranslated (the runtime fallback still renders the source language) and
/// are counted, not thrown.
/// </summary>
public sealed class CatalogFiller(ITranslator translator, ITranslationStore store)
{
    public async Task<FillReport> FillAsync(
        IReadOnlyList<CatalogEntry> entries,
        IReadOnlyList<string> languages,
        string sourceLanguage = Internationalization.SourceLanguage,
        Action<string>? log = null,
        int maxParallelism = 4,
        CancellationToken cancellationToken = default)
    {
        var existing = store.LoadAll()
            .Select(row => (row.Key, row.Language))
            .ToHashSet();

        var work = entries
            .SelectMany(entry => languages.Select(language => (entry, language)))
            .Where(pair => !existing.Contains((pair.entry.Key, pair.language)))
            .GroupBy(pair => (pair.entry.Key, pair.language))
            .Select(group => group.First())
            .ToList();

        var skipped = entries.Count * languages.Count - work.Count;
        var translated = 0;
        var failed = 0;

        using var throttle = new SemaphoreSlim(maxParallelism);
        var tasks = work.Select(async pair =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var request = new TranslationRequest(
                    pair.entry.Key,
                    sourceLanguage,
                    pair.language,
                    pair.entry.Legend,
                    pair.entry.Facts);

                var rows = await translator.TranslateAsync(request, cancellationToken);

                // Whether a value keeps its capital is not a matter of taste and
                // not the model's call: a common noun goes lowercase so it reads
                // right inside a sentence, a proper name says so with the trait
                // and keeps it everywhere. Models capitalize "Falò" as readily
                // as "acqua", so the answer is normalized here — and which of
                // the two a value is comes from the CATALOG, because only
                // whoever owns the content knows that a room is a place.
                var proper = pair.entry.Kind == CatalogEntryKind.Value
                    && pair.entry.Facts.Contains(WellKnownTraits.Capitalize, StringComparer.Ordinal);

                foreach (var row in TranslationLint.WithFallback(rows))
                {
                    // Key and language are ours, whatever the translator echoed.
                    var saved = new TranslationRow(pair.entry.Key, row.Context, pair.language, row.Template, row.Traits);

                    store.Save(pair.entry.Kind == CatalogEntryKind.Sentence
                        ? saved
                        : proper
                            ? ValueHygiene.AsProperNoun(saved)
                            : ValueHygiene.AsCommonNoun(saved));
                }

                if (rows.Count > 0)
                {
                    Interlocked.Increment(ref translated);
                    log?.Invoke($"  [{pair.language}] {pair.entry.Key} -> {rows.Count} rows");
                }
                else
                {
                    Interlocked.Increment(ref failed);
                    log?.Invoke($"  [{pair.language}] {pair.entry.Key} -> NO ROWS");
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Interlocked.Increment(ref failed);
                log?.Invoke($"  [{pair.language}] {pair.entry.Key} -> FAILED: {exception.Message}");
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);

        return new FillReport(translated, skipped, failed);
    }
}
