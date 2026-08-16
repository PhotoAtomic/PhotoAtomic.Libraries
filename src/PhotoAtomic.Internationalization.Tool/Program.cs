using Microsoft.Extensions.Configuration;
using PhotoAtomic;

if (args.Length == 0 || args[0].StartsWith('-'))
{
    Console.WriteLine("PhotoAtomic i18n pre-translation tool");
    Console.WriteLine();
    Console.WriteLine("Usage: <source> [<source>...] [--csv <path>] [--all]");
    Console.WriteLine("       [--verify] [--lint] [--fix] [--prune]");
    Console.WriteLine();
    Console.WriteLine("A source is a project.csproj (source generators run, Razor included), a");
    Console.WriteLine("compiled assembly.dll, or a .json catalog file / directory of them — the");
    Console.WriteLine("content a compiler never sees, emitted by whoever owns it.");
    Console.WriteLine();
    Console.WriteLine("PASS THEM ALL AT ONCE. Code and content belong in one catalog: --prune");
    Console.WriteLine("deletes rows nothing asks for, so a partial catalog deletes the rest.");
    Console.WriteLine();
    Console.WriteLine("  --all     THE ONE COMMAND. Prunes what the code no longer says, translates");
    Console.WriteLine("            the values, then the sentences (in that order: sentences are");
    Console.WriteLine("            declined against the words already translated), then lints and");
    Console.WriteLine("            repairs round after round until it stops improving. Ends with the");
    Console.WriteLine("            list of what a human still has to look at — and moves those rows");
    Console.WriteLine("            to the END of the CSV, so opening the file lands on the work.");
    Console.WriteLine();
    Console.WriteLine("  The single steps, for CI and for looking closer:");
    Console.WriteLine("  --verify  no translation: checks that every catalog unit has rows for");
    Console.WriteLine("            every configured language; exit code 3 when coverage is missing.");
    Console.WriteLine("  --lint    no translation: reads the rows themselves and reports what is");
    Console.WriteLine("            wrong with them (holes that never arrive, variants that were");
    Console.WriteLine("            never declined, values with no gender, example words left in);");
    Console.WriteLine("            exit code 4 when an ERROR is found (warnings only inform).");
    Console.WriteLine("  --fix     repairs what the lint found: from the table where possible,");
    Console.WriteLine("            asking the model again only for the rows it got wrong, then");
    Console.WriteLine("            lints once more and reports the before/after count.");
    Console.WriteLine("  --prune   no translation: deletes the rows for sentences the code no");
    Console.WriteLine("            longer says (never values), rewriting the CSV. Pass the catalog");
    Console.WriteLine("            that covers the whole table, or live rows will look dead.");
    Console.WriteLine();
    Console.WriteLine("Configuration (appsettings.json next to the tool, user secrets, env vars):");
    Console.WriteLine("  Translator:Languages  array of target languages, e.g. [\"it-IT\", \"fr-FR\"]");
    Console.WriteLine("  Translator:Csv        output CSV path (or --csv)");
    Console.WriteLine("  Translator:Endpoint, Translator:Model, Translator:ApiKey   (fill only)");
    Console.WriteLine("  Translator:SystemPrompt, Translator:ApplicationContext     (optional)");
    return 1;
}

// Every source named before the options, together. Code and content end up in
// ONE catalog because half a catalog is dangerous: --prune deletes rows for
// keys nobody asks for, so a run that only knows about the code would happily
// delete every line the rooms say, and a run that only knows the rooms would
// delete the program's own words.
var sources = args.TakeWhile(argument => !argument.StartsWith('-')).ToList();
if (sources.Count == 0)
{
    Console.WriteLine("No source given: name a project, an assembly or a catalog file.");
    return 1;
}
var verify = args.Contains("--verify");
var lint = args.Contains("--lint");
var fix = args.Contains("--fix");
var prune = args.Contains("--prune");
var all = args.Contains("--all");
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

// Three kinds of source, one catalog. A csproj goes through the workspace
// (source generators run there, Razor included, so markup T() calls are
// visible); a dll reads the baked catalog; a .json file or a directory of them
// is content somebody else owns — rooms, records, anything a compiler never
// sees.
var entries = sources
    .SelectMany(source => FileCatalogReader.Handles(source)
        ? FileCatalogReader.Read(source, Console.WriteLine)
        : source.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            ? ProjectCatalogReader.Read(source, Console.WriteLine)
            : CatalogReader.Read(source))
    // The tool's own identity for a unit of translation, applied across
    // sources: the same sentence said by the code and by a room is one row.
    .GroupBy(entry => (entry.Key, entry.Context, entry.Kind))
    .Select(group => group.First())
    .ToList();

var sentences = entries.Count(entry => entry.Kind == CatalogEntryKind.Sentence);
var values = entries.Count - sentences;

Console.WriteLine($"Catalog: {sentences} sentences, {values} values from "
    + string.Join(" + ", sources.Select(Path.GetFileName)));
Console.WriteLine($"Languages: {string.Join(", ", languages)}");
Console.WriteLine($"CSV: {Path.GetFullPath(csvPath)}");
Console.WriteLine();

var store = new CsvTranslationStore(csvPath);

// The catalog is what tells a sentence from a value: everything it does not
// know as a sentence is content — an entity name, a room's title — and content
// is judged by the rules for values.
var sentenceKeys = entries
    .Where(entry => entry.Kind == CatalogEntryKind.Sentence)
    .Select(entry => entry.Key)
    .ToList();

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

if (lint)
{
    // Reading the rows, not the catalog: this is about what the translations
    // SAY, and it needs no model — which is the whole point. Prompt tuning was
    // guesswork until there was a number to watch.
    var findings = TranslationLint.Inspect(store.LoadAll(), sentenceKeys);

    if (findings.Count == 0)
    {
        Console.WriteLine("Lint clean: nothing to report.");
        return 0;
    }

    var byRule = findings.GroupBy(finding => finding.Rule, StringComparer.Ordinal).ToList();
    var errors = findings.Count(finding => finding.Severity == LintSeverity.Error);

    // The tally first: it is the number to watch while tuning a prompt, and it
    // fits on one screen even when the detail does not.
    Console.WriteLine($"Lint found {errors} error(s) and {findings.Count - errors} warning(s):");
    foreach (var rule in byRule)
    {
        var severity = rule.First().Severity == LintSeverity.Error ? "error  " : "warning";
        Console.WriteLine($"  {rule.Count(),5}  {severity}  {rule.Key}");
    }

    const int shown = 8;
    foreach (var rule in byRule)
    {
        Console.WriteLine();
        Console.WriteLine($"  {rule.Key} ({rule.Count()})");
        foreach (var finding in rule.Take(shown))
        {
            var criteria = string.IsNullOrWhiteSpace(finding.Context) ? string.Empty : $" [{finding.Context}]";
            Console.WriteLine($"    [{finding.Language}] {finding.Key}{criteria}");
            Console.WriteLine($"      {finding.Message}");
        }

        if (rule.Count() > shown)
        {
            Console.WriteLine($"    ... and {rule.Count() - shown} more");
        }
    }

    // Only the certain ones are a gate: a warning is for reading, not for
    // stopping a build over a language that behaves unusually.
    return errors > 0 ? 4 : 0;
}

if (prune)
{
    // Deleting is the one thing an append-only table cannot say, and the lint
    // already knows what to delete: a sentence whose key no code asks for any
    // more. Values are never touched — the catalog does not carry the names of
    // things, so their absence from it proves nothing.
    if (sentenceKeys.Count == 0)
    {
        Console.WriteLine("Refusing to prune: the catalog has no sentences at all, so every row "
            + "would look dead. Point the tool at the project that owns these translations.");
        return 5;
    }

    var rows = store.LoadAll().ToList();
    var dead = TranslationLint.Inspect(rows, sentenceKeys)
        .Where(finding => finding.Rule == TranslationLint.Rules.OrphanRow)
        .Select(finding => finding.Key)
        .ToHashSet(StringComparer.Ordinal);

    if (dead.Count == 0)
    {
        Console.WriteLine($"Nothing to prune: all {rows.Count} row(s) belong to something the code still says.");
        return 0;
    }

    var kept = rows.Where(row => !dead.Contains(row.Key)).ToList();

    foreach (var key in dead.Order(StringComparer.Ordinal))
    {
        Console.WriteLine($"  removed: {key}");
    }

    store.ReplaceAll(kept);

    Console.WriteLine();
    Console.WriteLine($"Pruned {rows.Count - kept.Count} row(s) of {dead.Count} dead sentence(s); {kept.Count} remain.");
    return 0;
}

// A translator is built FRESH each time it is asked for, because the values
// already in the store teach us how a language behaves — which trait
// combinations exist, and a real word for each — and that vocabulary grows as
// the run goes on. A translator built once at the start would keep asking for
// the cases the table had before it began.
ITranslator NewTranslator()
{
    var vocabulary = ValueVocabulary.FromStore(store);
    foreach (var language in languages)
    {
        var states = vocabulary.StatesOf(language);
        if (states.Count > 0)
        {
            Console.WriteLine($"Vocabulary [{language}]: {states.Count} trait combinations "
                + $"({string.Join("; ", states.Select(state => $"{string.Join('+', state.Traits)} e.g. {state.Example}"))})");
        }
    }

    return AiTranslator.ForOpenAiCompatibleEndpoint(
        new Uri(Required("Endpoint")),
        Required("ApiKey"),
        Required("Model"),
        configuration["Translator:SystemPrompt"],
        configuration["Translator:ApplicationContext"],
        vocabulary);
}

if (all)
{
    var pipeline = new TranslationPipeline(NewTranslator, store);
    var outcome = await pipeline.RunAsync(entries, languages, Console.WriteLine);

    Console.WriteLine();
    Console.WriteLine("=========================================================");
    Console.WriteLine($"  pruned      {outcome.Pruned,5}  row(s) the code no longer says");
    Console.WriteLine($"  values      {outcome.Values,5}  translated");
    Console.WriteLine($"  sentences   {outcome.Sentences,5}  translated");
    Console.WriteLine($"  repaired    {outcome.Repaired,5}  in {outcome.Rounds} round(s)");
    Console.WriteLine("=========================================================");

    if (outcome.IsClean)
    {
        Console.WriteLine();
        Console.WriteLine("Nothing left to do: every unit is translated and the table lints clean.");
        return 0;
    }

    // What a human has to look at, said plainly and once. The rows are already
    // at the end of the CSV — this is the map to them.
    if (outcome.Missing.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"STILL UNTRANSLATED ({outcome.Missing.Count}) — the model gave nothing usable back:");
        foreach (var (key, language) in outcome.Missing)
        {
            Console.WriteLine($"  [{language}] {key}");
        }
    }

    if (outcome.Remaining.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine($"NEEDS A HUMAN ({outcome.Remaining.Count}) — translated, but the lint is not convinced.");
        Console.WriteLine("These rows are now at the END of the CSV, in this order:");

        foreach (var rule in outcome.Remaining.GroupBy(finding => finding.Rule, StringComparer.Ordinal))
        {
            Console.WriteLine();
            Console.WriteLine($"  {rule.Key} ({rule.Count()})");
            foreach (var finding in rule)
            {
                var criteria = string.IsNullOrWhiteSpace(finding.Context) ? string.Empty : $" [{finding.Context}]";
                Console.WriteLine($"    [{finding.Language}] {finding.Key}{criteria}");
                Console.WriteLine($"      {finding.Message}");
            }
        }
    }

    Console.WriteLine();
    return outcome.Remaining.Any(finding => finding.Severity == LintSeverity.Error)
        || outcome.Missing.Count > 0
            ? 4
            : 0;
}

var translator = NewTranslator();

if (fix)
{
    // Close the loop the lint opened: repair what can be repaired from the
    // table alone, ask the model again ONLY for the rows it got wrong, then
    // lint once more so the result is a number, not a hope.
    var before = TranslationLint.Inspect(store.LoadAll(), sentenceKeys);
    var repair = new TranslationRepair(translator, store);
    var repaired = await repair.RepairAsync(before, entries, sentenceKeys, Console.WriteLine);

    var after = TranslationLint.Inspect(store.LoadAll(), sentenceKeys);
    var wasErrors = before.Count(finding => finding.Severity == LintSeverity.Error);
    var nowErrors = after.Count(finding => finding.Severity == LintSeverity.Error);

    Console.WriteLine();
    Console.WriteLine($"Repaired {repaired.Locally} row(s) from the table itself, "
        + $"re-asked the model for {repaired.Reasked} ({repaired.Failed} failed).");
    Console.WriteLine($"Errors: {wasErrors} -> {nowErrors}.");
    return nowErrors > 0 ? 4 : 0;
}

var filler = new CatalogFiller(translator, store);
var report = await filler.FillAsync(entries, languages, log: Console.WriteLine);

Console.WriteLine();
Console.WriteLine($"Done: {report.Translated} translated, {report.Skipped} already present, {report.Failed} failed.");
return report.Failed == 0 ? 0 : 2;

// Anchor type for user-secrets discovery.
internal sealed class ToolAnchor;
