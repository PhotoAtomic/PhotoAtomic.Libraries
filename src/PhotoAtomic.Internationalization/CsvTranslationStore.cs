using System.Text;

namespace PhotoAtomic;

/// <summary>
/// Translation store backed by an RFC 4180 CSV file with a header row:
/// key, context (the row's criteria, empty = generic), language, template,
/// traits (facts the row declares about its text). Data fields are always
/// quoted; double quotes inside a field are doubled (""), and real newlines
/// are legal inside a quoted field — so any sentence survives the round trip
/// and the file opens cleanly in any spreadsheet, where translators live.
///
/// The file is append-only and, on equal specificity, the last row wins:
/// a save never rewrites existing lines — simple, diff-friendly, crash-safe.
/// Compaction can come later if needed.
///
/// Reads are eager and both directions open the file with permissive sharing:
/// translations get appended at runtime (future AI fill) while other parts of
/// the process may be loading, and neither side may block the other.
/// </summary>
public sealed class CsvTranslationStore : ITranslationStore
{
    private const string Header = "key,context,language,template,traits";

    private readonly string path;
    private readonly Lock writeLock = new();

    public CsvTranslationStore(string path) => this.path = path;

    public IEnumerable<TranslationRow> LoadAll()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        string content;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            content = reader.ReadToEnd();
        }

        var rows = new List<TranslationRow>();
        foreach (var record in ParseRecords(content))
        {
            if (record.Count != 5)
            {
                continue; // tolerate hand-edited noise rather than fail the whole load
            }

            if (record[0] == "key" && record[1] == "context" && record[2] == "language"
                && record[3] == "template" && record[4] == "traits")
            {
                continue; // the header (data rows are always quoted, but parsing unquotes them too)
            }

            var context = record[1].Length == 0 ? null : record[1];
            var traits = record[4].Length == 0 ? null : record[4];
            rows.Add(new TranslationRow(record[0], context, record[2], record[3], traits));
        }

        return rows;
    }

    public void Save(TranslationRow row)
    {
        var line = string.Join(',',
            Quote(row.Key),
            Quote(row.Context ?? string.Empty),
            Quote(row.Language),
            Quote(row.Template),
            Quote(row.Traits ?? string.Empty));

        lock (writeLock)
        {
            // Append creates the file when missing, and Position == 0 tells us
            // atomically that a header is needed: no exists-then-check races
            // with concurrent deletions or creations.
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            if (stream.Position == 0)
            {
                writer.WriteLine(Header);
            }

            writer.WriteLine(line);
        }
    }

    private static string Quote(string field) =>
        "\"" + field.Replace("\"", "\"\"") + "\"";

    /// <summary>Minimal RFC 4180 reader: quoted fields, doubled quotes, newlines inside quotes.</summary>
    private static List<List<string>> ParseRecords(string content)
    {
        var records = new List<List<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < content.Length; i++)
        {
            var current = content[i];

            if (inQuotes)
            {
                if (current == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(current);
                }

                continue;
            }

            switch (current)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break; // record ends are handled on \n; a stray \r outside quotes is noise
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    if (fields.Count > 1 || fields[0].Length > 0)
                    {
                        records.Add(fields);
                    }

                    fields = [];
                    break;
                default:
                    field.Append(current);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(fields);
        }

        return records;
    }
}
