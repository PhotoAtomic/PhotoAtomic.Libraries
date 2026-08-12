# PhotoAtomic.Libraries

Monorepo of PhotoAtomic's general-purpose .NET libraries, published as individual NuGet
packages with lockstep versioning (one git tag `v*` → one version for every package).

## Packages

| Package | Kind | What it does |
|---|---|---|
| `PhotoAtomic.IndentedStrings` | library (netstandard2.0) | Interpolated string handler that preserves indentation: `Indent($"...")` for readable code templates and source generators. |
| `PhotoAtomic.Clooney` | Roslyn analyzer | Source generators for deep clone (`[Clonable]`), structural diff (`[Diffable]`) and structural hash (`[Hashable]`). |
| `PhotoAtomic.Clooney.Abstractions` | library (netstandard2.0) | Attributes, interfaces and runtime contexts used by Clooney-generated code, plus the `DifferencePath` model. |
| `PhotoAtomic.Internationalization` | library (net8.0/net10.0) | Structural-key i18n with zero dependencies: `T($"...")`, grammar engine (CLDR plurals, gender, elision), CSV store. |
| `PhotoAtomic.Internationalization.SourceGen` | Roslyn analyzer | Opt-in compile-time catalog generator + `PAI18N001` analyzer for the core i18n library. |
| `PhotoAtomic.Internationalization.AI` | library (net10.0) | Background AI translation filler based on Microsoft.Extensions.AI. |
| `PhotoAtomic.Internationalization.Tool` | dotnet tool (`pai18n`) | Extracts, pre-translates and verifies translation catalogs from a csproj. |
| `PhotoAtomic.DecimalPrecisionExtensions` | library (netstandard2.0/net8.0) | `decimal` extensions for significant zeros: `SetPrecision`, `RoundWithPrecision`, `GetPrecision` on the binary representation. |

More libraries are being folded in over time (see `PLAN.md` for the roadmap; the sources under
`staging/` are imported repositories awaiting modernization, e.g. DecimalPrecisionExtensions).

## Build

```
dotnet build PhotoAtomic.Libraries.slnx
dotnet test  PhotoAtomic.Libraries.slnx
```

Requires the .NET SDK pinned in `global.json`. Versioning is computed by MinVer from git tags.

## License

[MIT](LICENSE)
