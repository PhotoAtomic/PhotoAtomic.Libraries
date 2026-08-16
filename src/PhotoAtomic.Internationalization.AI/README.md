# PhotoAtomic.Internationalization.AI

AI-powered translation filler for
[PhotoAtomic.Internationalization](https://www.nuget.org/packages/PhotoAtomic.Internationalization):
completes the missing entries of your translation catalog **in the background**, while the
application keeps serving the source-language sentence until the translation lands.

Built on **Microsoft.Extensions.AI**, so it works with OpenAI and any OpenAI-compatible
provider. Every translation request carries the structural information the core engine
collects — facts, criteria, context, the legend of each placeholder — so the model translates
with grammar-aware instructions instead of a bare string.

The grammatical cases a sentence needs are computed here, not left to the model:
`ValueVocabulary` observes which trait combinations the language's already-translated values
declare (and a real word for each), `VariantCases` turns them into the exact list of rows to
produce, and each case is asked for by name, one call at a time, with its example word already
sitting inside the placeholder. Answers are verified hole by hole and asked again when a
placeholder went missing, was invented, or an example word leaked into the template.

The prompt is explicit about who owns the capital letters: values come back bare and lowercase
(the engine capitalizes the one that opens a sentence), while **templates are never touched**,
so the model writes each with the capitalization it will have on screen — and a template that
forgets it is reported by the lint rather than silently patched on the way to the display.

When an answer has to be asked for again, the reason travels with the question:
`TranslationRequest.Feedback` reaches the model as the last paragraph of the prompt, because at
temperature 0 the same question returns the same defect forever. A call that never arrived is a
different failure from an answer that was refused: `TransportRetry` asks again after a doubling
pause when the line broke, timed out or throttled, and gives up immediately on a 4xx that will
fail the same way however often it is repeated.

## Installing

```
dotnet add package PhotoAtomic.Internationalization.AI
```

Kept separate from the core on purpose: `PhotoAtomic.Internationalization` stays
dependency-free, and you add the AI machinery (and its Microsoft.Extensions.AI dependencies)
only where you want background fill. For a build-time / CI alternative, the same fill logic is
available as the [`pai18n`](https://www.nuget.org/packages/PhotoAtomic.Internationalization.Tool)
dotnet tool.

Part of [PhotoAtomic.Libraries](https://github.com/PhotoAtomic/PhotoAtomic.Libraries).
