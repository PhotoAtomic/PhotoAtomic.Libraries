# PhotoAtomic.Libraries

Monorepo (in costruzione) delle librerie generali PhotoAtomic, estratte dai progetti in cui erano
vendorizzate e destinate a diventare pacchetti NuGet individuali:

- **PhotoAtomic.IndentedStrings** — interpolated string handler che preserva l'indentazione, per
  scrivere source generator leggibili con raw string literal.
- **PhotoAtomic.Clooney** (+ Abstractions) — generatori `[Clonable]` (deep copy), `[Diffable]`
  (differenze strutturali tra grafi di oggetti), `[Hashable]` (hash strutturale).
- **PhotoAtomic.Internationalization** (core, AI, SourceGen, Tool) — internazionalizzazione a
  chiave strutturale con motore fatti/criteri, store CSV e traduzione AI.

Stato: **Fase 0 — staging**. I sorgenti sono copie verbatim dai repo di origine
(vedi [IMPORT.md](IMPORT.md)); il piano completo di pacchettizzazione è in [PLAN.md](PLAN.md).
