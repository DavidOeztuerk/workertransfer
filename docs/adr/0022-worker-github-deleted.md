# ADR-0022 — `worker-github` gelöscht: kein Konsument, und die falsche Form

Date: 2026-08-03
Status: Angenommen
Related: ADR-0021 (`worker-files` gelöscht, `worker-storage` eingedampft), ADR-0004 (kein Scraping, consent-first), [ULTRAPLAN](../ULTRAPLAN.md) Phase 6

## Kontext

`worker-github` stand seit Phase 1 im Repository: 318 Zeilen, vier deklarierte
Abhängigkeiten, ein übersprungener Smoke-Test.

Es war **unimportierbar**. Der Quelltext machte `from github import Github`
(PyGithub), deklariert war `githubkit`. Damit ist keine einzige Zeile darin je
gelaufen — nicht in einem Test, nicht in einem Dienst, nirgends. Es gab auch
keinen Konsumenten.

Beim Nachsehen, was eine Reparatur kosten würde, fiel das eigentliche Problem
auf. Es ist nicht der Import.

## Was darin steckte

`OSSReputationCalculator` verrechnet einen Menschen zu **einer Zahl zwischen 0
und 100**, aus zehn Dimensionen mit festen Gewichten:

```
technical_expertise 0.25 · architecture 0.15 · open_source 0.15
community 0.10 · leadership 0.10 · documentation 0.05 · testing 0.05
devops 0.05 · ai 0.05 · security 0.05
```

Und `SkillAnalyzer` misst Können in einer Sprache als **Anteil an geschriebenen
Bytes**:

```python
base_score = bytes_count / total_bytes
```

Beides ist nicht bloß ungenau, es ist die falsche Art von Aussage:

- **Bytes sind kein Können.** Eine eingecheckte Abhängigkeit, eine generierte
  Datei, ein umfangreiches Migrationsskript schlagen jede sorgfältige
  Bibliothek. Wer wenig und gut schreibt, verliert.
- **„leadership" und „community" aus Repository-Metadaten** sind Behauptungen
  über einen Menschen, für die es keine Grundlage gibt. Sie stehen da mit einer
  Nachkommastelle, und eine Nachkommastelle sieht aus wie eine Messung.
- **Die Gewichte hat niemand begründet** und niemand gegen irgendetwas
  geprüft. Sie sind eine Meinung im Gewand einer Formel.
- **GitHub misst, wer Zeit für Open Source hat.** Das korreliert mit
  Lebensumständen — Sorgearbeit, Zweitjob, Arbeitsvertrag mit
  Nebentätigkeitsverbot — und nicht mit Fähigkeit. Eine Rangliste daraus
  benachteiligt systematisch.

Die Startseite dieser Plattform verspricht *„nachvollziehbare KI-Unterstützung
statt Black-Box-Entscheidungen"*. Ein Gesamtscore aus zehn ungeprüften
Dimensionen ist eine Black Box mit Komma.

## Entscheidung

**`worker-github` wird gelöscht.**

Nicht repariert. Ein Import-Fix hätte 318 Zeilen scharf gestellt, die genau die
Art von Aussage produzieren, gegen die diese Plattform gebaut ist — und zwar zu
dem Zeitpunkt, an dem sie am billigsten zu übersehen sind: wenn in Phase 6
jemand einen Konsumenten sucht und etwas Fertiges vorfindet.

Dieselbe Begründung wie bei `worker-files` (ADR-0021): ein Paket ohne
Konsumenten, das nie lief, ist kein Vermögenswert. Hier kommt hinzu, dass es ein
Risiko war.

## Was in Phase 6 wiederkommen darf — und was nicht

Der ULTRAPLAN will „Developer Intelligence", und das ist ein legitimes Ziel. Für
den Wiederaufbau gilt:

**Darf wiederkommen:**
- **Belege mit Herkunft.** „Hat an *diesem* Projekt *diese* Commits gemacht" —
  nachprüfbar, mit Link, ohne Zwischenrechnung.
- **Einwilligung zuerst.** Eine Person verbindet ihr Konto selbst; die
  Plattform sieht ohne Freigabe nichts an und liest nichts über jemanden, der
  nicht gefragt wurde (ADR-0004: kein Scraping).
- **Sichtbarkeit über den Ledger**, wie alles andere — eine Capability, die
  jederzeit widerrufbar ist und dann sofort wirkt (ADR-0013).

**Darf nicht wiederkommen:**
- **Ein Gesamtscore.** Keine Zahl, die einen Menschen zusammenfasst, und keine
  Rangfolge daraus.
- **Abgeleitete Eigenschaften ohne Grundlage** — „leadership", „community",
  „architecture" aus Metadaten.
- **Stillschweigende Vollständigkeit.** Wer nichts auf GitHub hat, ist nicht
  schlechter, sondern woanders. Eine Ansicht, die das nicht sagt, lügt durch
  Auslassung.

## Konsequenzen

- Ein übersprungener Test weniger. Übrig bleibt `worker-ai` (Wheels ohne
  Python-3.14-Build).
- Vier deklarierte Abhängigkeiten weniger, darunter `redis`, das im Quelltext
  **kein einziges Mal** vorkam.
- Phase 6 beginnt bei null — und das ist der Punkt: sie beginnt mit dem Entwurf
  der Einwilligung, nicht mit einer vorgefundenen Formel.
- Die Historie bleibt in git. Wer die Rechnung nachlesen will, findet sie dort;
  wer sie benutzen will, muss sie bewusst wiederholen.
