# PhotoAtomic.DecimalPrecisionExtensions

Extensions for `System.Decimal` that manipulate the **scale** (number of decimal places)
directly on the binary representation of the value — no string round-trips, significant
zeros preserved.

```csharp
using PhotoAtomic.Numerics;

123.456m.SetPrecision(1);   // 123.4   (truncates)
123.456m.SetPrecision(5);   // 123.45600 (adds significant zeros)
123.456m.SetPrecision(-1);  // 120     (negative precision zeroes integral digits)

123.4567m.RoundWithPrecision(3);  // 123.457 (rounds, then fixes the scale)
123.4000m.GetPrecision();         // 4
```

## API

| Method | Behavior |
|---|---|
| `SetPrecision(int precision)` | Truncates to `precision` decimal places; adds trailing zeros if needed; with negative values zeroes integral digits (`-2` → hundreds). |
| `RoundWithPrecision(int precision, MidpointRounding rounding = AwayFromZero)` | `Math.Round` followed by `SetPrecision`, so the result always shows exactly `precision` decimal places. |
| `GetPrecision()` | The scale of the value: `123.400m` → `3`. |

Useful when the *presentation* of a number carries meaning — reports, invoices, fixed-format
exports — and `123.4m` is not the same thing as `123.400m`.

Part of [PhotoAtomic.Libraries](https://github.com/PhotoAtomic/PhotoAtomic.Libraries)
(originally the standalone [DecimalPrecisionExtensions](https://github.com/PhotoAtomic/DecimalPrecisionExtensions)
repository, whose full history lives on in the monorepo).
