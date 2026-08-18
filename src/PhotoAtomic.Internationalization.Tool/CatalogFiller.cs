using System.Collections.Concurrent;

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

        // What each scene has already decided, per language. Seeded from the
        // table so a room translated in an earlier run leads this one too:
        // otherwise a room drifts one session at a time, which is the same
        // defect spread over days instead of minutes.
        var settled = Settled(entries, store);

        using var throttle = new SemaphoreSlim(maxParallelism);

        // Units of one scene go in ORDER, so each is told what the ones before
        // it settled on. Everything else keeps the parallelism it had: a scene
        // is a chain, the chains run side by side, and content with no scene at
        // all (code, which has no such thing) is a chain of one.
        var chains = work
            .GroupBy(pair => (Scene: pair.entry.Setting ?? pair.entry.Key, pair.language))
            .ToList();

        var tasks = chains.Select(async chain =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                foreach (var pair in chain)
                {
                    await Fill(pair);
                }
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);

        return new FillReport(translated, skipped, failed);

        // One list per scene and language, created on first sight. Chains for
        // different scenes run side by side, so the map itself must tolerate
        // that; within a chain the list is touched by one thread at a time.
        List<GlossaryTerm> Known(string scene, string language) =>
            settled.GetOrAdd((scene, language), _ => []);

        async Task Fill((CatalogEntry entry, string language) pair)
        {
            try
            {
                var scene = pair.entry.Setting;
                var known = scene is null
                    ? []
                    : Lexicon.RelevantTo(Known(scene, pair.language), pair.entry.Key);

                var request = new TranslationRequest(
                    pair.entry.Key,
                    sourceLanguage,
                    pair.language,
                    pair.entry.Legend,
                    pair.entry.Facts)
                {
                    Setting = scene,
                    Glossary = known,
                    Kind = pair.entry.Kind,
                };

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

                if (pair.entry.Setting is { } here
                    && pair.entry.Kind == CatalogEntryKind.Value
                    && Generic(rows) is { } chosen)
                {
                    Known(here, pair.language).Add(new GlossaryTerm(pair.entry.Key, chosen));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Interlocked.Increment(ref failed);
                log?.Invoke($"  [{pair.language}] {pair.entry.Key} -> FAILED: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// What every scene has already settled on, taken from the table before a
    /// single call is made. A name translated last week must lead this run as
    /// firmly as one translated a second ago.
    /// </summary>
    private static ConcurrentDictionary<(string Scene, string Language), List<GlossaryTerm>> Settled(
        IReadOnlyList<CatalogEntry> entries,
        ITranslationStore store)
    {
        var rows = store.LoadAll().ToList();
        var settled = new ConcurrentDictionary<(string, string), List<GlossaryTerm>>();

        foreach (var entry in entries.Where(entry => entry.Setting is not null))
        {
            foreach (var row in rows.Where(row =>
                string.Equals(row.Key, entry.Key, StringComparison.Ordinal)
                && entry.Kind == CatalogEntryKind.Value))
            {
                var scene = (entry.Setting!, row.Language);
                var known = settled.GetOrAdd(scene, _ => []);

                if (!known.Any(term => term.Source == entry.Key))
                {
                    known.Add(new GlossaryTerm(entry.Key, row.Template));
                }
            }
        }

        return settled;
    }

    /// <summary>The plain row — no criteria — which is the one that stands for the term itself.</summary>
    private static string? Generic(IReadOnlyList<TranslationRow> rows) =>
        rows.FirstOrDefault(row => string.IsNullOrEmpty(row.Context))?.Template
        ?? rows.FirstOrDefault()?.Template;
}
