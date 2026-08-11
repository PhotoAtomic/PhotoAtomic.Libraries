using Microsoft.Extensions.Configuration;
using PhotoAtomic;

if (args.Length == 0 || args[0].StartsWith('-'))
{
    Console.WriteLine("PhotoAtomic i18n pre-translation tool");
    Console.WriteLine();
    Console.WriteLine("Usage: <assembly-path | project.csproj> [--csv <path>] [--verify]");
    Console.WriteLine();
    Console.WriteLine("  --verify  no translation: checks that every catalog unit has rows for");
    Console.WriteLine("            every configured language; exit code 3 when coverage is missing.");
    Console.WriteLine();
    Console.WriteLine("Configuration (appsettings.json next to the tool, user secrets, env vars):");
    Console.WriteLine("  Translator:Languages  array of target languages, e.g. [\"it-IT\", \"fr-FR\"]");
    Console.WriteLine("  Translator:Csv        output CSV path (or --csv)");
    Console.WriteLine("  Translator:Endpoint, Translator:Model, Translator:ApiKey   (fill only)");
    Console.WriteLine("  Translator:SystemPrompt, Translator:ApplicationContext     (optional)");
    return 1;
}

var assemblyPath = args[0];
var verify = args.Contains("--verify");
string? csvOverride = null;
for (var i = 1; i < args.Length - 1; i++)
{
    if (args[i] == "--csv")
    {
        csvOverride = args[i + 1];
    }
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets<ToolAnchor>()
    .AddEnvironmentVariables()
    .Build();

string Required(string key) =>
    configuration[$"Translator:{key}"]
    ?? throw new InvalidOperationException($"Missing configuration value Translator:{key}");

var languages = configuration.GetSection("Translator:Languages").Get<string[]>()
    ?? throw new InvalidOperationException("Missing configuration value Translator:Languages");
var csvPath = csvOverride ?? configuration["Translator:Csv"] ?? "translations.csv";

// A csproj goes through the workspace (source generators run there, Razor
// included, so markup T() calls are visible); a dll reads the baked catalog.
var entries = assemblyPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
    ? ProjectCatalogReader.Read(assemblyPath, Console.WriteLine)
    : CatalogReader.Read(assemblyPath);
var sentences = entries.Count(entry => entry.Kind == CatalogEntryKind.Sentence);
var values = entries.Count - sentences;

Console.WriteLine($"Catalog: {sentences} sentences, {values} values from {Path.GetFileName(assemblyPath)}");
Console.WriteLine($"Languages: {string.Join(", ", languages)}");
Console.WriteLine($"CSV: {Path.GetFullPath(csvPath)}");
Console.WriteLine();

var store = new CsvTranslationStore(csvPath);

if (verify)
{
    var coverage = CatalogVerifier.Verify(entries, languages, store);

    if (coverage.IsComplete)
    {
        Console.WriteLine($"Coverage complete: {coverage.Present} (key, language) pairs all present.");
        return 0;
    }

    Console.WriteLine($"Coverage INCOMPLETE: {coverage.Missing.Count} pairs missing ({coverage.Present} present):");
    foreach (var (key, language) in coverage.Missing)
    {
        Console.WriteLine($"  [{language}] {key}");
    }

    return 3;
}

var translator = AiTranslator.ForOpenAiCompatibleEndpoint(
    new Uri(Required("Endpoint")),
    Required("ApiKey"),
    Required("Model"),
    configuration["Translator:SystemPrompt"],
    configuration["Translator:ApplicationContext"]);

var filler = new CatalogFiller(translator, store);
var report = await filler.FillAsync(entries, languages, log: Console.WriteLine);

Console.WriteLine();
Console.WriteLine($"Done: {report.Translated} translated, {report.Skipped} already present, {report.Failed} failed.");
return report.Failed == 0 ? 0 : 2;

// Anchor type for user-secrets discovery.
internal sealed class ToolAnchor;
