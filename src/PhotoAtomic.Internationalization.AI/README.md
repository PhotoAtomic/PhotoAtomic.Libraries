# PhotoAtomic.Internationalization.AI

AI-powered translation filler for
[PhotoAtomic.Internationalization](https://www.nuget.org/packages/PhotoAtomic.Internationalization):
completes the missing entries of your translation catalog **in the background**, while the
application keeps serving the source-language sentence until the translation lands.

Built on **Microsoft.Extensions.AI**, so it works with OpenAI and any OpenAI-compatible
provider. Every translation request carries the structural information the core engine
collects — facts, criteria, context, the legend of each placeholder — so the model translates
with grammar-aware instructions instead of a bare string.

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
