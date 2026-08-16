using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoAtomic;

/// <summary>
/// A catalog read from a FILE, for the content a compiler never sees.
///
/// The other two readers find translatable text by looking at a program: one
/// walks a compiled assembly, the other opens a project and lets the source
/// generators run. Neither can help with text that is DATA — a room written by
/// an AI, rows in a database, a CMS export — and that text is increasingly
/// most of it.
///
/// So the tool learns to read a catalog instead of deriving one. The format is
/// not a new idea, it is <see cref="CatalogEntry"/> written down: whoever owns
/// the content emits it, and everything downstream — the vocabulary order, the
/// lint, the repair, the pruning — works exactly as it does for code, because
/// by that point nothing can tell the difference.
/// </summary>
public static class FileCatalogReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    /// <summary>Whether this path is something this reader can take: a .json file, or a directory of them.</summary>
    public static bool Handles(string path) =>
        Directory.Exists(path) || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every entry in a file, or in every .json of a directory. A directory is
    /// read shallow and in name order, so two runs see the same catalog in the
    /// same sequence.
    /// </summary>
    public static IReadOnlyList<CatalogEntry> Read(string path, Action<string>? log = null)
    {
        if (!Directory.Exists(path))
        {
            return ReadFile(path, log);
        }

        var entries = new List<CatalogEntry>();
        foreach (var file in Directory.GetFiles(path, "*.json").OrderBy(name => name, StringComparer.Ordinal))
        {
            entries.AddRange(ReadFile(file, log));
        }

        return entries;
    }

    private static IReadOnlyList<CatalogEntry> ReadFile(string path, Action<string>? log)
    {
        List<CatalogEntry>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<CatalogEntry>>(File.ReadAllText(path), Options);
        }
        catch (JsonException failure)
        {
            // A directory of data files may hold things that are not catalogs
            // at all — the rooms themselves, for one. Saying so and moving on
            // beats refusing to translate anything.
            log?.Invoke($"  skipped {Path.GetFileName(path)}: not a catalog ({failure.Message})");
            return [];
        }

        var usable = (entries ?? []).Where(entry => !string.IsNullOrWhiteSpace(entry.Key)).ToList();
        log?.Invoke($"  {Path.GetFileName(path)}: {usable.Count} entr{(usable.Count == 1 ? "y" : "ies")}");
        return usable;
    }

    /// <summary>Writes a catalog in the shape this reader expects — for whoever owns the content.</summary>
    public static string ToJson(IEnumerable<CatalogEntry> entries) =>
        JsonSerializer.Serialize(entries.ToList(), Options);
}
