# Piano di estrazione e pacchettizzazione — PhotoAtomic.Libraries

Obiettivo: estrarre le librerie generali vendorizzate in Nyota, PhotoAtomic.Darc e PartyOf2,
trasformarle in pacchetti NuGet individuali, e tenerle organizzate in modo che evolvano assieme
(es. aggiornarle tutte in un colpo solo quando esce una nuova versione di .NET).

Vincolo rispettato: i repo originali (`D:\Nyota`, `D:\PhotoAtomic.Darc`, `D:\PartyOf2`) non sono
stati toccati; tutto il contenuto qui è una copia (vedi [IMPORT.md](IMPORT.md) per la provenienza).

> **Stato decisioni (2026-08-11):** Fase 1 chiusa — monorepo ✅, SourceGen i18n come pacchetto
> separato ✅, versioning lockstep ✅, xUnit come framework unico ✅. La migrazione dei
> consumatori diventa l'ULTIMA fase; prima si raccolgono tutte le librerie riusabili
> (DecimalPrecisionExtensions già importata, poi datafile e MVVM).

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
| **DecimalPrecisionExtensions** | repo GitHub dedicato (importato con storia completa) | Estensioni su `decimal` per gli zeri significativi: `SetPrecision`, `RoundWithPrecision`, `GetPrecision`, lavorando sui bit della rappresentazione (niente string manipulation) | ~130 righe, era .NET 4.0: da modernizzare (v. §6) |

Nyota (il generatore CLI) **non** viene estratto: è un prodotto a sé nel suo repo GitHub; in
futuro potrà consumare `PhotoAtomic.IndentedStrings` da NuGet invece del file interno.

---

## 2. Organizzazione: monorepo con pacchetti indipendenti ✅ deciso

**Un solo repo `PhotoAtomic/PhotoAtomic.Libraries` che produce N pacchetti NuGet.**

Sulla domanda "monorepo semplice o repo con submodule separati": oggi il monorepo semplice è
nettamente la scelta più moderna e manutenibile. I submodule risolvono un problema che qui non
abbiamo (condividere *sorgenti* tra repo che devono restare separati) al costo di attriti noti:
detached HEAD, cloni che arrivano vuoti senza `--recurse-submodules`, il pin dello SHA da
aggiornare a mano a ogni modifica (di fatto lo stesso "drift" del vendoring, solo con più
cerimonia), PR che si spezzano in due repo, CI più complessa. L'ecosistema .NET moderno spinge
nella direzione opposta: un repo, tanti pacchetti, Central Package Management — è il modello dei
repo Microsoft (runtime, aspnetcore) e della maggior parte delle librerie OSS multi-pacchetto.
Il confine di condivisione tra librerie diventa la `PackageReference`, non il submodule.

Perché il monorepo per noi:
- "Tenerle assieme" è esattamente ciò che dà gratis: quando esce .NET 11 si apre **un solo
  branch**, si aggiorna `global.json` + TFM in `Directory.Build.props`, la CI compila e testa
  tutto il grafo in un colpo, e si vede subito cosa si può migliorare.
- L'isolamento verso i consumatori lo danno **i pacchetti**: chi usa Clooney installa solo Clooney.
- Ogni pacchetto ha comunque il suo README (mostrato su nuget.org) e i suoi tag di release.
- Se un giorno una libreria acquisisce vita propria, estrarla con `git filter-repo` preservando la
  storia è sempre possibile: partire uniti e separare poi è molto più facile del contrario.

### Assorbimento di repo esistenti (ricetta collaudata con DecimalPrecisionExtensions)

1. `git subtree add --prefix=staging/import/<Nome> <url-o-clone-locale> <branch>` — importa
   l'intera storia del repo dentro il monorepo (fatto: i commit del 2013, inclusa la migrazione
   TFS, ora fanno parte della storia di questo repo).
2. In fase di raccolta (Fase 4) si crea il progetto moderno in `src/` partendo da quei sorgenti.
3. Il vecchio repo GitHub **non si cancella: si archivia** (Settings → Archive repository), dopo
   aver aggiornato il suo README con un puntatore al monorepo e al pacchetto NuGet. Restano vivi
   link, stelle e storia; il badge "archived" comunica dove continua lo sviluppo.

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
│   ├── PhotoAtomic.DecimalPrecisionExtensions/      # da creare in Fase 4 dai sorgenti importati
│   ├── PhotoAtomic.Internationalization/
│   ├── PhotoAtomic.Internationalization.AI/
│   ├── PhotoAtomic.Internationalization.SourceGen/
│   └── PhotoAtomic.Internationalization.Tool/
├── tests/                           # un progetto di test per libreria (xUnit)
├── samples/
│   └── PhotoAtomic.Internationalization.Demo/
├── staging/
│   ├── candidates/                  # pezzi singoli in valutazione (EquatableArray, ...)
│   └── import/                      # repo assorbiti interi, in attesa di modernizzazione
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
        └── PhotoAtomic.Internationalization.SourceGen        (analyzer package, opzionale)
                    │ (genera codice che usa i tipi del core)
                    ▼
PhotoAtomic.Internationalization     (lib net10.0, zero dep)
        ▲
        ├── PhotoAtomic.Internationalization.AI               (lib, dep: Microsoft.Extensions.AI[.OpenAI])
        └── PhotoAtomic.Internationalization.Tool             (dotnet tool: fill / verify)

PhotoAtomic.DecimalPrecisionExtensions   (lib, indipendente da tutto)
```

| # | Pacchetto | Tipo | Note di packaging |
|---|---|---|---|
| 1 | `PhotoAtomic.IndentedStrings` | libreria | Il fondamento. Pubblicabile per primo e utile da solo a chiunque scriva generator. |
| 2 | `PhotoAtomic.Clooney.Abstractions` | libreria runtime | Attributi + interfacce + contesti; è l'unica dipendenza runtime dei consumatori. |
| 3 | `PhotoAtomic.Clooney` | analyzer | La dll del generatore **e** quella di IndentedStrings vanno in `analyzers/dotnet/cs` (tecnica già risolta in Darc: target `GetTargetPathDependsOn`, da riusare). Dipendenza NuGet su Abstractions. |
| 4 | `PhotoAtomic.Internationalization` | libreria | Solo runtime, zero dipendenze. Funziona anche da sola (traduzione runtime + AI fill). |
| 5 | `PhotoAtomic.Internationalization.SourceGen` | analyzer **separato** ✅ deciso | Opt-in esplicito: chi vuole catalogo compile-time + analyzer PAI18N001 + workflow di pre-traduzione lo installa; chi non lo vuole non si trova un generator che gira a ogni build. Stessa tecnica di packaging di Clooney. Dipendenza NuGet sul core (i tipi generati la richiedono). Nel README del core: "per il catalogo installa anche .SourceGen". |
| 6 | `PhotoAtomic.Internationalization.AI` | libreria | Separata dal core così il core resta a zero dipendenze. |
| 7 | `PhotoAtomic.Internationalization.Tool` | **dotnet tool** | `PackAsTool=true`. Usa `InternalsVisibleTo` verso SourceGen: nel monorepo funziona senza attriti. |
| 8 | `PhotoAtomic.DecimalPrecisionExtensions` | libreria | Da modernizzare prima del pack (v. §6). Nome pacchetto = nome storico del repo; namespace `PhotoAtomic.Numerics` da confermare. |

Separare SourceGen non è un problema tecnico: è il pattern standard "core + analyzer opzionale".
L'unico costo è documentare bene il secondo install. (Peraltro un generator installato ma non
usato non produce nulla: il costo sarebbe solo il suo giro a vuoto in build — che con la
separazione eviti comunque del tutto.)

### Versioning ✅ deciso: lockstep

Una sola versione per tutto il repo (tag git `v0.x.y` → tutti i pacchetti escono con quella
versione, via MinVer). Nessuna matrice di compatibilità da documentare. Se in futuro serve, si
può passare a versioni indipendenti.

### Target framework

- Analyzer/generator e IndentedStrings: `netstandard2.0` (vincolo Roslyn, già così).
- Internationalization core/AI: proporrei multi-target `net8.0;net10.0` (il core è a zero
  dipendenze, quasi certamente compila su net8 → più platea). Da verificare in fase 2.
- DecimalPrecisionExtensions: `netstandard2.0` + eventuale target moderno (v. §6).
- Tool: `net10.0`.

### Test framework ✅ deciso: xUnit ovunque

Per partire **xUnit v2**, che è quello che i test copiati già usano (Darc e PartyOf2): zero
attrito. Il porting di `StringIndentTests` da TUnit e dei test SharpTestsEx di DecimalPrecision è
banale. Passaggio a xUnit v3 valutabile in seguito come modernizzazione unica per tutto il repo
(è il vantaggio del monorepo). TUnit resta interessante ma oggi xUnit è la strada con meno
sorprese su tooling/CI.

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
6. **Porting test**: `StringIndentTests` TUnit → xUnit; test DecimalPrecision SharpTestsEx → xUnit.
7. **Slnx portabile**: lo slnx di Darc conteneva path assoluti `D:/...` — il nuovo va scritto con
   path relativi.
8. Correggere il commento stale in Darc sul luogo degli attributi (nel nuovo csproj).

Difetto noto da decidere consapevolmente (non un bug bloccante): in `AppendLiteral` i contatori di
indentazione vengono **sovrascritti** a ogni segmento letterale anziché accumulati; c'era una riga
commentata (rimossa nella copia PartyOf2) che suggerisce un tentativo abbandonato. I test attuali
passano così: documentare il comportamento o sistemarlo con un test di regressione.

---

## 5. Fasi operative (ordine aggiornato: migrazione consumatori per ULTIMA)

- **Fase 0 — Ricognizione e staging** ✅ (copie verbatim + questo piano)
- **Fase 1 — Decisioni** ✅ (monorepo; SourceGen separato; lockstep; xUnit)
- **Fase 2 — Scaffolding build**: slnx, `Directory.Build.props`/`Packages.props`, csproj per
  IndentedStrings (oggi non ne ha uno), csproj dei progetti di test, porting test a xUnit,
  pulizie del §4, `dotnet build && dotnet test` verdi.
- **Fase 3 — Repo GitHub + CI + pubblicazione**: creazione `PhotoAtomic/PhotoAtomic.Libraries`,
  workflow CI (modello: quello di Nyota), pack in ordine di grafo, push su nuget.org al tag
  (serve la tua API key NuGet come secret `NUGET_API_KEY`).
- **Fase 4 — Raccolta razionale di tutte le librerie riusabili** (prima della migrazione!):
  - **DecimalPrecisionExtensions** — già importata con storia completa in
    `staging/import/DecimalPrecisionExtensions/`; modernizzazione secondo §6, poi archiviazione
    del vecchio repo GitHub con puntatore.
  - **Libreria datafile** (lettura/scrittura file excel-like) — da mostrare e valutare assieme.
  - **Libreria MVVM** — da rimodernare prima dell'inclusione (property `field` di C#, feature
    .NET 10, ecc.): entra quando è pronta, senza fretta.
  - Candidati già individuati nei repo esplorati: `EquatableArray<T>` (Nyota), `ContextResolver`
    (risoluzione di costanti stringa a compile time), `ProjectCatalogReader` (MSBuildWorkspace su
    csproj inclusi alberi Razor), il target MSBuild di flusso analyzer → possibile
    `PhotoAtomic.Generators.Toolkit`; `DifferencePath` usabile anche standalone.
  - Snapshot test dei generatori (Verify/Microsoft.CodeAnalysis.Testing): oggi Clooney e
    SourceGen sono testati solo indirettamente.
- **Fase 5 (ULTIMA) — Migrazione dei consumatori**: Darc, PartyOf2 e Nyota sostituiscono le copie
  vendorizzate con `PackageReference`, un repo alla volta, quando tutto l'ecosistema di pacchetti
  è stabile. Fino ad allora restano com'è oggi: le copie sono identiche, niente drift da
  rincorrere.

---

## 6. Modernizzazione DecimalPrecisionExtensions (note per la Fase 4)

Sorgenti importati (era .NET Framework 4.0, csproj vecchio stile, test SharpTestsEx):

1. **Collisione con la BCL**: `EnumerableExtensions.Chunk<T>(IEnumerable<T>, int)` ha la stessa
   firma di `Enumerable.Chunk` introdotto in .NET 6 → per i consumatori sarebbe una chiamata
   ambigua. Gli helper (`Chunk`, `LeftFill`, `RightFill`) servono solo all'implementazione:
   renderli `internal` e lasciare pubblica solo l'API su `decimal`.
2. **API moderne**: `decimal.Scale` (dove disponibile) può sostituire l'estrazione manuale del
   byte di scala in `GetPrecision`; `Decimal.GetBits(value, Span<int>)` evita allocazioni;
   `PowerOfTen` via `Math.Pow` su `int` è fragile (limiti e conversioni float) → tabella di
   potenze o `BigInteger.Pow`.
3. **TFM**: `netstandard2.0` copre quasi tutto; eventuale multi-target con un TFM moderno per
   usare le API sopra via `#if`.
4. **Packaging**: PackageId `PhotoAtomic.DecimalPrecisionExtensions` (nome storico del repo);
   decidere se mantenere il namespace storico `PhotoAtomic.Numerics` (più naturale se in futuro
   arrivano altre estensioni numeriche) o allinearlo al PackageId.
5. **Test**: portare `NumericsTest.cs` a xUnit; i casi esistenti coprono bene round/truncate.
6. **Repo storico**: archiviare su GitHub dopo la prima pubblicazione NuGet, con README aggiornato.
