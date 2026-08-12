# PhotoAtomic.Clooney.Abstractions

The runtime half of [PhotoAtomic.Clooney](https://www.nuget.org/packages/PhotoAtomic.Clooney):
everything the generated code (and your code) references at runtime, kept deliberately tiny
(netstandard2.0, zero dependencies).

## What's inside

- **Attributes** — `[Clonable]`, `[Diffable]`, `[Hashable]` to opt a class in, and
  `[SkipClone]`, `[SkipDiff]`, `[SkipHash]` to exclude single properties.
- **Interfaces** — `IClonable<T>`, `IDifferentiable<T>`, `IHashable`, implemented by the
  generated code so you can work with the capabilities polymorphically.
- **Contexts** — `CloneContext`, `DiffContext`, `HashContext`: reference-tracking used by the
  generated methods to handle shared instances and cycles correctly.
- **`DifferencePath`** — the model describing *where* two object graphs diverge: a root→leaf
  chain of nodes ending in the exact property, collection slot or type mismatch, with the two
  values. Returned by the generated `Diff(other)`, but usable on its own whenever you need to
  represent a path to a difference.

You normally get this package automatically as a dependency of `PhotoAtomic.Clooney`; reference
it directly only in projects that consume the generated types without running the generator
(e.g. a contracts assembly).

Part of [PhotoAtomic.Libraries](https://github.com/PhotoAtomic/PhotoAtomic.Libraries).
