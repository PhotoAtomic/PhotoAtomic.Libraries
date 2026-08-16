namespace PhotoAtomic;

/// <summary>What a repair pass did.</summary>
public sealed record RepairReport(int Locally, int Reasked, int Failed);

/// <summary>
/// The other half of the lint: fixing what it found, and only that.
///
/// Two kinds of repair, and the difference is the point. Some defects are
/// answerable from the table itself — a missing plain row is one of the
/// variants with its criteria dropped — and those cost nothing and cannot go
/// wrong. The rest (a hole that never arrived, a value that would not say its
/// gender) need the model again, so the model is asked ONE row at a time,
/// instead of retranslating a corpus to fix a handful of lines.
/// </summary>
public sealed class TranslationRepair(ITranslator translator, ITranslationStore store)
{
    /// <summary>
    /// Rules a model has to answer for; everything else is either local or not
    /// a defect.
    ///
    /// The two suspicions are here on purpose. A sentence that declines for one
    /// pair of genders and not for another is exactly the defect the whole lint
    /// was written to catch — leaving it unrepaired would report the disease
    /// and refuse the cure. And a value one language calls a proper name while
    /// another does not is a question only the model can answer: dropping the
    /// trait ourselves would silently rename a place.
    /// </summary>
    private static readonly string[] NeedTheModel =
    [
        TranslationLint.Rules.MissingHole,
        TranslationLint.Rules.StrayHole,
        TranslationLint.Rules.GenderlessValue,
        TranslationLint.Rules.InconsistentAgreement,
        TranslationLint.Rules.DisputedCapitalization,
        TranslationLint.Rules.UndeclaredCapital,
    ];

    private const int Attempts = 3;

    public async Task<RepairReport> RepairAsync(
        IReadOnlyList<LintFinding> findings,
        IReadOnlyList<CatalogEntry> entries,
        IReadOnlyList<string> sentenceKeys,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        // A dead sentence is not worth a plain row, let alone a request: the
        // lint says which keys nobody asks for any more, so they are left out
        // of every repair below.
        var orphans = findings
            .Where(finding => finding.Rule == TranslationLint.Rules.OrphanRow)
            .Select(finding => finding.Key)
            .ToHashSet(StringComparer.Ordinal);

        var live = findings.Where(finding => !orphans.Contains(finding.Key)).ToList();

        var locally = Normalize(entries, log) + Localy(live, log);

        // What the model gives back REPLACES the unit it answers for, so the
        // replacements are collected here and written in one go at the end.
        // Appending them would leave the old variants in place: the last row
        // wins only where the new answer happens to carry the same criteria,
        // and a repair that returns three rows where there were five leaves two
        // defects alive under a table that now looks fixed.
        var replaced = new Dictionary<(string Key, string Language), IReadOnlyList<TranslationRow>>();

        var (reasked, failed) = await ReaskAsync(live, entries, sentenceKeys, replaced, log, cancellationToken);

        Commit(replaced);

        return new RepairReport(locally, reasked, failed);
    }

    /// <summary>
    /// Writes the replacements, dropping what they supersede — one rewrite for
    /// the whole pass. A store that cannot forget gets them appended instead:
    /// less correct, still an improvement, and the caller knows which kind of
    /// store it handed over.
    /// </summary>
    private void Commit(Dictionary<(string Key, string Language), IReadOnlyList<TranslationRow>> replaced)
    {
        if (replaced.Count == 0)
        {
            return;
        }

        if (store is not IRewritableTranslationStore rewritable)
        {
            foreach (var row in replaced.Values.SelectMany(rows => rows))
            {
                store.Save(row);
            }

            return;
        }

        var kept = store.LoadAll().Where(row => !replaced.ContainsKey(Unit(row)));
        rewritable.ReplaceAll([.. kept, .. replaced.Values.SelectMany(rows => rows)]);
    }

    private static (string Key, string Language) Unit(TranslationRow row) => (row.Key, row.Language);

    /// <summary>
    /// Every value put back the way the CATALOG says it should be: a proper
    /// name keeps its capital and declares it, a common noun goes lowercase.
    ///
    /// No model is asked, because there is nothing to ask: whoever owns the
    /// content already answered when they emitted the catalog. This exists
    /// because rows outlive the rules that judge them — the table holds
    /// translations written before anyone thought to mark rooms as places, and
    /// a fill that skips what is already there would never revisit them.
    /// </summary>
    private int Normalize(IReadOnlyList<CatalogEntry> entries, Action<string>? log)
    {
        var known = entries
            .Where(entry => entry.Kind == CatalogEntryKind.Value)
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var normalized = 0;

        foreach (var row in store.LoadAll())
        {
            if (!known.TryGetValue(row.Key, out var entry))
            {
                continue; // not ours to judge: a sentence, or content from elsewhere
            }

            var hygienic = Hygienic(row, entry);
            if (hygienic == row)
            {
                continue;
            }

            store.Save(hygienic);
            normalized++;
            log?.Invoke($"  [{row.Language}] {row.Key} -> \"{hygienic.Template}\""
                + (hygienic.Traits == row.Traits ? string.Empty : $" [{hygienic.Traits}]"));
        }

        return normalized;
    }

    /// <summary>
    /// The plain row a set of variants never got. Reusing WithFallback means
    /// the repair and the fill agree on what a stand-in is, instead of two
    /// opinions drifting apart.
    /// </summary>
    private int Localy(IReadOnlyList<LintFinding> findings, Action<string>? log)
    {
        var rows = store.LoadAll().ToList();
        var repaired = 0;

        foreach (var finding in findings.Where(finding => finding.Rule == TranslationLint.Rules.NoFallbackRow))
        {
            var variants = rows
                .Where(row => row.Key == finding.Key && row.Language == finding.Language)
                .ToList();

            foreach (var added in TranslationLint.WithFallback(variants).Except(variants))
            {
                store.Save(added);
                repaired++;
                log?.Invoke($"  [{finding.Language}] {finding.Key} -> plain row added: \"{added.Template}\"");
            }
        }

        return repaired;
    }

    /// <summary>
    /// One request per broken (key, language), with the catalog's legend when
    /// there is one — a sentence gets the same context the fill would give it,
    /// a value (an entity name, a room title) is asked for plainly.
    /// </summary>
    private async Task<(int Reasked, int Failed)> ReaskAsync(
        IReadOnlyList<LintFinding> findings,
        IReadOnlyList<CatalogEntry> entries,
        IReadOnlyList<string> sentenceKeys,
        Dictionary<(string Key, string Language), IReadOnlyList<TranslationRow>> replaced,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var catalog = entries
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var broken = findings
            .Where(finding => NeedTheModel.Contains(finding.Rule, StringComparer.Ordinal))
            .GroupBy(finding => (finding.Key, finding.Language))
            .ToList();

        var reasked = 0;
        var failed = 0;

        foreach (var group in broken)
        {
            var (key, language) = group.Key;
            var entry = catalog.GetValueOrDefault(key);

            // The complaint travels WITH the request. Asking the same question
            // again would get the same answer — these models reply at
            // temperature 0 — so what makes a second attempt worth its cost is
            // that it is a different question: here is what was wrong with the
            // last one.
            var wanted = group.Select(finding => finding.Rule).ToHashSet(StringComparer.Ordinal);
            var complaint = Complaint(group);

            var request = new TranslationRequest(
                key,
                Internationalization.SourceLanguage,
                language,
                entry?.Legend ?? [],
                entry?.Facts ?? []);

            try
            {
                // Ask, then LINT THE ANSWER before believing it. A model that
                // invented a hole once will invent it again, and saving a
                // second broken row on top of the first only makes the table
                // longer. Only an answer that passes gets written.
                IReadOnlyList<TranslationRow> accepted = [];
                var complaints = string.Empty;

                for (var attempt = 1; attempt <= Attempts; attempt++)
                {
                    var answer = await translator.TranslateAsync(
                        request with { Feedback = complaint }, cancellationToken);

                    if (answer.Count == 0)
                    {
                        complaints = "no answer";
                        continue;
                    }

                    // The same hygiene the fill applies, or repairing a value
                    // would quietly undo it: a common noun back to lowercase, a
                    // proper name keeping the trait the catalog says it has.
                    // Two paths that write the same rows have to agree, and the
                    // way they disagree is never visible until a room's title
                    // comes back lowercase in one language only.
                    var proposed = TranslationLint.WithFallback(answer)
                        .Select(row => Hygienic(
                            new TranslationRow(key, row.Context, language, row.Template, row.Traits),
                            entry))
                        .ToList();

                    // Certain defects always disqualify an answer; the ones we
                    // came here to fix disqualify it too, however sure the lint
                    // was about them — otherwise a repair pass would happily
                    // save back the very row it set out to replace.
                    var offending = TranslationLint.Inspect(Corpus(proposed, replaced), sentenceKeys)
                        .Where(finding => finding.Key == key
                            && string.Equals(finding.Language, language, StringComparison.OrdinalIgnoreCase)
                            && (finding.Severity == LintSeverity.Error || wanted.Contains(finding.Rule)))
                        .ToList();

                    if (offending.Count == 0)
                    {
                        accepted = proposed;
                        break;
                    }

                    complaints = offending[0].Rule;
                    complaint = Complaint(offending);
                    log?.Invoke($"  [{language}] {key} -> still {complaints}, asking again ({attempt}/{Attempts})");
                }

                if (accepted.Count == 0)
                {
                    failed++;
                    log?.Invoke($"  [{language}] {key} -> gave up: {complaints}");
                    continue;
                }

                replaced[(key, language)] = accepted;
                reasked++;
                log?.Invoke($"  [{language}] {key} -> repaired, {accepted.Count} row(s) ({group.First().Rule})");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failed++;
                log?.Invoke($"  [{language}] {key} -> FAILED: {exception.Message}");
            }
        }

        return (reasked, failed);
    }

    /// <summary>
    /// A value as the engine needs it: lowercase unless the catalog says this
    /// is a proper name. Sentences are left exactly as translated — their
    /// opening is the author's business, not ours.
    /// </summary>
    private static TranslationRow Hygienic(TranslationRow row, CatalogEntry? entry)
    {
        if (entry is null || entry.Kind == CatalogEntryKind.Sentence)
        {
            return row;
        }

        return entry.Facts.Contains(WellKnownTraits.Capitalize, StringComparer.Ordinal)
            ? ValueHygiene.AsProperNoun(row)
            : ValueHygiene.AsCommonNoun(row);
    }

    /// <summary>
    /// What to tell the model, in the lint's own words. A couple of complaints
    /// is a correction; the whole list is a wall of text a model reads as
    /// noise, so the rest is left for the next round to raise if it survives.
    /// </summary>
    private static string Complaint(IEnumerable<LintFinding> findings) =>
        string.Join("; ", findings
            .Select(finding => finding.Message)
            .Distinct(StringComparer.Ordinal)
            .Take(2));

    /// <summary>
    /// The proposed rows against the rest of the table: judging a value's
    /// gender needs to know whether this language declines at all, and whether
    /// its capital is disputed needs the other languages — only the corpus can
    /// say either. The units already repaired in this pass count as their new
    /// selves, since that is what the table will hold when it is written.
    /// </summary>
    private IReadOnlyList<TranslationRow> Corpus(
        IReadOnlyList<TranslationRow> proposed,
        Dictionary<(string Key, string Language), IReadOnlyList<TranslationRow>> replaced)
    {
        var superseded = replaced.Keys.ToHashSet();
        foreach (var row in proposed)
        {
            superseded.Add(Unit(row));
        }

        return
        [
            .. store.LoadAll().Where(row => !superseded.Contains(Unit(row))),
            .. replaced.Where(unit => !proposed.Any(row => Unit(row) == unit.Key)).SelectMany(unit => unit.Value),
            .. proposed,
        ];
    }
}
