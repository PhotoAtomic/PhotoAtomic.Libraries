using static PhotoAtomic.Internationalization;

namespace PhotoAtomic.Tests;

public class CsvTranslationStoreTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"i18n-{Guid.NewGuid():N}.csv");

    public void Dispose()
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp file: a concurrent write-through
            // from a parallel test may still hold the handle for an instant.
        }
    }

    [Fact]
    public void A_missing_file_loads_as_empty()
    {
        var storeOnMissingFile = new CsvTranslationStore(path);

        Assert.Empty(storeOnMissingFile.LoadAll());
    }

    [Fact]
    public void Saved_rows_round_trip_through_a_new_store_instance()
    {
        var writer = new CsvTranslationStore(path);
        writer.Save(new TranslationRow("The pot boils", null, "it-IT", "La pentola bolle"));
        writer.Save(new TranslationRow("Open", "verb", "it-IT", "Apri"));
        writer.Save(new TranslationRow("He said \"hi\"", null, "it-IT", "Ha detto \"ciao\""));
        writer.Save(new TranslationRow("One, two, three", null, "it-IT", "Uno, due, tre"));
        writer.Save(new TranslationRow("Line one\nLine two", null, "it-IT", "Riga uno\nRiga due"));

        var reader = new CsvTranslationStore(path);
        var rows = reader.LoadAll().ToList();

        Assert.Equal(5, rows.Count);
        Assert.Equal(new TranslationRow("The pot boils", null, "it-IT", "La pentola bolle"), rows[0]);
        Assert.Equal(new TranslationRow("Open", "verb", "it-IT", "Apri"), rows[1]);
        Assert.Equal(new TranslationRow("He said \"hi\"", null, "it-IT", "Ha detto \"ciao\""), rows[2]);
        Assert.Equal(new TranslationRow("One, two, three", null, "it-IT", "Uno, due, tre"), rows[3]);
        Assert.Equal(new TranslationRow("Line one\nLine two", null, "it-IT", "Riga uno\nRiga due"), rows[4]);
    }

    [Fact]
    public void The_file_starts_with_a_single_header_row_for_spreadsheets()
    {
        var store = new CsvTranslationStore(path);
        store.Save(new TranslationRow("First", null, "it-IT", "Primo"));
        store.Save(new TranslationRow("Second", null, "it-IT", "Secondo"));

        var lines = File.ReadAllLines(path);

        Assert.Equal("key,context,language,template,traits", lines[0]);
        Assert.Equal(1, lines.Count(line => line == "key,context,language,template,traits"));
    }

    [Fact]
    public void The_file_is_append_only_so_every_version_of_a_row_is_kept_in_order()
    {
        var store = new CsvTranslationStore(path);
        store.Save(new TranslationRow("Draft", null, "it-IT", "Bozza vecchia"));
        store.Save(new TranslationRow("Draft", null, "it-IT", "Bozza nuova"));

        var rows = store.LoadAll().ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal("Bozza nuova", rows[^1].Template);
    }
}

// Store attachment mutates process-wide state (UseStore), so these tests use
// unique sentences and detach in Dispose to stay friendly with parallel classes.
public class StoreAttachmentTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"i18n-{Guid.NewGuid():N}.csv");

    public void Dispose()
    {
        UseStore(null);
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp file: a concurrent write-through
            // from a parallel test may still hold the handle for an instant.
        }
    }

    [Fact]
    public void Attaching_a_store_makes_its_translations_available_and_later_rows_win()
    {
        var seed = new CsvTranslationStore(path);
        seed.Save(new TranslationRow("The candle flickers", null, "it-IT", "La candela trema"));
        seed.Save(new TranslationRow("The candle flickers", null, "it-IT", "La candela tremola"));

        UseStore(new CsvTranslationStore(path));

        Language = "it-IT";
        Assert.Equal("La candela tremola", T($"The candle flickers"));
    }

    [Fact]
    public void Registrations_write_through_to_the_attached_store()
    {
        UseStore(new CsvTranslationStore(path));

        SetTranslation("The bonfire roars", "it-IT", "Il falò ruggisce");

        var reader = new CsvTranslationStore(path);
        var row = Assert.Single(reader.LoadAll(), r => r.Key == "The bonfire roars");
        Assert.Equal("Il falò ruggisce", row.Template);
        Assert.Equal("it-IT", row.Language);
        Assert.Null(row.Context);
    }
}
