# PhotoAtomic.Internationalization.SourceGen

Opt-in **compile-time companion** for
[PhotoAtomic.Internationalization](https://www.nuget.org/packages/PhotoAtomic.Internationalization):
a Roslyn source generator that scans your `T($"...")` call sites and builds the **translation
catalog** at build time, plus an analyzer that keeps the call sites honest.

## What you get

- **Catalog generation** — every translatable sentence in your code (Razor included) becomes a
  catalog entry with its structural key, facts and context, ready for the translation store and
  for the [`pai18n`](https://www.nuget.org/packages/PhotoAtomic.Internationalization.Tool)
  pre-translation workflow. No runtime scanning, no missed sentences.
- **Analyzer `PAI18N001`** — warns when a `context:` argument cannot be resolved at compile
  time (it understands literals, constants, ternaries, switch expressions and single-assignment
  locals), so catalog and runtime can never disagree silently.

## Installing

```
dotnet add package PhotoAtomic.Internationalization           # the runtime core
dotnet add package PhotoAtomic.Internationalization.SourceGen # this analyzer
```

The split is deliberate: the core works fine on its own at runtime, and you only pay for the
build-time machinery in projects that want the compile-time catalog. This package is an
analyzer — it runs inside the compiler and adds no runtime dependency of its own.

Part of [PhotoAtomic.Libraries](https://github.com/PhotoAtomic/PhotoAtomic.Libraries).
