# PhotoAtomic.Internationalization

Structural-key internationalization for .NET, born inside the PartyOf2 project.
One idea drives everything: **the sentence you write in code *is* the
translation key**, captured structurally by the compiler — and everything a
translator (human or AI) needs to do a grammatically correct job travels with
it as data.

```csharp
using static PhotoAtomic.Internationalization;

var count = 3;
var item = Item.Key;

Language = "it-IT";
Console.WriteLine(T($"You found {count} golden coins"));  // Hai trovato 3 monete d'oro
Console.WriteLine(T($"The {item} is broken"));            // La chiave è rotta
```

The family is five projects:

| Project | What it is |
|---|---|
| `PhotoAtomic.Internationalization` | The core engine. Zero dependencies. |
| `PhotoAtomic.Internationalization.AI` | `ITranslator` implementation on Microsoft.Extensions.AI (any OpenAI-compatible endpoint). |
| `PhotoAtomic.Internationalization.SourceGen` | Roslyn source generator + analyzer: extracts every `T(...)` into a compile-time catalog. |
| `PhotoAtomic.Internationalization.Tool` | CLI: pre-translates the catalog for a set of languages (`fill`) and gates CI (`--verify`). |
| `PhotoAtomic.IndentedStrings` | Vendored helper (author: PhotoAtomic, from the Darc repo) for readable code generation. |

---

## The core engine

### `T($"...")` — translate an interpolated string

A custom `[InterpolatedStringHandler]` receives the string *as structure*:
literal parts and typed hole values, separately, before any formatting. From it
the engine derives:

- the **structural key**: `T($"{color} is the color of {sentiment}")` →
  `"{0} is the color of {1}"`. Positional, so renaming a variable never breaks
  existing translations;
- the **legend**: the source expression of each hole (`["color", "sentiment"]`,
  via `CallerArgumentExpression`) — semantic context for translators, never part
  of the identity;
- the **facts** of the call (see below).

The translated template may reorder holes freely: `"Il colore di {1} è il {0}"`.
With no matching row, the key itself renders the source language (`en-US`):
untranslated text always displays correctly. An optional context argument
disambiguates identical sentences: `T($"Open", "verb")` vs `T($"Open", "state")`.

`KeyOf($"...")` and `LegendOf($"...")` expose key and legend for tests/tooling.

### The facts & criteria matching engine

Every translation row carries **criteria** (column `context`) and **traits**.
At each `T()` call the engine collects the **facts**:

- the sentence context argument (`"menu"`);
- the contexts of `[Translatable("ctx")]` hole types — attribute allows
  multiple, and on enum *members* too, additively (`1:tool`, `1:music`);
- the CLDR plural category of numeric holes (`0:CLDR-one`);
- the traits declared by the chosen value translations (`1:GENDER-female`,
  `1:starts-with-vowel`), which flow upward into the sentence lookup.

A row matches only when **all** of its criteria are satisfied; among matches the
most specific (most criteria) wins; ties go to the **last registered** row.
Tags are free strings — the engine matches, it never interprets.

This one mechanism yields context disambiguation, plural selection, gender
agreement and elision:

```
"The {0} is broken"  0:GENDER-male                          "Il {0} è rotto"
"The {0} is broken"  0:GENDER-female                        "La {0} è rotta"
"The {0} is broken"  0:GENDER-female,0:starts-with-vowel    "L'{0} è rotta"
"Key"                tool                                   "chiave"    traits: GENDER-female
"Key"                tool,CLDR-other                        "chiavi"    traits: GENDER-female
```

`T($"The {item} is broken")` with `item = Item.Key` renders **"La chiave è
rotta"**; with 2 keys, `T($"{count} broken {item}")` renders **"2 chiavi
rotte"** — value plural and sentence agreement both selected mechanically.

### `[Translatable]` — values translated by content

Sentences translate by structure; values of marked types translate by
*content*: their rendered text is looked up as a key of its own
("Red" → "Rosso"). The attribute takes an optional context, allows multiple,
and applies to enum members too (member contexts add to type contexts).

### `PluralRules` — CLDR categories, no AI

`CategoryOf(value, language)` maps a number to its CLDR category
(`CLDR-zero/one/two/few/many/other` — constants on the class), with real rules
for Arabic (all six, modulo 100), Welsh (six is `many`!), Scottish Gaelic
(vigesimal: 11 is `one`), Russian/Ukrainian/Polish (modulo), French (0 is
`one`), and the one/other default. `CategoriesOf(language)` lists what a
language distinguishes — used as an explicit checklist in AI prompts.

### `GrammarRules` and `WellKnownTraits` — capitalization

Values are stored lowercase. The trait `Capitalize` keeps an uppercase initial
everywhere (proper names, German nouns). Everything positional is mechanics:
`GrammarRules.ApplySentenceCapitalization` uppercases sentence openings and
letters after `.` `!` `?`, transparently through quotes, leaving digit-led
sentences alone ("2 chiavi rotte"). `WellKnownTraits` holds the conventional
vocabulary: `GENDER-male`, `GENDER-female`, `starts-with-vowel`, `Capitalize`.

### `Language` — ambient and async-safe

`Internationalization.Language` is an `AsyncLocal`: a server can translate for
players with different languages in the same process.

### Persistence — `ITranslationStore` / `CsvTranslationStore`

`UseStore(store)` loads all rows and write-through persists every future
registration. The CSV store is RFC 4180 with a header
(`key,context,language,template,traits`), always-quoted fields, doubled quotes,
real newlines allowed — it opens cleanly in any spreadsheet. Append-only:
the last row for equal specificity wins, so a bad row is fixed by appending a
better one. Reads are eager with permissive sharing (concurrent append/load safe).

### Runtime AI fill — `ITranslator` / `UseTranslator`

On a miss, `T()` renders the fallback immediately and queues a background fill
(one per key+language; failures stay recorded so a broken endpoint is not
hammered). The next render finds the row, persisted through the store.
`WhenIdleAsync()` awaits pending fills (tests, demos, shutdown). This is the
path that makes runtime-generated content translatable at all.

---

## PhotoAtomic.Internationalization.AI

`AiTranslator` implements `ITranslator` on `IChatClient`
(Microsoft.Extensions.AI), so any provider fits;
`ForOpenAiCompatibleEndpoint(endpoint, apiKey, model, systemPrompt?, applicationContext?)`
connects to e.g. Azure AI Foundry. The prompt teaches the row format and the
exact tag vocabulary, and includes hard-won rules:

- single-word keys: facts are the **semantic domain** — never a homonym from
  another domain ("Key" + `tool` → "clé", not "touche");
- explicit **checklists** per request: the CLDR categories of the target
  language and the gender traits — the model ticks a list instead of recalling
  CLDR from memory (this fixed plural-variant completeness);
- values are inserted bare and lowercase; articles live in sentence templates.

`systemPrompt` replaces the default entirely (expert use);
`applicationContext` is additive — "a point-and-click adventure game" steers
word senses and tone without losing the format rules. Parsing is tolerant
(markdown fences, garbage answers → no rows).

---

## SourceGen: the compile-time catalog

`TranslationCatalogGenerator` (attach via
`<ProjectReference ... OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`)
emits `PhotoAtomic.Generated.TranslationCatalog` into your assembly: one
`CatalogEntry(Key, Context, Legend, Facts, Kind)` per translation unit —
sentences with their compile-time facts (`0:tool`, `0:CLDR-other`), plus the
full **universe of `[Translatable]` enum members** as `Value` entries. Unit
identity is `(Key, Context, Kind)`.

Contexts are resolved statically by the shared `ContextResolver`: constants
(via the compiler's own constant folding), ternaries and switch expressions
over resolvable branches, single-assignment locals. Unresolvable contexts are
reported by the analyzer **PAI18N001** (warning; promote per repo with
`dotnet_diagnostic.PAI18N001.severity = error`). Contract tests pin the
generator-derived key to the runtime `KeyOf` — they cannot diverge silently.

---

## The Tool: pre-translate at build time, verify in CI

```
tool <assembly.dll | project.csproj> [--csv <path>] [--verify]
```

- **dll mode** reads the baked `TranslationCatalog`.
- **csproj mode** opens the project with `MSBuildWorkspace`, where source
  generators run inside the compilation — **Razor included**: `T(...)` calls
  written in `.razor` markup are extracted with full semantics (the shared
  `SiteExtraction` brain guarantees parity with the generator).
- **fill** (default): translates delta-style — (key, language) pairs already
  in the CSV are skipped; reruns only pay for what is new. Limited parallelism,
  failures counted, exit 2 on any.
- **`--verify`**: no AI, no network — checks that every unit has rows for every
  configured language; prints each missing pair and exits 3. The CI gate.

Configuration (appsettings.json next to the tool + user secrets + env vars):
`Translator:Languages` (array), `Translator:Csv`, `Translator:Endpoint`,
`Translator:Model`, `Translator:ApiKey`, optional `Translator:SystemPrompt`
and `Translator:ApplicationContext`.

The intended shape: static content is pre-translated and shipped in the CSV
(no network at runtime), the runtime AI fill covers dynamically generated
content, and `--verify` keeps everyone honest.
