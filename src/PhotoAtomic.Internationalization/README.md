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

A translated row that invented a hole the sentence does not have would throw at
render time; the engine catches that and falls back to the source template, so a
bad row never takes the screen down.

### `[Translatable]` — values translated by content

Sentences translate by structure; values of marked types translate by
*content*: their rendered text is looked up as a key of its own
("Red" → "Rosso"). The attribute takes an optional context, allows multiple,
and applies to enum members too (member contexts add to type contexts).

### `Value(value, context?)` — a value on its own

`T($"{item}")` would invite a translator to wrap the value in an article, which
is exactly what values must never carry. `Value(item)` translates a value
outside any sentence — a label, a list entry, an item name in the UI — looking
it up by content with its traits, honouring `Capitalize`, and queueing a
background fill on a miss. Untranslated values render as they are.

### `ValueHygiene` — what a usable value looks like

A sentence row is chosen by the traits of the values filling its holes, so a
trait that never arrives does not degrade gracefully: it makes the whole
sentence fall back to the source language. One object whose translation forgot
to declare its gender is enough to make it speak English among forty perfect
neighbours. `ValueHygiene` holds the checks — cheaper than debugging that:

- `UsesGrammaticalGender(corpus, language)` reads from the corpus whether a
  language declines gender, instead of assuming it: if any value already
  translated into it declares a `GENDER-` trait, one that declares none is a
  hole. No list of languages to maintain, and a language nobody predicted is
  covered the moment its first gendered value arrives. `DeclaresGender(row)`
  (also over a set of rows) is the per-value test — the natural cue to ask the
  translator again;
- `AsCommonNoun(row)` lowercases an initial the model gave unbidden ("Falò" →
  "falò"): a capital inside a value survives everywhere it appears, and
  "there is the Bucket in the chest" reads like a typo. `GrammarRules` puts
  the capital back at the start of a sentence. Values declaring `Capitalize`
  (German nouns, names) are left alone;
- `AsProperNoun(row)` marks a value that keeps its capital wherever it lands,
  with the trait rather than by hoping nobody lowercases it. Idempotent.

Deciding *which* values are proper names stays with the application: only it
knows that "The Pirate Galley" is a place and "iron lever" is a thing.

### `TranslationLint` — what is wrong with a table, measured

Prompt tuning was guesswork: a checklist added here, an instruction removed
there, and no way to tell whether the corpus got better or worse.
`TranslationLint.Inspect(rows, sentenceKeys?)` reads the translations
themselves — **no model in the loop**, the customs desk is arithmetic — and
returns `LintFinding`s ordered by rule then key, so two runs read the same.
Errors are defects by construction, warnings are suspicions worth reading
(`LintSeverity`), so CI can gate on the certain ones alone. Every rule was
earned by a defect that reached a running application; the names are constants
on `TranslationLint.Rules`:

- `missing-hole` / `stray-hole` — a hole of the key that never arrives in the
  template (the sentence renders half-said), or one the key does not have
  (`FormatException` at render time);
- `inconsistent-agreement` — one hole changes the sentence in some of its
  cases and not in others: one of the two is wrong;
- `no-fallback-row` — variants with no plain row, so a combination nobody
  foresaw finds nothing and falls back to the source language;
- `genderless-value` — a value that forgot to say its gender, in a language
  that declines it (read from the corpus, not from a list);
- `example-left-in` — the template kept a word from the example it was shown
  ("infila la {1} nel falò");
- `disputed-capitalization` — one language calls the value a proper name and
  another does not;
- `orphan-row` — a sentence nobody says any more, the code that asked for it
  is gone. Only reported when the caller passes the sentence keys it knows.

`WithFallback(rows)` is the repair that belongs next to the writer rather than
to the tool: a set of variants with no unconditional row gets the plain one
they forgot (the least committed variant), so a value with unforeseen traits
finds *something* instead of English.

### `PluralRules` — CLDR categories, no AI

`CategoryOf(value, language)` maps a number to its CLDR category
(`CLDR-zero/one/two/few/many/other` — constants on the class), with real rules
for Arabic (all six, modulo 100), Welsh (six is `many`!), Scottish Gaelic
(vigesimal: 11 is `one`), Russian/Ukrainian/Polish (modulo), French (0 is
`one`), and the one/other default. `CategoriesOf(language)` lists what a
language distinguishes — used as an explicit checklist in AI prompts.

### `GrammarRules` and `WellKnownTraits` — capitalization and elision

Values are stored lowercase. The trait `Capitalize` keeps an uppercase initial
everywhere (proper names, German nouns). Everything positional is mechanics:
`GrammarRules.ApplySentenceCapitalization` uppercases sentence openings and
letters after `.` `!` `?`, transparently through quotes, leaving digit-led
sentences alone ("2 chiavi rotte"). `WellKnownTraits` holds the conventional
vocabulary: `GENDER-male`, `GENDER-female`, `starts-with-vowel`, `Capitalize`.

Elision is mechanics too, and applied after rendering:
`GrammarRules.ApplyElision` contracts the little words that must give way
before a vowel — "mette la acqua" → "mette l'acqua", "le pot de eau" → "le pot
d'eau" — keeping the capitalization of the word it replaces ("La" → "L'").
Italian and French are covered; languages without elision pass through
untouched. `StartsWithVowelSound(text, language)` is the underlying test, aware
of the silent H of Italian, French, Spanish, Portuguese and Catalan, and of
accented vowels. Doing this deterministically keeps the rows few: translators —
human or AI — spend their attention on gender and meaning, not on one variant
per initial letter.

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
`CsvTranslationStore.Parse(content)` reads the same format out of a string,
for hosts that have no filesystem — a WebAssembly client fetching the table
over HTTP shares the file store's format exactly. The file is written as UTF-8
**without** a byte order mark, and a mark left by someone else's editor is
stripped on read: in front of the header it would make the first field read as
`﻿key`, turning the header itself into a translation row.

Deleting is the one thing appending cannot say, so it is asked for by name:
`IRewritableTranslationStore.ReplaceAll(rows)` (implemented by
`CsvTranslationStore`) rewrites the whole table in the given order, via a
temporary file moved over the original — a crash halfway leaves the old table
intact rather than half a new one. Most stores have no business deleting,
which is why it is a separate interface.

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
`ForOpenAiCompatibleEndpoint(endpoint, apiKey, model, systemPrompt?, applicationContext?, vocabulary?, retry?)`
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

### The program owns the combinatorics, the model owns the grammar

Enumerating which grammatical cases a sentence needs is bookkeeping, and a
model asked to do it either forgets cases or invents hundreds. So
`VariantCases.For(request, vocabulary?)` computes them — one case per real
situation, capped at `MaxCases` by narrowing the widest axis to its plainest
states rather than truncating the list — and each is then asked for **by name,
one per call**.

The states of a hole are not a hardcoded list of genders: `ValueVocabulary`
(`FromStore` / `FromRows`) observes the trait combinations the language's
already-translated **values** actually declared, and keeps a real word for
each. So a language carrying traits nobody anticipated — vowel harmony, impure
S, a noun class system — still gets one case per situation, with a concrete
example attached. Translate values first; the sentences then ask for exactly
the cases those words can produce.

The sentence reaches the model with the word already inside each hole —
`"You have {0:'3'} coins in your {1:'borsa'}"` — and the model writes the
translation *around* the braces. It never has to put a placeholder back, and
every answer is verified hole by hole: a dissolved placeholder, an invented
one, or an example word that leaked outside its brace ("infila la {1} nel
falò") is rejected and asked again. The first accepted wording leads the
others, so the app does not switch verbs depending on the gender of an object.

Two traits are derived rather than asked, because they are mechanical and
models forget them: `starts-with-vowel` on a value that begins with one, and
the unconditional generic row for a value answered only in plural variants.

### Asking again — the complaint, and the line

`TranslationRequest.Feedback` carries what was wrong with the last answer to
this same request, in the words of whoever rejected it ("the sentence agrees
with hole {1} in some cases but not in others"). It reaches the model as the
**last** paragraph of the prompt, after every rule and example, because that is
the one thing not true of every other call. Without it a second attempt is
pointless: at temperature 0 the same question returns the same defect forever.

A refused answer and a call that never arrived are different failures.
`TransportRetry(attempts, backoff)` covers only the second: the line breaking,
timing out or throttling gets asked again after a doubling pause (`Default` is
used when none is given), while a 4xx with a verdict of its own — a wrong key,
a model that does not exist, a request we malformed — is final, since insisting
only spends the same failure four times. Refusals are judged upstream and never
retried here.

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
tool <assembly.dll | project.csproj> [--csv <path>] --all
```

**`--all` is the whole workflow in one command.** Write your UI with `T(...)`,
list your target languages in the config, run it once:

1. **prune** — rows for sentences the code no longer says are deleted (values
   are never touched: the catalog does not carry the names of things, so their
   absence from it proves nothing).
2. **values, then sentences** — in that order, and the order is the point. A
   sentence asks for the grammatical cases its holes can actually produce, and
   those come from the values already translated. Fill a cold table in one pass
   and every sentence is written while the vocabulary is still empty: nothing
   is ever declined, and the engine's whole reason for existing is gone.
3. **lint → repair → lint**, round after round, until the count stops falling
   (three rounds at most). Only the units the lint complained about are asked
   again, one at a time, each with the complaint in hand — a model answering at
   temperature 0 needs a different question, not the same one twice.
4. **what is still wrong is reported and moved to the END of the CSV**, so a
   human opens the file and lands on the work. Whole units move together: among
   rows with the same criteria the last one wins, and those rows always live in
   the same unit, so moving units is safe where moving single rows would not be.

Exit 0 when nothing is left, 4 when something needs a human. A rerun with
nothing to do makes no AI calls and leaves the file byte-identical.

The single steps stay available for CI and for looking closer:

```
tool <assembly.dll | project.csproj> [--csv <path>] [--verify] [--lint] [--fix] [--prune]
```

- **dll mode** reads the baked `TranslationCatalog`.
- **csproj mode** opens the project with `MSBuildWorkspace`, where source
  generators run inside the compilation — **Razor included**: `T(...)` calls
  written in `.razor` markup are extracted with full semantics (the shared
  `SiteExtraction` brain guarantees parity with the generator).
- **fill** (default): translates delta-style — (key, language) pairs already
  in the CSV are skipped; reruns only pay for what is new. Limited parallelism,
  failures counted, exit 2 on any. It first builds the `ValueVocabulary` from
  the values already in the store and prints what it found per language, so
  sentence variants are asked for against real words.
- **`--verify`**: no AI, no network — checks that every unit has rows for every
  configured language; prints each missing pair and exits 3. The CI gate.
- **`--lint`**: no AI — reads what the translations SAY and reports what is
  wrong with them (the rules of `TranslationLint`, tallied per rule and then
  detailed). Exit 4 on an error; warnings only inform.
- **`--fix`**: repairs what the lint found — from the table itself where the
  answer is there (the missing plain row is the least committed variant), and
  by asking the model again, one unit at a time, where it is not. Every answer
  is linted before it is believed, and a repaired unit REPLACES its old rows
  rather than being appended over them.
- **`--prune`**: deletes the dead sentences. Refuses to run when the catalog
  has no sentences at all — that is a catalog that failed to load, not a table
  that died.

Configuration (appsettings.json next to the tool + user secrets + env vars):
`Translator:Languages` (array), `Translator:Csv`, `Translator:Endpoint`,
`Translator:Model`, `Translator:ApiKey`, optional `Translator:SystemPrompt`
and `Translator:ApplicationContext`.

The intended shape: static content is pre-translated and shipped in the CSV
(no network at runtime) with `--all`, the runtime AI fill covers dynamically
generated content, and `--verify` (or `--lint`) keeps everyone honest in CI.
