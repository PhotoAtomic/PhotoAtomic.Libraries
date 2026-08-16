# PhotoAtomic.Internationalization.Tool

The CLI companion of
[PhotoAtomic.Internationalization](https://www.nuget.org/packages/PhotoAtomic.Internationalization),
packaged as a dotnet tool. It opens your csproj with MSBuildWorkspace (Razor components
included), extracts the translation catalog from the `T($"...")` call sites, and lets you
manage translations **before** shipping instead of at runtime.

## Installing and running

```
dotnet tool install -g PhotoAtomic.Internationalization.Tool

pai18n MyApp.csproj --csv translations.csv --all
                                              # THE one command: prune, translate, lint,
                                              # repair — and report what needs a human
pai18n MyApp.csproj --csv translations.csv --verify
                                              # CI-friendly: fails when the catalog has holes
```

The first argument is a csproj (opened via MSBuildWorkspace, Razor included) or a compiled
assembly. Filling uses the same AI translation pipeline as
[PhotoAtomic.Internationalization.AI](https://www.nuget.org/packages/PhotoAtomic.Internationalization.AI)
(configure the provider via appsettings / user secrets / environment variables); `--verify` is
meant for pipelines, so a missing translation breaks the build instead of surprising a user.

## `--all`: the whole workflow, in the right order

`--all` deletes the rows for sentences the code no longer says, translates the **values first
and the sentences after** (sentences are declined against the words already translated — fill a
cold table in one pass and nothing is ever declined), then lints and repairs round after round
until the count stops falling. What is still wrong is printed and **moved to the end of the
CSV**, so opening the file lands on the work. Exit 0 when nothing is left, 4 when a human is
needed; a rerun with nothing to do makes no AI calls and leaves the file byte-identical.

The single steps stay available, for CI and for looking closer:

- `--verify` — no AI: every unit has rows for every language, or exit 3;
- `--lint` — no AI: what the rows themselves get wrong (holes that never arrive or were
  invented, variants never declined, a set of variants with no plain row to fall back on, a
  value that forgot its gender, an example word left in the template, a sentence nobody says
  any more). Exit 4 on an error; warnings only inform;
- `--fix` — repairs what the lint found: from the table where the answer is already there,
  by re-asking the model **with the complaint in hand** where it is not, and lints again so
  the result is a number rather than a hope;
- `--prune` — deletes the dead sentences only (never values), rewriting the CSV. Pass the
  catalog that covers the whole table, or live rows will look dead.

Part of [PhotoAtomic.Libraries](https://github.com/PhotoAtomic/PhotoAtomic.Libraries).
