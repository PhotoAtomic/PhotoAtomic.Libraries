# Piano di estrazione e pacchettizzazione — PhotoAtomic.Libraries

Obiettivo: estrarre le librerie generali vendorizzate in Nyota, PhotoAtomic.Darc e PartyOf2,
trasformarle in pacchetti NuGet individuali, e tenerle organizzate in modo che evolvano assieme
(es. aggiornarle tutte in un colpo solo quando esce una nuova versione di .NET).

Vincolo rispettato: i repo originali (`D:\Nyota`, `D:\PhotoAtomic.Darc`, `D:\PartyOf2`) non sono
stati toccati; tutto il contenuto qui è una copia (vedi [IMPORT.md](IMPORT.md) per la provenienza).

---

## 1. Cosa è emerso dalla ricognizione

### Le tre copie di IndentedStrings sono identiche

Verificato con diff: i corpi dei metodi sono byte-equivalenti in tutte e tre le copie.
Differiscono solo per namespace (`Nyota.Helpers` vs `PhotoAtomic.IndentedStrings`), visibilità
(`internal` in Nyota, `public` altrove) e formattazione. **Nessun merge necessario**: la copia di
PartyOf2 (la più pulita: formattata, senza codice morto, con header di provenienza) è la base
canonica, a cui si aggiunge il README di 418 righe presente solo in Darc.

Catena di vendoring ricostruita: Nyota (origine, mag–lug 2025) → Darc (gen 2026, reso `public`,
progetto separato) → PartyOf2 (ago 2026, ripulito, consumato come sorgente linkato).

### Inventario delle librerie da estrarre

| Libreria | Origine | Cosa fa | Stato |
|---|---|---|---|
| **IndentedStrings** | PartyOf2 (canone) + README da Darc | Handler di stringhe interpolate che preserva l'indentazione (`Indent($$"""...""")`), per scrivere source generator leggibili | ~180 righe + 2 polyfill, zero dipendenze, netstandard2.0 |
| **Clooney** + **Clooney.Abstractions** | Darc | Tre generatori: `[Clonable]` (deep copy), `[Diffable]` (dove divergono due gerarchie, modello `DifferencePath`), `[Hashable]` (hash strutturale). Abstractions = attributi, interfacce `IClonable<T>`/`IDifferentiable<T>`/`IHashable`, contesti runtime per i cicli | Generatore netstandard2.0 + assembly runtime; ~2900 righe di generatori |
| **Internationalization** (core, AI, SourceGen, Tool) | PartyOf2 | i18n a chiave strutturale: `T($"...")` con motore fatti/criteri (plurali CLDR, genere, elisione), store CSV, traduzione AI in background, generatore del catalogo + analyzer PAI18N001, CLI di pre-traduzione/verifica | Core a zero dipendenze net10.0; SourceGen netstandard2.0 |

Nyota (il generatore CLI) **non** viene estratto: è un prodotto a sé nel suo repo GitHub; in
futuro potrà consumare `PhotoAtomic.IndentedStrings` da NuGet invece del file interno.

---

## 2. Organizzazione: monorepo con pacchetti indipendenti (raccomandato)

Il dilemma era: repo GitHub individuali per ogni libreria vs tenerle assieme. Raccomandazione:

**Un solo repo `PhotoAtomic/PhotoAtomic.Libraries` che produce N pacchetti NuGet.**

Perché:
- "Tenerle assieme" è esattamente ciò che un monorepo dà gratis: quando esce .NET 11 si apre
  **un solo branch**, si aggiorna `global.json` + TFM in `Directory.Build.props`, la CI compila e
  testa tutto il grafo in un colpo, e si vede subito cosa si può migliorare.
- L'isolamento verso i consumatori lo danno **i pacchetti**, non i repo: chi usa Clooney installa
  solo Clooney; non gli serve un repo dedicato.
- Ogni pacchetto ha comunque il suo README (mostrato su nuget.org), il suo changelog e i suoi tag
  di release nel monorepo.
- I repo separati avrebbero il costo che conosci già dal vendoring: 3 PR coordinate per ogni
  modifica a IndentedStrings, drift di versioni, ecc.

L'alternativa (un repo per famiglia: IndentedStrings, Clooney, Internationalization) resta
percorribile se un giorno una libreria acquisisce vita/community propria: estrarla dal monorepo
con `git filter-repo` preservando la storia è sempre possibile. Partire uniti e separare poi è
molto più facile del contrario.

### Layout finale proposto

```
PhotoAtomic.Libraries/
├── PhotoAtomic.Libraries.slnx
├── global.json                      # SDK pinnato
├── Directory.Build.props            # metadati comuni: Authors, MIT, RepositoryUrl, SourceLink,
│                                    #   snupkg, deterministic build, LangVersion, Nullable
├── Directory.Packages.props         # Central Package Management (versioni in un punto solo)
├── src/
│   ├── PhotoAtomic.IndentedStrings/
│   ├── PhotoAtomic.Clooney/
│   ├── PhotoAtomic.Clooney.Abstractions/
│   ├── PhotoAtomic.Internationalization/
│   ├── PhotoAtomic.Internationalization.AI/
│   ├── PhotoAtomic.Internationalization.SourceGen/
│   └── PhotoAtomic.Internationalization.Tool/
├── tests/                           # un progetto di test per libreria
├── samples/
│   └── PhotoAtomic.Internationalization.Demo/
├── eng/                             # pack-local.ps1, push-nuget.ps1 (da Darc)
└── .github/workflows/ci.yml         # build + test su push/PR; pack + push su tag
```

---

## 3. Pacchetti NuGet e grafo delle dipendenze

```
PhotoAtomic.IndentedStrings          (lib, netstandard2.0, dep: System.Memory)
        ▲ (solo build/analyzer-load, PrivateAssets=all)
        │
        ├── PhotoAtomic.Clooney      (analyzer package)──dep──▶ PhotoAtomic.Clooney.Abstractions (lib)
        │
        └── PhotoAtomic.Internationalization.SourceGen   (analyzer, impacchettato DENTRO il core)

PhotoAtomic.Internationalization     (lib net10.0, zero dep; include SourceGen in analyzers/)
        ▲
        ├── PhotoAtomic.Internationalization.AI          (lib, dep: Microsoft.Extensions.AI[.OpenAI])
        └── PhotoAtomic.Internationalization.Tool        (dotnet tool: `dotnet i18n fill|verify`)
```

| # | Pacchetto | Tipo | Note di packaging |
|---|---|---|---|
| 1 | `PhotoAtomic.IndentedStrings` | libreria | Il fondamento. Pubblicabile per primo e utile da solo a chiunque scriva generator. |
| 2 | `PhotoAtomic.Clooney.Abstractions` | libreria runtime | Attributi + interfacce + contesti; è l'unica dipendenza runtime dei consumatori. |
| 3 | `PhotoAtomic.Clooney` | analyzer | La dll del generatore **e** quella di IndentedStrings vanno in `analyzers/dotnet/cs` (tecnica già risolta in Darc: target `GetTargetPathDependsOn`, da riusare). Dipendenza NuGet su Abstractions. |
| 4 | `PhotoAtomic.Internationalization` | libreria + analyzer incluso | Consiglio: impacchettare SourceGen dentro il core (`analyzers/dotnet/cs`) così chi installa il pacchetto ha subito catalogo + analyzer, senza un secondo install. SourceGen resta un progetto separato ma non un pacchetto separato. |
| 5 | `PhotoAtomic.Internationalization.AI` | libreria | Separata dal core così il core resta a zero dipendenze. |
| 6 | `PhotoAtomic.Internationalization.Tool` | **dotnet tool** | `PackAsTool=true`, comando tipo `photoatomic-i18n`. Da decidere: consuma SourceGen via `InternalsVisibleTo` — nel monorepo funziona senza attriti. |

### Versioning

**Lockstep**: una sola versione per tutto il repo (tag git `v0.x.y` → tutti i pacchetti escono con
quella versione, es. via MinVer). È la scelta che massimizza il "sempre assieme": nessuna matrice
di compatibilità da documentare. Se in futuro serve, si può passare a versioni indipendenti.

### Target framework

- Analyzer/generator e IndentedStrings: `netstandard2.0` (vincolo Roslyn, già così).
- Internationalization core/AI: proporrei multi-target `net8.0;net10.0` (il core è a zero
  dipendenze, quasi certamente compila su net8 → più platea). Da verificare in fase 2.
- Tool: `net10.0`.

---

## 4. Pulizie da fare in fase di estrazione (sulle copie, mai sugli originali)

1. **Polyfill fuori dal namespace globale**: `StringBuilderExtensions`, `SpanExtensions`,
   `SpanSplitEnumerator<T>` oggi sono `public` in namespace globale — in un pacchetto NuGet
   inquinerebbero ogni consumatore. Renderli `internal` (l'handler li usa solo internamente).
2. **Namespace incoerente**: `SkipCloneAttribute` è in `PhotoAtomic.Clooney.Abstractions` mentre i
   gemelli `SkipDiff`/`SkipHash` sono in `PhotoAtomic.Clooney`. Uniformare (breaking accettabile
   pre-1.0; i consumer vendorizzati non sono impattati).
3. **Metadati NuGet centralizzati** in `Directory.Build.props` (oggi quasi nessun progetto li ha).
4. **Allineare le versioni Roslyn**: Clooney pinna Microsoft.CodeAnalysis 5.3.0, SourceGen 4.11.0,
   Nyota 4.13.0. La versione Roslyn determina l'SDK/VS minimo dei consumatori → scegliere una
   versione unica in `Directory.Packages.props` (tendenzialmente la più bassa che serve).
5. **SourceGen: da sorgente linkato a ProjectReference** + target di flusso analyzer (lo stesso di
   Clooney), così IndentedStrings vive in un posto solo anche a livello di build.
6. **Test framework**: Darc e PartyOf2 usano xUnit v2; i test dell'handler (Nyota) sono in TUnit.
   Proposta: xUnit ovunque (porting banale di `StringIndentTests`), modernizzazioni dopo.
7. **Slnx portabile**: lo slnx di Darc conteneva path assoluti `D:/...` — il nuovo va scritto con
   path relativi.
8. Correggere il commento stale in Darc sul luogo degli attributi (nel nuovo csproj).

Difetto noto da decidere consapevolmente (non un bug bloccante): in `AppendLiteral` i contatori di
indentazione vengono **sovrascritti** a ogni segmento letterale anziché accumulati; c'era una riga
commentata (rimossa nella copia PartyOf2) che suggerisce un tentativo abbandonato. I test attuali
passano così: documentare il comportamento o sistemarlo con un test di regressione.

---

## 5. Fasi operative

- **Fase 0 — Ricognizione e staging** ✅ (questo repo: copie verbatim + questo piano)
- **Fase 1 — Decisioni** (bastano 4 ok/ko):
  1. monorepo `PhotoAtomic.Libraries` come sopra? *(raccomandato: sì)*
  2. SourceGen i18n dentro il pacchetto core o pacchetto analyzer separato? *(raccomandato: dentro)*
  3. versioning lockstep con MinVer? *(raccomandato: sì)*
  4. xUnit come framework unico? *(raccomandato: sì)*
- **Fase 2 — Scaffolding build**: slnx, `Directory.Build.props`/`Packages.props`, csproj per
  IndentedStrings (oggi non ne ha uno), csproj dei 5 progetti di test, porting `StringIndentTests`,
  pulizie del §4, `dotnet build && dotnet test` verdi.
- **Fase 3 — Repo GitHub + CI + pubblicazione**: creazione `PhotoAtomic/PhotoAtomic.Libraries`,
  workflow CI (modello: quello di Nyota), pack in ordine di grafo, push su nuget.org al tag
  (serve la tua API key NuGet come secret `NUGET_API_KEY`).
- **Fase 4 — Migrazione dei consumatori** (quando vuoi, un repo alla volta): Darc, PartyOf2 e
  Nyota sostituiscono la copia vendorizzata con la `PackageReference`. Fino ad allora restano
  com'è oggi, senza fretta: le copie sono identiche, niente drift da rincorrere.
- **Fase 5 — Candidati futuri** (già individuati, parcheggiati in `staging/candidates/` o nei
  sorgenti copiati):
  - `EquatableArray<T>` (Nyota) — wrapper value-equality per pipeline incrementali; utile a tutti
    i generator → possibile `PhotoAtomic.Generators.Toolkit` assieme a `ContextResolver`
    (SourceGen i18n: risoluzione di costanti stringa a compile time) e al target MSBuild di
    flusso analyzer.
  - `ProjectCatalogReader` (Tool i18n) — ricetta MSBuildWorkspace per leggere l'output dei
    generator da un csproj, inclusi gli alberi Razor.
  - `DifferencePath` — il modello "dove divergono due grafi" è usabile anche senza generatore.
  - Snapshot test dei generatori (Verify/Microsoft.CodeAnalysis.Testing): oggi Clooney e SourceGen
    sono testati solo indirettamente.
