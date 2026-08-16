using System.Text.RegularExpressions;

namespace PhotoAtomic;

/// <summary>
/// How sure the lint is. Errors are defects by construction — a hole that
/// cannot arrive, a combination with no row to match. Warnings are suspicions
/// worth a human eye: they rest on how a language behaves, and languages are
/// not obliged to behave. Keeping the two apart is what lets the errors be a
/// gate without the warnings holding a build hostage.
/// </summary>
public enum LintSeverity
{
    Warning,
    Error,
}

/// <summary>One thing wrong with a row, said precisely enough to fix or to re-ask for.</summary>
public sealed record LintFinding(
    string Rule,
    string Key,
    string? Context,
    string Language,
    string Message,
    LintSeverity Severity = LintSeverity.Error);

/// <summary>
/// Reads a translation table and says what is wrong with it — deterministically,
/// with no model in the loop. The same bargain as the puzzle solver: no AI
/// judges the AI, the customs desk is arithmetic.
///
/// It exists because prompt tuning was guesswork: a checklist added here, an
/// instruction removed there, and no way to tell whether the corpus got better
/// or worse. Every rule below was earned by a defect that reached the running
/// game — a sentence that lost its article, a value that forgot its gender and
/// silently dragged every sentence naming it back into English, a template
/// that kept a word from the prompt's example.
/// </summary>
public static class TranslationLint
{
    public static class Rules
    {
        /// <summary>A hole the key has never arrives in the template — String.Format would leave the sentence half-said.</summary>
        public const string MissingHole = "missing-hole";

        /// <summary>A hole the key does NOT have appears in the template: FormatException at render time.</summary>
        public const string StrayHole = "stray-hole";

        /// <summary>One hole changes the sentence in some of its cases and not in others: one of the two is wrong.</summary>
        public const string InconsistentAgreement = "inconsistent-agreement";

        /// <summary>A key has variants but no plain row: a combination nobody foresaw finds nothing and falls back to English.</summary>
        public const string NoFallbackRow = "no-fallback-row";

        /// <summary>A value in a language that declines gender does not say which one it is.</summary>
        public const string GenderlessValue = "genderless-value";

        /// <summary>The template kept a word from the example it was shown ("infila la {1} nel falò").</summary>
        public const string ExampleLeftIn = "example-left-in";

        /// <summary>A sentence nobody says any more: the code that asked for it is gone.</summary>
        public const string OrphanRow = "orphan-row";

        /// <summary>One language calls the value a proper name and another does not: one of them is wrong.</summary>
        public const string DisputedCapitalization = "disputed-capitalization";
    }

    /// <param name="sentenceKeys">
    /// Which keys are SENTENCES. Values and sentences are judged by different
    /// rules — a value must declare its gender, a sentence must not swallow
    /// one — and the two cannot be told apart by looking at a row: plenty of
    /// sentences have no holes ("Nothing interesting happens."). The catalog
    /// knows, so the caller passes it; without it the value rules stay quiet
    /// rather than guess.
    /// </param>
    /// <summary>
    /// Every finding in the table, ordered by rule then key so two runs read
    /// the same.
    /// </summary>
    public static IReadOnlyList<LintFinding> Inspect(
        IEnumerable<TranslationRow> rows,
        IEnumerable<string>? sentenceKeys = null,
        string? sourceLanguage = null)
    {
        var corpus = rows.ToList();
        var source = sourceLanguage ?? Internationalization.SourceLanguage;
        var sentences = sentenceKeys?.ToHashSet(StringComparer.Ordinal);
        var findings = new List<LintFinding>();
        var values = new List<TranslationRow>();

        var translated = corpus
            .Where(row => !string.Equals(row.Language, source, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var row in translated)
        {
            findings.AddRange(Holes(row));

            if (sentences is null || sentences.Contains(row.Key))
            {
                findings.AddRange(BorrowedWords(row, corpus, sentences));
                continue;
            }

            // Not in the catalog. Either the code that said it is gone — dead
            // weight worth reporting — or it is CONTENT the catalog never sees
            // (an entity's name, a room's title), which is judged as a value.
            if (LooksLikeASentence(row.Key))
            {
                findings.Add(new LintFinding(
                    Rules.OrphanRow, row.Key, row.Context, row.Language,
                    "no code asks for this sentence any more: the row can be deleted",
                    LintSeverity.Warning));
            }
            else
            {
                values.Add(row);
                findings.AddRange(Genders(row, corpus));
            }
        }

        findings.AddRange(Capitals(values));

        foreach (var group in translated.GroupBy(row => (row.Key, row.Language)))
        {
            findings.AddRange(Variants(group.Key.Key, group.Key.Language, [.. group]));
        }

        // A dead sentence has no defects worth counting: reporting that a row
        // nobody reads also lacks a fallback is noise that hides the rest.
        var dead = findings
            .Where(finding => finding.Rule == Rules.OrphanRow)
            .Select(finding => finding.Key)
            .ToHashSet(StringComparer.Ordinal);

        return findings
            .Where(finding => finding.Rule == Rules.OrphanRow || !dead.Contains(finding.Key))
            .OrderBy(finding => finding.Rule, StringComparer.Ordinal)
            .ThenBy(finding => finding.Key, StringComparer.Ordinal)
            .ThenBy(finding => finding.Language, StringComparer.Ordinal)
            .ThenBy(finding => finding.Context ?? string.Empty, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The same rows, plus the plain one they forgot. A set of variants with no
    /// unconditional row is a trap: the day a value turns up whose traits none
    /// of the criteria cover — a name that never said its gender, a language
    /// with a combination nobody predicted — the sentence has nothing to match
    /// and renders in English.
    ///
    /// The stand-in is the variant asking the fewest criteria: the least
    /// committed of the answers, and by construction a real translation rather
    /// than a guess. Better an article that may not agree than a line of
    /// English in the middle of the game.
    /// </summary>
    public static IReadOnlyList<TranslationRow> WithFallback(IReadOnlyList<TranslationRow> rows)
    {
        if (rows.Count == 0 || rows.Any(row => string.IsNullOrWhiteSpace(row.Context)))
        {
            return rows;
        }

        var plainest = rows
            .OrderBy(row => (row.Context ?? string.Empty).Count(character => character == ','))
            .ThenBy(row => row.Context, StringComparer.Ordinal)
            .First();

        return [.. rows, plainest with { Context = null }];
    }

    /// <summary>The holes of the key must all arrive, and nothing else may.</summary>
    private static IEnumerable<LintFinding> Holes(TranslationRow row)
    {
        var wanted = HolesOf(row.Key);
        var got = HolesOf(row.Template);

        foreach (var hole in wanted.Except(got))
        {
            yield return new LintFinding(
                Rules.MissingHole, row.Key, row.Context, row.Language,
                $"the template never uses {{{hole}}}, so that part of the sentence is lost: \"{row.Template}\"");
        }

        foreach (var hole in got.Except(wanted))
        {
            yield return new LintFinding(
                Rules.StrayHole, row.Key, row.Context, row.Language,
                $"the template uses {{{hole}}}, which the key does not have: rendering throws");
        }
    }

    /// <summary>A value has to say its gender wherever gender decides the sentence around it.</summary>
    private static IEnumerable<LintFinding> Genders(TranslationRow row, IReadOnlyList<TranslationRow> corpus)
    {
        if (!ValueHygiene.UsesGrammaticalGender(corpus, row.Language))
        {
            yield break;
        }

        if (!ValueHygiene.DeclaresGender(row))
        {
            yield return new LintFinding(
                Rules.GenderlessValue, row.Key, row.Context, row.Language,
                $"\"{row.Template}\" declares no gender: every sentence naming it will find no matching row "
                + "and fall back to the source language");
        }
    }

    /// <summary>
    /// Which values are proper names — decided by asking the languages to
    /// agree, since nothing in a row says whether a word is a name or a thing.
    ///
    /// A bonfire is a bonfire in every language: if one translation marks it
    /// Capitalize and another does not, one of the two got it wrong, and the
    /// suspect is the one that capitalized (the defect this rule was written
    /// for was "Falò" appearing mid-sentence). A real proper name — Player One,
    /// The Pirate Galley — is capitalized everywhere and says nothing here.
    ///
    /// German capitalizes every noun, so in a language where the capital is the
    /// NORM the disagreement is expected and the rule stays quiet. The norm is
    /// read from the corpus, like everything else here: no list of languages to
    /// keep up to date, and a language nobody predicted behaves correctly the
    /// day its values arrive.
    /// </summary>
    private static IEnumerable<LintFinding> Capitals(IReadOnlyList<TranslationRow> values)
    {
        var byLanguage = values
            .GroupBy(row => row.Language, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, KeysCapitalized, StringComparer.OrdinalIgnoreCase);

        foreach (var value in values.GroupBy(row => row.Key, StringComparer.Ordinal))
        {
            var languages = value.GroupBy(row => row.Language, StringComparer.OrdinalIgnoreCase).ToList();
            if (languages.Count < 2)
            {
                continue; // one language cannot disagree with itself
            }

            var lowercase = languages
                .Where(language => !language.Any(ValueHygiene.DeclaresCapitalization))
                .Select(language => language.Key)
                .ToList();

            if (lowercase.Count == 0)
            {
                continue; // agreed to be a proper name
            }

            foreach (var language in languages.Where(language => language.Any(ValueHygiene.DeclaresCapitalization)))
            {
                // Nearly ALL of them, not merely most: a language that
                // capitalizes nouns does it by rule, so its corpus comes out
                // near a hundred percent. A language where two values in three
                // are capitalized is not German — it is a table with a problem.
                var (capitalized, total) = byLanguage[language.Key];
                if (capitalized * 5 >= total * 4)
                {
                    continue; // a language that capitalizes its nouns anyway
                }

                var row = language.First(ValueHygiene.DeclaresCapitalization);
                yield return new LintFinding(
                    Rules.DisputedCapitalization, value.Key, row.Context, language.Key,
                    $"\"{row.Template}\" is marked as a proper name here but not in "
                    + $"{string.Join(", ", lowercase)}: if it is a common noun the capital "
                    + "will show up in the middle of every sentence naming it",
                    LintSeverity.Warning);
            }
        }
    }

    /// <summary>How many of a language's values are capitalized, out of how many — counted per value, not per row.</summary>
    private static (int Capitalized, int Total) KeysCapitalized(IEnumerable<TranslationRow> rows)
    {
        var keys = rows.GroupBy(row => row.Key, StringComparer.Ordinal).ToList();
        return (keys.Count(key => key.Any(ValueHygiene.DeclaresCapitalization)), keys.Count);
    }

    /// <summary>
    /// The example words a model is shown have a way of staying in the answer
    /// ("infila la {1} nel falò", where the bonfire should have been a hole).
    /// Detected against the table itself: a sentence whose text contains a
    /// translated VALUE as a whole word.
    ///
    /// Unless the English key contains that same word — "steam rises" is
    /// allowed to mention steam, and a rule that cannot tell the difference is
    /// a rule nobody will read twice.
    /// </summary>
    private static IEnumerable<LintFinding> BorrowedWords(
        TranslationRow row,
        IReadOnlyList<TranslationRow> corpus,
        HashSet<string>? sentences)
    {
        if (HolesOf(row.Key).Count == 0)
        {
            yield break;
        }

        var values = corpus
            .Where(other => other.Language == row.Language
                && (sentences is null || !sentences.Contains(other.Key))
                && !LooksLikeASentence(other.Key) // an orphan sentence is not a value
                && !Mentions(row.Key, other.Key))
            .Select(other => other.Template)
            .Where(value => value.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values.Where(value => Mentions(row.Template, value)))
        {
            yield return new LintFinding(
                Rules.ExampleLeftIn, row.Key, row.Context, row.Language,
                $"the template contains the value \"{value}\", which belongs in a hole: \"{row.Template}\"",
                LintSeverity.Warning);
        }
    }

    /// <summary>Whether the text names that word (whole word, any case).</summary>
    private static bool Mentions(string text, string word) =>
        Regex.IsMatch(text, $@"(?<![\p{{L}}]){Regex.Escape(word)}(?![\p{{L}}])", RegexOptions.IgnoreCase);

    /// <summary>
    /// What the variants of one key say about each other.
    ///
    /// Two variants sharing a text is NOT a defect in itself: Italian writes
    /// "usa la chiave" whoever is using it, so the subject's gender changes
    /// nothing and the rows are rightly identical. What IS a defect is a hole
    /// that decides the sentence in some of its cases and not in others —
    /// "È una grossa {0}" for a feminine vowel-word but "È un grosso {0}" for a
    /// feminine consonant-word. Comparing variants that differ in ONE hole only
    /// tells the two apart, which is what makes this rule quiet enough to read.
    ///
    /// The other half of being quiet is elision: in front of a vowel the
    /// genders legitimately merge, so those pairs prove nothing either way.
    /// </summary>
    private static IEnumerable<LintFinding> Variants(string key, string language, IReadOnlyList<TranslationRow> rows)
    {
        var variants = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Context))
            .Select(row => (Row: row, Holes: TraitsPerHole(row.Context!)))
            .ToList();

        if (variants.Count > 0 && !rows.Any(row => string.IsNullOrWhiteSpace(row.Context)))
        {
            yield return new LintFinding(
                Rules.NoFallbackRow, key, null, language,
                $"{variants.Count} variant(s) and no plain row: a combination none of them covers "
                + "renders in the source language");
        }

        var agrees = new Dictionary<string, (bool Changes, bool Ignores, string Example)>(StringComparer.Ordinal);

        foreach (var (left, right) in Pairs(variants))
        {
            if (GenderOnlyDifference(left.Holes, right.Holes) is not { } hole)
            {
                continue;
            }

            var same = left.Row.Template == right.Row.Template;

            // Two genders saying the same thing is only suspicious when nothing
            // explains it — and in front of a vowel something does. French
            // writes "à l'{1}" for either gender and "à la" / "au" otherwise;
            // Italian writes "l'{0}" for either and "la" / "il" otherwise. The
            // elision is the reason the two merged, so a pair that carries it
            // is no evidence of anything. Measured on the real corpus, where
            // half the complaints were exactly this, and a lint that cries wolf
            // is a lint nobody reads.
            var elided = left.Holes[hole].Contains(WellKnownTraits.StartsWithVowel, StringComparer.Ordinal);
            var seen = agrees.GetValueOrDefault(hole);

            agrees[hole] = (
                seen.Changes || !same,
                seen.Ignores || (same && !elided),
                same && !elided
                    ? $"\"{left.Row.Context}\" and \"{right.Row.Context}\" both say \"{left.Row.Template}\""
                    : seen.Example);
        }

        foreach (var (hole, seen) in agrees.Where(entry => entry.Value.Changes && entry.Value.Ignores))
        {
            yield return new LintFinding(
                Rules.InconsistentAgreement, key, $"hole {hole}", language,
                $"the sentence agrees with hole {{{hole}}} in some cases but not in others — {seen.Example}",
                LintSeverity.Warning);
        }
    }

    /// <summary>
    /// The traits a row's criteria assign to each hole. Capitalize is dropped:
    /// it says how a value is spelled, never how the sentence around it agrees,
    /// so two variants that differ only in it are rightly identical.
    /// </summary>
    private static IReadOnlyDictionary<string, SortedSet<string>> TraitsPerHole(string context)
    {
        var perHole = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var criterion in context.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = criterion.IndexOf(':');
            if (separator <= 0)
            {
                continue; // a sentence-level context, not a hole's trait
            }

            var trait = criterion[(separator + 1)..];
            if (string.Equals(trait, WellKnownTraits.Capitalize, StringComparison.Ordinal))
            {
                continue;
            }

            var hole = criterion[..separator];
            if (!perHole.TryGetValue(hole, out var traits))
            {
                traits = perHole[hole] = new SortedSet<string>(StringComparer.Ordinal);
            }

            traits.Add(trait);
        }

        return perHole;
    }

    /// <summary>
    /// The hole where two variants differ ONLY in grammatical gender —
    /// everything else about them identical, both saying which gender they are.
    /// Any looser comparison mixes axes (a vowel against a consonant, a
    /// declared gender against none) and the answer stops meaning anything.
    /// </summary>
    private static string? GenderOnlyDifference(
        IReadOnlyDictionary<string, SortedSet<string>> left,
        IReadOnlyDictionary<string, SortedSet<string>> right)
    {
        if (left.Count != right.Count || !left.Keys.All(right.ContainsKey))
        {
            return null;
        }

        string? differing = null;

        foreach (var (hole, traits) in left)
        {
            var other = right[hole];
            if (traits.SetEquals(other))
            {
                continue;
            }

            if (differing is not null || !DiffersOnlyByGender(traits, other))
            {
                return null;
            }

            differing = hole;
        }

        return differing;
    }

    private static bool DiffersOnlyByGender(SortedSet<string> left, SortedSet<string> right)
    {
        bool IsGender(string trait) => trait.StartsWith("GENDER-", StringComparison.Ordinal);

        return left.Any(IsGender)
            && right.Any(IsGender)
            && left.Where(trait => !IsGender(trait)).ToHashSet(StringComparer.Ordinal)
                .SetEquals(right.Where(trait => !IsGender(trait)));
    }

    private static IEnumerable<(T Left, T Right)> Pairs<T>(IReadOnlyList<T> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            for (var j = i + 1; j < items.Count; j++)
            {
                yield return (items[i], items[j]);
            }
        }
    }

    /// <summary>
    /// Sentence or name, told apart by shape — the only way, for keys the
    /// catalog does not carry. A sentence has holes, or ends in punctuation,
    /// or simply runs long; "Stone press base" does none of those.
    /// </summary>
    private static bool LooksLikeASentence(string key) =>
        HolesOf(key).Count > 0
        || (key.Length > 0 && ".!?:…".Contains(key[^1], StringComparison.Ordinal))
        || key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 5;

    private static IReadOnlyList<string> HolesOf(string text) =>
        [.. Regex.Matches(text, @"\{(\d+)(?::[^}]*)?\}").Select(match => match.Groups[1].Value).Distinct()];
}
