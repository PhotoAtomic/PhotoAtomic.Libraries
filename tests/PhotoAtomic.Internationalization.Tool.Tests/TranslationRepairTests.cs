using PhotoAtomic;

namespace PhotoAtomic.Tool.Tests;

/// <summary>
/// The other half of the lint. Measuring was the point of the first half;
/// these tests are about the number actually going down — and about the ways a
/// repair pass can pretend it did.
/// </summary>
public class TranslationRepairTests : IDisposable
{
    /// <summary>Answers from a script, and remembers what it was told was wrong.</summary>
    private sealed class ScriptedTranslator(Func<int, TranslationRequest, IReadOnlyList<TranslationRow>> answer) : ITranslator
    {
        public List<TranslationRequest> Requests { get; } = [];

        public Task<IReadOnlyList<TranslationRow>> TranslateAsync(
            TranslationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(answer(Requests.Count, request));
        }
    }

    private readonly string path = Path.Combine(Path.GetTempPath(), $"repair-{Guid.NewGuid():N}.csv");

    public void Dispose()
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private const string Sentence = "Give the {0} to the {1}";

    private static readonly CatalogEntry[] Catalog =
    [
        new(Sentence, null, ["item", "person"], ["0:item", "1:item"]),
    ];

    private static readonly string[] SentenceKeys = [Sentence];

    private CsvTranslationStore StoreWith(params TranslationRow[] rows)
    {
        var store = new CsvTranslationStore(path);
        foreach (var row in rows)
        {
            store.Save(row);
        }

        return store;
    }

    private static IReadOnlyList<LintFinding> Lint(CsvTranslationStore store) =>
        TranslationLint.Inspect(store.LoadAll(), SentenceKeys, "en-US");

    [Fact]
    public async Task The_plain_row_a_set_of_variants_never_got_is_written_without_asking_anyone()
    {
        var store = StoreWith(
            new TranslationRow(Sentence, "0:GENDER-female", "it-IT", "Dai la {0} a {1}", null),
            new TranslationRow(Sentence, "0:GENDER-male", "it-IT", "Dai il {0} a {1}", null));

        var translator = new ScriptedTranslator((_, _) => throw new InvalidOperationException("must not ask"));
        var report = await new TranslationRepair(translator, store)
            .RepairAsync(Lint(store), Catalog, SentenceKeys);

        Assert.Equal(1, report.Locally);
        Assert.Empty(translator.Requests);
        Assert.DoesNotContain(Lint(store), finding => finding.Rule == TranslationLint.Rules.NoFallbackRow);
    }

    [Fact]
    public async Task A_sentence_that_declines_for_one_gender_and_not_the_other_is_asked_again_with_the_complaint()
    {
        // The defect the whole lint was written for: same text under both
        // genders in a sentence that clearly declines elsewhere.
        var store = StoreWith(
            new TranslationRow(Sentence, null, "it-IT", "Dai {0} a {1}", null),
            new TranslationRow(Sentence, "0:GENDER-female,1:GENDER-female", "it-IT", "Dai la {0} alla {1}", null),
            new TranslationRow(Sentence, "0:GENDER-male,1:GENDER-female", "it-IT", "Dai il {0} alla {1}", null),
            // Both genders of hole 0, same text, and no vowel to explain it.
            new TranslationRow(Sentence, "0:GENDER-female,1:GENDER-male", "it-IT", "Dai il {0} al {1}", null),
            new TranslationRow(Sentence, "0:GENDER-male,1:GENDER-male", "it-IT", "Dai il {0} al {1}", null));

        var before = Lint(store);
        Assert.Contains(before, finding => finding.Rule == TranslationLint.Rules.InconsistentAgreement);

        var translator = new ScriptedTranslator((_, _) =>
        [
            new TranslationRow(Sentence, null, "it-IT", "Dai {0} a {1}", null),
            new TranslationRow(Sentence, "0:GENDER-female", "it-IT", "Dai la {0} a {1}", null),
            new TranslationRow(Sentence, "0:GENDER-male", "it-IT", "Dai il {0} a {1}", null),
        ]);

        var report = await new TranslationRepair(translator, store).RepairAsync(before, Catalog, SentenceKeys);

        Assert.Equal(1, report.Reasked);
        Assert.Equal(0, report.Failed);

        // The model was told what was wrong, in the lint's own words.
        var feedback = Assert.Single(translator.Requests).Feedback;
        Assert.NotNull(feedback);
        Assert.Contains("agrees with hole", feedback, StringComparison.Ordinal);

        Assert.DoesNotContain(Lint(store), finding => finding.Rule == TranslationLint.Rules.InconsistentAgreement);
    }

    [Fact]
    public async Task An_answer_that_repeats_the_defect_is_refused_rather_than_saved_on_top_of_it()
    {
        var store = StoreWith(
            new TranslationRow(Sentence, null, "it-IT", "Dai {0} a {1}", null),
            new TranslationRow(Sentence, "0:GENDER-female,1:GENDER-female", "it-IT", "Dai la {0} alla {1}", null),
            new TranslationRow(Sentence, "0:GENDER-male,1:GENDER-female", "it-IT", "Dai il {0} alla {1}", null),
            // Both genders of hole 0, same text, and no vowel to explain it.
            new TranslationRow(Sentence, "0:GENDER-female,1:GENDER-male", "it-IT", "Dai il {0} al {1}", null),
            new TranslationRow(Sentence, "0:GENDER-male,1:GENDER-male", "it-IT", "Dai il {0} al {1}", null));

        // A model that keeps dissolving hole {0} into the sentence.
        var translator = new ScriptedTranslator((_, _) =>
        [
            new TranslationRow(Sentence, null, "it-IT", "Dai la chiave a {1}", null),
        ]);

        var report = await new TranslationRepair(translator, store).RepairAsync(Lint(store), Catalog, SentenceKeys);

        Assert.Equal(0, report.Reasked);
        Assert.Equal(1, report.Failed);
        Assert.Equal(3, translator.Requests.Count); // tried, three times, then gave up
        Assert.DoesNotContain(store.LoadAll(), row => row.Template == "Dai la chiave a {1}");
    }

    [Fact]
    public async Task A_sentence_nobody_says_any_more_is_neither_repaired_nor_asked_about()
    {
        var store = StoreWith(
            new TranslationRow("Something the code stopped saying, long ago and at length.", "0:GENDER-female", "it-IT", "Roba vecchia.", null),
            new TranslationRow("Something the code stopped saying, long ago and at length.", "0:GENDER-male", "it-IT", "Roba vecchia.", null));

        var translator = new ScriptedTranslator((_, _) => throw new InvalidOperationException("must not ask"));
        var report = await new TranslationRepair(translator, store)
            .RepairAsync(Lint(store), Catalog, SentenceKeys);

        Assert.Equal(0, report.Locally);
        Assert.Equal(0, report.Reasked);
        Assert.Empty(translator.Requests);
    }

    [Fact]
    public void Rewriting_the_table_keeps_every_surviving_row_exactly_as_it_was()
    {
        var store = StoreWith(
            new TranslationRow("Bonfire", null, "it-IT", "falò", "GENDER-male"),
            new TranslationRow("Dead \"quoted\" sentence, with a comma.", null, "it-IT", "Roba, \"vecchia\".", null),
            new TranslationRow(Sentence, "0:GENDER-female", "it-IT", "Dai la {0} a {1}", null));

        var kept = store.LoadAll().Where(row => row.Key != "Dead \"quoted\" sentence, with a comma.").ToList();
        store.ReplaceAll(kept);

        Assert.Equal(kept, store.LoadAll());
    }

    [Fact]
    public void A_rewritten_table_keeps_its_header_a_header()
    {
        // A byte order mark in front of it turns "key" into "﻿key", the
        // header stops being recognised, and the table grows a row whose
        // language is "language" — which is exactly what happened the first
        // time the file was rewritten.
        var store = StoreWith(new TranslationRow("Bonfire", null, "it-IT", "falò", "GENDER-male"));
        store.ReplaceAll(store.LoadAll().ToList());

        Assert.False(File.ReadAllText(path).StartsWith('﻿'));
        Assert.Equal("Bonfire", Assert.Single(store.LoadAll()).Key);
    }

    [Fact]
    public async Task What_the_catalog_knows_about_a_value_reaches_rows_written_before_it_knew()
    {
        // The table outlives the rules that judge it: these two were written
        // when nobody marked rooms as places, and a fill that skips what is
        // already there would never come back to them.
        var store = StoreWith(
            new TranslationRow("The Ship's Galley", null, "it-IT", "la cambusa della nave", "GENDER-female"),
            new TranslationRow("Secret recipe", null, "it-IT", "Ricetta segreta", "GENDER-female"));

        CatalogEntry[] catalog =
        [
            new("The Ship's Galley", null, [], [WellKnownTraits.Capitalize], CatalogEntryKind.Value),
            new("Secret recipe", "item", [], ["item"], CatalogEntryKind.Value),
        ];

        var translator = new ScriptedTranslator((_, _) => throw new InvalidOperationException("must not ask"));
        var report = await new TranslationRepair(translator, store).RepairAsync([], catalog, SentenceKeys);

        Assert.Equal(2, report.Locally);
        Assert.Empty(translator.Requests);

        var rows = store.LoadAll().GroupBy(row => row.Key).ToDictionary(g => g.Key, g => g.Last());

        // A place keeps its capital and says so...
        Assert.Contains(WellKnownTraits.Capitalize, rows["The Ship's Galley"].Traits!);

        // ...a thing goes lowercase, or it would keep that capital in the
        // middle of every sentence naming it.
        Assert.Equal("ricetta segreta", rows["Secret recipe"].Template);
    }

    [Fact]
    public async Task A_value_the_catalog_says_nothing_about_is_left_alone()
    {
        var store = StoreWith(new TranslationRow("Elsewhere", null, "it-IT", "Altrove", null));

        var translator = new ScriptedTranslator((_, _) => throw new InvalidOperationException("must not ask"));
        var report = await new TranslationRepair(translator, store).RepairAsync([], Catalog, SentenceKeys);

        Assert.Equal(0, report.Locally);
        Assert.Equal("Altrove", Assert.Single(store.LoadAll()).Template);
    }
}
