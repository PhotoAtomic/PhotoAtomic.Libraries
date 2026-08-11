using PhotoAtomic;

namespace PhotoAtomic.Tool.Tests;

public class CatalogFillerTests : IDisposable
{
    private sealed class FakeTranslator : ITranslator
    {
        private readonly List<TranslationRequest> requests = [];

        public IReadOnlyList<TranslationRequest> Requests
        {
            get
            {
                lock (requests)
                {
                    return requests.ToArray();
                }
            }
        }

        public Func<TranslationRequest, IReadOnlyList<TranslationRow>> Handler { get; init; } =
            request => [new TranslationRow(request.Key, null, request.TargetLanguage, $"[{request.TargetLanguage}] {request.Key}")];

        public Task<IReadOnlyList<TranslationRow>> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
        {
            lock (requests)
            {
                requests.Add(request);
            }

            return Task.FromResult(Handler(request));
        }
    }

    private readonly string path = Path.Combine(Path.GetTempPath(), $"fill-{Guid.NewGuid():N}.csv");

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

    private static readonly CatalogEntry[] Catalog =
    [
        new("You found {0} coins", null, ["count"], ["0:CLDR-other"]),
        new("Open", "verb", [], []),
        new("Hammer", null, [], ["tool"], CatalogEntryKind.Value),
    ];

    [Fact]
    public async Task Fills_every_entry_for_every_language()
    {
        var translator = new FakeTranslator();
        var store = new CsvTranslationStore(path);

        var report = await new CatalogFiller(translator, store).FillAsync(Catalog, ["it-IT", "fr-FR"]);

        Assert.Equal(6, report.Translated);
        Assert.Equal(0, report.Skipped);
        Assert.Equal(0, report.Failed);

        var rows = store.LoadAll().ToList();
        Assert.Equal(6, rows.Count);
        Assert.Contains(rows, row => row.Key == "Hammer" && row.Language == "fr-FR");
    }

    [Fact]
    public async Task The_request_carries_legend_and_facts_from_the_catalog()
    {
        var translator = new FakeTranslator();

        await new CatalogFiller(translator, new CsvTranslationStore(path)).FillAsync(Catalog, ["it-IT"]);

        var sentence = Assert.Single(translator.Requests, r => r.Key == "You found {0} coins");
        Assert.Equal(["count"], sentence.Legend);
        Assert.Equal(["0:CLDR-other"], sentence.Facts);

        var value = Assert.Single(translator.Requests, r => r.Key == "Hammer");
        Assert.Equal(["tool"], value.Facts);
    }

    [Fact]
    public async Task Reruns_skip_pairs_already_present_in_the_store()
    {
        var translator = new FakeTranslator();
        var store = new CsvTranslationStore(path);
        var filler = new CatalogFiller(translator, store);

        await filler.FillAsync(Catalog, ["it-IT"]);
        var requestsAfterFirstRun = translator.Requests.Count;

        var report = await filler.FillAsync(Catalog, ["it-IT", "fr-FR"]);

        // Second run only pays for the new language.
        Assert.Equal(3, requestsAfterFirstRun);
        Assert.Equal(3, report.Translated);
        Assert.Equal(3, report.Skipped);
    }

    [Fact]
    public async Task A_failing_translation_is_counted_not_thrown()
    {
        var translator = new FakeTranslator
        {
            Handler = request => request.Key == "Open"
                ? throw new InvalidOperationException("boom")
                : [new TranslationRow(request.Key, null, request.TargetLanguage, "ok")],
        };

        var report = await new CatalogFiller(translator, new CsvTranslationStore(path)).FillAsync(Catalog, ["it-IT"]);

        Assert.Equal(2, report.Translated);
        Assert.Equal(1, report.Failed);
    }
}

public class CatalogVerifierTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"verify-{Guid.NewGuid():N}.csv");

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

    private static readonly CatalogEntry[] Catalog =
    [
        new("You found {0} coins", null, ["count"], ["0:CLDR-other"]),
        new("Hammer", null, [], ["tool"], CatalogEntryKind.Value),
    ];

    [Fact]
    public void A_fully_covered_catalog_verifies_complete()
    {
        var store = new CsvTranslationStore(path);
        store.Save(new TranslationRow("You found {0} coins", "0:CLDR-other", "it-IT", "Hai trovato {0} monete"));
        store.Save(new TranslationRow("Hammer", null, "it-IT", "martello", "GENDER-male"));

        var report = CatalogVerifier.Verify(Catalog, ["it-IT"], store);

        Assert.True(report.IsComplete);
        Assert.Equal(2, report.Present);
    }

    [Fact]
    public void Every_uncovered_pair_is_listed()
    {
        var store = new CsvTranslationStore(path);
        store.Save(new TranslationRow("Hammer", null, "it-IT", "martello", "GENDER-male"));

        var report = CatalogVerifier.Verify(Catalog, ["it-IT", "fr-FR"], store);

        Assert.False(report.IsComplete);
        Assert.Equal(1, report.Present);
        Assert.Equal(3, report.Missing.Count);
        Assert.Contains(("You found {0} coins", "fr-FR"), report.Missing);
        Assert.Contains(("Hammer", "fr-FR"), report.Missing);
    }

    [Fact]
    public void A_missing_store_means_everything_is_missing()
    {
        var report = CatalogVerifier.Verify(Catalog, ["it-IT"], new CsvTranslationStore(path));

        Assert.Equal(2, report.Missing.Count);
        Assert.Equal(0, report.Present);
    }
}
