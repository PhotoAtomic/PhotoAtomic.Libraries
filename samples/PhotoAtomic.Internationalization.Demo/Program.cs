using Microsoft.Extensions.Configuration;
using PhotoAtomic;
using static PhotoAtomic.Internationalization;

// Configuration sources, later wins: appsettings.json (committed, no secrets)
// then user secrets (endpoint/model overrides and the API key):
//   dotnet user-secrets set "Translator:ApiKey" "..." --project samples/PhotoAtomic.Internationalization.Demo
// Optional keys: Translator:SystemPrompt (full prompt override) and
// Translator:ApplicationContext (additive: what the app is about, for tone).
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets<DemoAnchor>()
    .Build();

string Required(string key) =>
    configuration[$"Translator:{key}"]
    ?? throw new InvalidOperationException($"Missing configuration value Translator:{key}");

var endpoint = Required("Endpoint");
var model = Required("Model");
var apiKey = Required("ApiKey");
var systemPrompt = configuration["Translator:SystemPrompt"];
var applicationContext = configuration["Translator:ApplicationContext"];

var csvPath = Path.Combine(AppContext.BaseDirectory, "translations.csv");
Console.WriteLine($"Translation table: {csvPath}");
Console.WriteLine($"Model: {model}");

UseStore(new CsvTranslationStore(csvPath));
UseTranslator(AiTranslator.ForOpenAiCompatibleEndpoint(new Uri(endpoint), apiKey, model, systemPrompt, applicationContext));

Language = "it-IT";

var coins = 3;
var item = Item.Key;

Console.WriteLine();
Console.WriteLine("First render (misses fall back to English and queue AI fills):");
Console.WriteLine($"  {T($"You found {coins} golden coins")}");
Console.WriteLine($"  {T($"The {item} is broken")}");

Console.WriteLine();
Console.Write("Waiting for background AI fills... ");
await WhenIdleAsync();
Console.WriteLine("done.");

Console.WriteLine();
Console.WriteLine("Second render (rows filled by the AI, or already persisted):");
Console.WriteLine($"  {T($"You found {coins} golden coins")}");
var one = 1;
Console.WriteLine($"  {T($"You found {one} golden coins")}");
Console.WriteLine($"  {T($"The {item} is broken")}");

Console.WriteLine();
Console.WriteLine("Rows now in the CSV:");
foreach (var line in File.ReadAllLines(csvPath))
{
    Console.WriteLine($"  {line}");
}

[Translatable("tool")]
enum Item
{
    Hammer,
    Key,
}

// Anchor type for user-secrets discovery (top-level programs have no Program class in scope here).
internal sealed class DemoAnchor;
