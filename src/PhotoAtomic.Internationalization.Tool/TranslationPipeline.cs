namespace PhotoAtomic;

/// <summary>What a whole pass did, in the order it did it.</summary>
public sealed record PipelineReport(
    int Pruned,
    int Values,
    int Sentences,
    int Unreachable,
    int Repaired,
    int Rounds,
    IReadOnlyList<LintFinding> Remaining,
    IReadOnlyList<(string Key, string Language)> Missing)
{
    /// <summary>Nothing left for a human to look at.</summary>
    public bool IsClean => Remaining.Count == 0 && Missing.Count == 0 && Unreachable == 0;
}

/// <summary>
/// Everything the tool knows how to do, in the one order that makes sense —
/// so that a project which has just adopted T() needs a single command and not
/// a memorised sequence.
///
/// The order is not arbitrary:
///
/// 1. PRUNE first, because a table full of sentences the code no longer says
///    makes every later count meaningless.
/// 2. VALUES before SENTENCES. This is the one that surprises: a sentence asks
///    for the grammatical cases its holes can actually produce, and those come
///    from the values already translated — the vocabulary. Fill a cold table in
///    one pass and the sentences are written while the vocabulary is still
///    empty, so nothing is ever declined and the whole point of the engine is
///    lost. Translating the nouns first is what lets the sentences ask "and now
///    the feminine one, starting with a vowel".
/// 3. LINT and FIX until it stops improving. One round is not enough: repairing
///    a value changes the corpus every sentence naming it is judged against.
///    Rounds stop when nothing is left, when nothing improved, or at the cap —
///    never on a promise that the next round would have got it.
/// 4. What is STILL wrong goes to the end of the table, where a human opens the
///    file and sees it first. Whole units move together (all the rows of one
///    key in one language): among rows with the same criteria the last one
///    wins, and those rows always live in the same unit — so moving units is
///    safe, while moving single rows could quietly change which one applies.
/// </summary>
public sealed class TranslationPipeline(
    Func<ITranslator> translatorFactory,
    ITranslationStore store)
{
    /// <summary>
    /// Enough rounds for a repair to settle, few enough that a model which
    /// cannot fix something stops being asked. Three was the observed shape:
    /// most units come back right the first time, the rest do not come back
    /// right at all.
    /// </summary>
    private const int MaxRounds = 3;

    public async Task<PipelineReport> RunAsync(
        IReadOnlyList<CatalogEntry> entries,
        IReadOnlyList<string> languages,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var sentenceKeys = entries
            .Where(entry => entry.Kind == CatalogEntryKind.Sentence)
            .Select(entry => entry.Key)
            .ToList();

        var pruned = Prune(sentenceKeys, log);

        // Values first: they are the vocabulary the sentences will be declined
        // against. A fresh translator between the two phases is what lets the
        // second one see the first one's work.
        log?.Invoke("Translating values...");
        var values = await new CatalogFiller(translatorFactory(), store).FillAsync(
            [.. entries.Where(entry => entry.Kind != CatalogEntryKind.Sentence)],
            languages, log: log, cancellationToken: cancellationToken);

        log?.Invoke("Translating sentences...");
        var sentences = await new CatalogFiller(translatorFactory(), store).FillAsync(
            [.. entries.Where(entry => entry.Kind == CatalogEntryKind.Sentence)],
            languages, log: log, cancellationToken: cancellationToken);

        var (repaired, rounds, remaining) = await SettleAsync(
            entries, sentenceKeys, log, cancellationToken);

        MoveToEnd(remaining, log);

        var missing = CatalogVerifier.Verify(entries, languages, store).Missing;

        return new PipelineReport(
            pruned,
            values.Translated,
            sentences.Translated,
            values.Failed + sentences.Failed,
            repaired,
            rounds,
            remaining,
            missing);
    }

    /// <summary>Rows for sentences nobody says any more; values are never touched.</summary>
    private int Prune(IReadOnlyList<string> sentenceKeys, Action<string>? log)
    {
        // A catalog with no sentences at all is a catalog that failed to load,
        // and pruning against it would delete the entire table.
        if (sentenceKeys.Count == 0 || store is not IRewritableTranslationStore rewritable)
        {
            return 0;
        }

        var rows = store.LoadAll().ToList();
        var dead = TranslationLint.Inspect(rows, sentenceKeys)
            .Where(finding => finding.Rule == TranslationLint.Rules.OrphanRow)
            .Select(finding => finding.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (dead.Count == 0)
        {
            return 0;
        }

        var kept = rows.Where(row => !dead.Contains(row.Key)).ToList();
        rewritable.ReplaceAll(kept);

        log?.Invoke($"Pruned {rows.Count - kept.Count} row(s) of {dead.Count} sentence(s) the code no longer says.");
        return rows.Count - kept.Count;
    }

    /// <summary>
    /// Lint, repair, lint again — until the count stops falling. Measuring
    /// after every round is the whole discipline: a repair pass that reports
    /// what it attempted rather than what it achieved is how a table stays
    /// broken while everyone believes otherwise.
    /// </summary>
    private async Task<(int Repaired, int Rounds, IReadOnlyList<LintFinding> Remaining)> SettleAsync(
        IReadOnlyList<CatalogEntry> entries,
        IReadOnlyList<string> sentenceKeys,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var findings = TranslationLint.Inspect(store.LoadAll(), sentenceKeys);
        var repaired = 0;
        var rounds = 0;

        while (findings.Count > 0 && rounds < MaxRounds)
        {
            rounds++;
            log?.Invoke($"Round {rounds}: {Count(findings)} to repair.");

            var repair = new TranslationRepair(translatorFactory(), store);
            var report = await repair.RepairAsync(findings, entries, sentenceKeys, log, cancellationToken);
            repaired += report.Locally + report.Reasked;

            var after = TranslationLint.Inspect(store.LoadAll(), sentenceKeys);
            if (after.Count >= findings.Count)
            {
                // No progress. Another round would ask the same questions and
                // get the same answers; what is left is for a human.
                findings = after;
                break;
            }

            findings = after;
        }

        log?.Invoke($"After {rounds} round(s): {Count(findings)} left.");
        return (repaired, rounds, findings);
    }

    /// <summary>
    /// The suspect units at the end of the file, so whoever opens it lands on
    /// the work. Rows keep their order within a unit and units keep theirs
    /// among themselves, which makes a rerun that changes nothing produce no
    /// diff at all.
    /// </summary>
    private void MoveToEnd(IReadOnlyList<LintFinding> findings, Action<string>? log)
    {
        if (findings.Count == 0 || store is not IRewritableTranslationStore rewritable)
        {
            return;
        }

        var suspect = findings
            .Select(finding => (finding.Key, finding.Language))
            .ToHashSet();

        var rows = store.LoadAll().ToList();
        var clean = rows.Where(row => !suspect.Contains((row.Key, row.Language))).ToList();
        if (clean.Count == rows.Count)
        {
            return;
        }

        rewritable.ReplaceAll([.. clean, .. rows.Where(row => suspect.Contains((row.Key, row.Language)))]);
        log?.Invoke($"Moved {rows.Count - clean.Count} row(s) needing a human to the end of the table.");
    }

    private static string Count(IReadOnlyList<LintFinding> findings)
    {
        var errors = findings.Count(finding => finding.Severity == LintSeverity.Error);
        return $"{errors} error(s), {findings.Count - errors} warning(s)";
    }
}
