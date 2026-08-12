# PhotoAtomic.Clooney

Roslyn source generators for **deep clone**, **structural diff** and **structural hash** of your
object graphs — cycle-safe, with zero reflection at runtime: everything is generated at compile
time.

```csharp
using PhotoAtomic.Clooney;

[Clonable]
[Diffable]
[Hashable]
public class BankAccountState
{
    public decimal Balance { get; set; }
    public int TransactionCount { get; set; }

    [SkipClone]
    public List<Event> PendingEvents { get; set; } = new();
}
```

```csharp
var clone = state.Clone();              // deep copy, [SkipClone] members excluded
var diffs = state.Diff(otherState);     // IEnumerable<DifferencePath>: where two graphs diverge
var hash  = state.HashValue();          // structural hash of the whole graph
```

## The three generators

| Attribute | Generates | Excludes with |
|---|---|---|
| `[Clonable]` | `Clone()` and `Clone(CloneContext)` extension methods — deep copy; the context tracks references so shared and cyclic instances clone once. | `[SkipClone]` |
| `[Diffable]` | `Diff(other)` returning `DifferencePath` items, each a root→leaf chain of nodes pointing at the exact property (or collection slot) that diverges, with current/other values. | `[SkipDiff]` |
| `[Hashable]` | `HashValue()` — structural hash over the graph, cycle-safe via `HashContext`. | `[SkipHash]` |

Inheritance, interfaces and polymorphic hierarchies are supported: derived types get their own
generated methods, and interface-typed properties dispatch to the runtime type.

## Installing

```
dotnet add package PhotoAtomic.Clooney
```

The package is an analyzer: it runs inside the compiler and adds **no runtime dependency**
except [`PhotoAtomic.Clooney.Abstractions`](https://www.nuget.org/packages/PhotoAtomic.Clooney.Abstractions)
(brought in automatically), which contains the attributes, the interfaces and the
clone/diff/hash contexts your compiled code uses.

Part of [PhotoAtomic.Libraries](https://github.com/PhotoAtomic/PhotoAtomic.Libraries).
