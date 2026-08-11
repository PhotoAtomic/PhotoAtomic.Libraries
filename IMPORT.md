# Provenienza delle copie (staging Fase 0)

Tutti i file sono **copie verbatim** dai repo originali, che non sono stati modificati.
Stato dei repo sorgente al momento della copia (2026-08-11):

| Repo | Branch | HEAD | Note |
|---|---|---|---|
| `D:\Nyota` (github.com/PhotoAtomic/Nyota) | main | `771c7cd` | working tree pulito |
| `D:\PhotoAtomic.Darc` (github.com/PhotoAtomic/PhotoAtomic.Darc) | main | `73b25b9` | working tree pulito |
| `D:\PartyOf2` (github.com/PhotoAtomic/PartyOf2) | main | `8777ec7` | modifiche non committate solo in PartyOf2.Core (non copiate qui) |

## Mappa destinazione ← origine

| Destinazione | Origine | Note |
|---|---|---|
| `src/PhotoAtomic.IndentedStrings/` | `D:\PartyOf2\src\PhotoAtomic.IndentedStrings\` | Copia canonica (la più pulita delle tre, funzionalmente identica alle altre). Senza csproj: da creare in Fase 2. |
| `src/PhotoAtomic.IndentedStrings/README.md` | `D:\PhotoAtomic.Darc\src\PhotoAtomic.IndentedStrings\README.md` | Il doc di 418 righe esiste solo in Darc. |
| `src/PhotoAtomic.Clooney/` | `D:\PhotoAtomic.Darc\src\PhotoAtomic.Clooney\` | Il csproj referenzia `..\PhotoAtomic.IndentedStrings` e contiene il target di flusso analyzer da riusare. |
| `src/PhotoAtomic.Clooney.Abstractions/` | `D:\PhotoAtomic.Darc\src\PhotoAtomic.Clooney.Abstractions\` | |
| `src/PhotoAtomic.Internationalization/` | `D:\PartyOf2\src\PhotoAtomic.Internationalization\` | |
| `src/PhotoAtomic.Internationalization.AI/` | `D:\PartyOf2\src\PhotoAtomic.Internationalization.AI\` | |
| `src/PhotoAtomic.Internationalization.SourceGen/` | `D:\PartyOf2\src\PhotoAtomic.Internationalization.SourceGen\` | Il csproj compila IndentedStrings via `<Compile Include>` linkato: da convertire in ProjectReference (Fase 2, punto 5 del piano). |
| `src/PhotoAtomic.Internationalization.Tool/` | `D:\PartyOf2\src\PhotoAtomic.Internationalization.Tool\` | |
| `tests/PhotoAtomic.IndentedStrings.Tests/StringIndentTests.cs` | `D:\Nyota\src\Nyota.Tests\StringIndentTests.cs` | TUnit, usa `Nyota.Helpers` internal: da portare a xUnit + namespace pubblico. Senza csproj. |
| `tests/PhotoAtomic.Clooney.Tests/*.cs` | `D:\PhotoAtomic.Darc\src\PhotoAtomic.Darc.Test\` | Solo i file dei generatori (Diff/Hash/Clone/Polymorphic/Interface + modelli); esclusi i test Orleans/KurrentDB. Senza csproj. |
| `tests/PhotoAtomic.Internationalization.Tests/` | `D:\PartyOf2\tests\PhotoAtomic.Internationalization.Tests\` | |
| `tests/PhotoAtomic.Internationalization.SourceGen.Tests/` | `D:\PartyOf2\tests\PhotoAtomic.Internationalization.SourceGen.Tests\` | |
| `tests/PhotoAtomic.Internationalization.Tool.Tests/` | `D:\PartyOf2\tests\PhotoAtomic.Internationalization.Tool.Tests\` | |
| `samples/PhotoAtomic.Internationalization.Demo/` | `D:\PartyOf2\samples\PhotoAtomic.Internationalization.Demo\` | |
| `eng/pack-local.ps1`, `eng/push-nuget.ps1` | `D:\PhotoAtomic.Darc\` | Path di progetto Darc hardcoded: da parametrizzare. |
| `staging/candidates/EquatableArray.cs` | `D:\Nyota\src\Nyota\Models\EquatableArray.cs` | Candidato futuro (Fase 5). |
| `staging/candidates/TUnitAssertionsExtensions.cs` | `D:\Nyota\src\Nyota.TestExtensions\TUnitAssertionsExtensions.cs` | Candidato futuro (Fase 5). |

Esclusi ovunque: `bin/`, `obj/`, `.vs/`, `*.user`.
