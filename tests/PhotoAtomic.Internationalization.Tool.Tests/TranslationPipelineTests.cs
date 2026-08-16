using PhotoAtomic;

namespace PhotoAtomic.Tool.Tests;

/// <summary>
/// The one command. What is tested here is mostly ORDER and STOPPING — the two
/// things a human doing this by hand gets wrong: translating sentences before
/// the words they decline against, and repairing forever in the hope that the
/// next round is the good one.
/// </summary>
public class TranslationPipelineTests : IDisposable
{
    /// <summary>A translator that answers plausibly and remembers the order it was asked in.</summary>
    private sealed class RecordingTranslator(Func<TranslationRequest, IReadOnlyList<TranslationRow>> answer) : ITranslator
    {
        public List<string> Asked { get; } = [];

        public Task<IReadOnlyList<TranslationRow>> TranslateAsync(
            TranslationRequest request, CancellationToken cancellationToken = default)
        {
            lock (Asked)
            {
                Asked.Add(request.Key);
            }

            return Task.FromResult(answer(request));
        }
    }

    private readonly string path = Path.Combine(Path.GetTempPath(), $"pipeline-{Guid.NewGuid():N}.csv");

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

    private const string Sentence = "Take the {0}";

    private static readonly CatalogEntry[] Catalog =
    [
        new(Sentence, null, ["item"], ["0:item"]),
        new("Bucket", null, [], ["item"], CatalogEntryKind.Value),
        new("Candle", null, [], ["item"], CatalogEntryKind.Value),
    ];

    private static readonly string[] Languages = ["it-IT"];

    private static IReadOnlyList<TranslationRow> Plausible(TranslationRequest request) =>
        request.Key == Sentence
            ?
            [
                new TranslationRow(request.Key, null, request.TargetLanguage, "Prendi {0}", null),
                new TranslationRow(request.Key, "0:GENDER-female", request.TargetLanguage, "Prendi la {0}", null),
                new TranslationRow(request.Key, "0:GENDER-male", request.TargetLanguage, "Prendi il {0}", null),
            ]
            : [new TranslationRow(request.Key, null, request.TargetLanguage, request.Key.ToLowerInvariant(), "GENDER-male")];

    [Fact]
    public async Task Values_are_translated_before_the_sentences_that_decline_against_them()
    {
        // The defect this ordering exists for: on a cold table the vocabulary
        // is empty, so a sentence asked first is written without a single
        // grammatical case and nothing is ever declined again.
        var translator = new RecordingTranslator(Plausible);
        var store = new CsvTranslationStore(path);

        await new TranslationPipeline(() => translator, store).RunAsync(Catalog, Languages);

        var sentenceAt = translator.Asked.IndexOf(Sentence);
        Assert.All(
            new[] { "Bucket", "Candle" },
            value => Assert.True(translator.Asked.IndexOf(value) < sentenceAt, $"{value} was asked after the sentence"));
    }

    [Fact]
    public async Task A_cold_table_comes_out_translated_linted_and_clean()
    {
        var store = new CsvTranslationStore(path);

        var report = await new TranslationPipeline(() => new RecordingTranslator(Plausible), store)
            .RunAsync(Catalog, Languages);

        Assert.Equal(2, report.Values);
        Assert.Equal(1, report.Sentences);
        Assert.Empty(report.Missing);
        Assert.Empty(report.Remaining);
        Assert.True(report.IsClean);
    }

    [Fact]
    public async Task Rows_the_code_no_longer_says_are_gone_before_anything_is_counted()
    {
        var store = new CsvTranslationStore(path);
        store.Save(new TranslationRow("A sentence this program stopped saying some time ago.", null, "it-IT", "Roba vecchia.", null));

        var report = await new TranslationPipeline(() => new RecordingTranslator(Plausible), store)
            .RunAsync(Catalog, Languages);

        Assert.Equal(1, report.Pruned);
        Assert.DoesNotContain(store.LoadAll(), row => row.Template == "Roba vecchia.");
    }

    [Fact]
    public async Task A_model_that_cannot_fix_its_own_defect_is_not_asked_forever()
    {
        // Every answer dissolves the hole. The pipeline must stop, report it,
        // and leave the sentence for a human — not spin.
        var translator = new RecordingTranslator(request => request.Key == Sentence
            ? [new TranslationRow(request.Key, null, request.TargetLanguage, "Prendi il secchio", null)]
            : Plausible(request));

        var store = new CsvTranslationStore(path);
        var report = await new TranslationPipeline(() => translator, store).RunAsync(Catalog, Languages);

        Assert.Contains(report.Remaining, finding => finding.Rule == TranslationLint.Rules.MissingHole);
        Assert.False(report.IsClean);
        Assert.True(report.Rounds <= 3, $"gave up after {report.Rounds} rounds");
    }

    [Fact]
    public async Task What_still_needs_a_human_ends_up_at_the_bottom_of_the_file()
    {
        var translator = new RecordingTranslator(request => request.Key == Sentence
            ? [new TranslationRow(request.Key, null, request.TargetLanguage, "Prendi il secchio", null)]
            : Plausible(request));

        var store = new CsvTranslationStore(path);
        await new TranslationPipeline(() => translator, store).RunAsync(Catalog, Languages);

        // Whole units move together: among rows with the same criteria the last
        // one wins, so a single row moved on its own could change which applies.
        var rows = store.LoadAll().ToList();
        Assert.Equal(Sentence, rows[^1].Key);
        Assert.DoesNotContain(rows.Take(rows.Count - 1), row => row.Key == Sentence);
    }

    [Fact]
    public async Task A_rerun_with_nothing_to_do_costs_nothing_and_changes_nothing()
    {
        var store = new CsvTranslationStore(path);
        await new TranslationPipeline(() => new RecordingTranslator(Plausible), store).RunAsync(Catalog, Languages);
        var afterFirst = File.ReadAllText(path);

        var second = new RecordingTranslator(Plausible);
        var report = await new TranslationPipeline(() => second, store).RunAsync(Catalog, Languages);

        Assert.Empty(second.Asked);
        Assert.True(report.IsClean);
        Assert.Equal(afterFirst, File.ReadAllText(path));
    }
}
