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
| `PhotoAtomic.Internationalization.Tool` | CLI: pre-translates, lints and repairs the catalog for a set of languages (`--all`), and gates CI (`--verify`, `--lint`). Reads code and JSON catalogs alike. |
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
- `lowercase-opening` — the translation opens in lowercase where the source
  opens a sentence (the other half of no longer patching templates silently);
  only when the source itself opens with an uppercase letter, since a key
  starting with a hole or a digit makes no claim about how it should open;
- `undeclared-capital` — a value written with a capital it never declared with
  the `Capitalize` trait: it will keep that capital mid-sentence
  ("Giocatore Uno intasca la Ricetta segreta"). Either answer fixes it — say
  `Capitalize` if it is a name, lowercase it if it is a common noun — and
  reporting beats correcting, because the model that wrote it knows which of
  the two it meant;
- `article-in-value` — a common noun that arrived with its article attached
  ("il sapore di quello stufato"): a value goes into a hole bare and the
  sentence supplies its own, so the reader gets it twice ("Metti il il sapore
  di quello stufato nella pentola"). A proper name that genuinely carries one
  is spared, because it says so with the `Capitalize` trait; a value of one
  word is spared too (a name a language happens to spell like its article is
  that language's business); and a language whose articles nobody encoded
  (`GrammarRules.ArticlesOf`) is not judged at all — silence beats being wrong
  about a grammar we never wrote down;
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

### `Spelled` — numbers in words

`T($"You found {new Spelled(count)} coins")` renders "two coins", "due
monete", "dà dhà bhonn". The number becomes a translated VALUE like any other:
the row for `"2"` in Italian says `due`, so nobody hard-codes number words —
least of all in a language nobody on the team speaks. Untranslated it renders
as its digits, which is never wrong, only less charming. `int`, `long`,
`decimal` and `double` convert implicitly.

The point of the type is what it does **not** lose: `PluralRules.CategoryOf`
unwraps it, so the sentence around it still agrees with the amount. Wrapping a
number normally hides it, and `"{0} coins"` would stop knowing it holds a two —
which matters most where you least expect it, since Scottish Gaelic has a
category for TWO and puts the following noun in the lenited singular. Its key
is formatted with the invariant culture on purpose: a key that moved with the
reader's language would make `1000` and `1,000` two different words to learn.

Where the words come from is not this library's business:
`UseNumberWords((amount, language) => ...)` attaches any speller — Humanizer
does this offline for a dozen-odd languages, with no model and no network. A
hook rather than a dependency, because the core ships inside a WebAssembly
client: whoever wants a spelling library pays for it, whoever does not gets
digits. The order is **table, then speller, then digits** — a row always wins,
since a library knows a language's cardinals but not that *this* sentence
wants the feminine, or the irregular form, or the word no library knows.

**Declining is part of the contract.** A speller that does not know a language
must return `null`, not guess: some libraries answer for Gaelic and Welsh by
falling back to English, and an English word inside a Gaelic sentence is a
mistake nobody will notice, while the digit it replaced was merely plain.

### `Phrase` — a sentence written as data

`new Phrase("The {liquid} boils away in the {vessel}").Render(values)` is the
same sentence a `T($"...")` call site produces, except nobody compiled it. It
exists because content is increasingly **written by a model**, and a model
cannot compile a call site: a generated room brings its own rules and its own
verbs, and without this it could not bring its own words — everything it
invented would fall back on a generic sentence written in advance by a
programmer who could not know about it.

The holes are **named**, not numbered, because the data around them already
names things (a rule binds `liquid` and `vessel`, an action binds `actor` and
`target`). Those names become the legend handed to the translator, so an AI is
told "{0} is the liquid" — better than what a C# call site usually manages. A
hole may declare a context after a colon (`{cook:person}`); doubled braces are
literal braces, exactly as in C#. `Key` exposes the structural key
(`"The {0} boils away in the {1}"`), identical to what the compiler derives.

Rendering does not reimplement anything: it replays the phrase into the real
interpolated-string handler and calls `T()`, so a data sentence and a compiled
one take the same path — same key, same facts, same matching, same table, same
lint. A hole nobody bound renders as its own name rather than throwing.

### `FileCatalogReader` — the content a compiler never sees

A catalog read from a **file**: `CatalogEntry` written down as JSON, for text
that is data — rooms written by an AI, rows in a database, a CMS export. The
other two readers derive a catalog by looking at a program; this one is handed
one by whoever owns the content, and from that point downstream nothing can
tell the difference: vocabulary order, lint, repair and pruning all work as
they do for code. `Handles(path)` takes a `.json` file or a directory of them
(read shallow, in name order, so two runs see the same catalog in the same
sequence); a file that turns out not to be a catalog is reported and skipped
rather than failing the run. `ToJson(entries)` writes the shape it expects.

### One thing, one name — `Setting`, `Glossary`, `Lexicon`

Values are translated one at a time, each its own question, and that is how a
single object ends up with two names: a room came back with a "pressa pesante
in pietra" standing next to the "base di torchio in pietra" that belongs to it,
and in French a "presse à fruits" beside a "pressoir". Neither answer was wrong
on its own — the question simply never mentioned the other one.

So `TranslationRequest` carries what the caller already knows:

- `Setting` — where the term lives, in one line of prose: the scene around it,
  the company it keeps. A name is ambiguous alone and obvious in place — a
  "press" among mortars, pestles and herbs is not a printing press, and its
  "base" is the base *of* something rather than something made of it.
  Deliberately **not** the sentences the term appears in, which was the first
  idea: for a value those are the narrator's generic lines, identical for every
  object, and a sentence put in front of a model is a sentence it can copy
  from (`example-left-in`).
- `Glossary` — the terms already settled in the same body of content, source
  term next to the translation accepted for it, with the instruction to reuse
  them exactly. The same bargain the sentence variants already make: the choice
  a language forces gets made **once** and then held to, so wrong preposition,
  wrong synonym and wrong register stop being three defects.
- `Kind` — sentence or value, when the caller knows. Left to itself the model
  guesses from length: "Steam" is obviously a name, "The taste of that stew"
  reads like a sentence and comes back with no gender declared — and a value
  with no gender makes every sentence naming it fall back to the source
  language. Whoever built the catalog knows which it is; saying so removes a
  guess.

`CatalogEntry.Setting` carries that line through the catalog and doubles as an
**identity**: units sharing a setting are pre-translated in order, each told
what the ones before it settled on, while different settings stay parallel (see
the Tool). `Lexicon.RelevantTo(settled, key)` decides how much of the glossary
is worth showing — only the terms sharing a word with the key, because that
shared word is exactly what can come back translated two different ways.
Not all of them: a model told to reuse a dozen unrelated words starts working
them in, and a mortar has nothing to say about a bundle of herbs.

### `GrammarRules` and `WellKnownTraits` — capitalization and elision

Values are stored lowercase. The trait `Capitalize` keeps an uppercase initial
everywhere (proper names, German nouns). `WellKnownTraits` holds the
conventional vocabulary: `GENDER-male`, `GENDER-female`, `starts-with-vowel`,
`Capitalize`.

**The capital goes on the value that opens the sentence, never on the
template.** A value is stored lowercase and needs one; a template was written
by someone who already decided how it opens, and patching its first letter on
the way to the screen both presumes and conceals — a machine translation that
forgot its capital used to be silently corrected and nobody ever knew. Now the
renderer asks `GrammarRules.HolesOpeningASentence(template)` which holes are in
opening position (of the row actually chosen: a language may put the subject
last) and applies `CapitalizeInitial(value, language)` to those, stepping over
leading punctuation so a quoted value still counts. A template that forgot its
own capital is reported by the lint instead, as `lowercase-opening`.

Elision is mechanics too, and applied after rendering:
`GrammarRules.ApplyElision` contracts the little words that must give way
before a vowel — "mette la acqua" → "mette l'acqua", "le pot de eau" → "le pot
d'eau" — keeping the capitalization of the word it replaces ("La" → "L'").
Italian and French are covered; languages without elision pass through
untouched. `ArticlesOf(language)` is a table of its own, kept apart from the
elisions although the two look alike: "what elides" and "what is an article"
are different questions, and the French elision list carries pronouns and
conjunctions that have no business accusing a name. `StartsWithVowelSound(text, language)` is the underlying test, aware
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
  A lowercase value is safe at the start of a sentence — the engine capitalizes
  the value that opens one — but the model is told plainly that its **templates
  are never touched**: each is written with the capitalization it will have on
  screen.
- the **setting** first and the **glossary** second, when the request carries
  them: what the thing is, then what it has already been called, with the
  instruction to keep the same word for it and to copy nothing out of the
  setting line itself. And whether the key is a value or a sentence, said
  outright rather than inferred from how long it is — a value is asked for as
  a term, with a gender and without a leading article.

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
tool <source> [<source>...] [--csv <path>] --all
```

A **source** is a `project.csproj` (source generators run, Razor included), a
compiled `assembly.dll`, or a `.json` catalog file / directory of them — the
content a compiler never sees, emitted by whoever owns it. **Pass them all at
once:** code and content belong in one catalog, because `--prune` deletes rows
nothing asks for, so a run that only knows the code would happily delete every
line the rooms say, and vice versa. Sources are merged on `(key, context,
kind)`: the same sentence said by the code and by a room is one row.

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
tool <source> [<source>...] [--csv <path>] [--verify] [--lint] [--fix] [--prune]
```

- **dll mode** reads the baked `TranslationCatalog`.
- **csproj mode** opens the project with `MSBuildWorkspace`, where source
  generators run inside the compilation — **Razor included**: `T(...)` calls
  written in `.razor` markup are extracted with full semantics (the shared
  `SiteExtraction` brain guarantees parity with the generator).
- **catalog mode** reads `CatalogEntry` records out of `.json` files
  (`FileCatalogReader`), for text the compiler never sees. A file in the
  directory that is not a catalog is reported and skipped, not fatal.
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
