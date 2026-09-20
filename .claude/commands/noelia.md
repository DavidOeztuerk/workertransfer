---
description: Von Girder 4.4.0 auf Noelia 6.4.0 umsteigen — zwölf Sprünge, Demo zuerst, Daten vor Grün
argument-hint: [Phase 0-5 | "alles"]
---

Fahre `docs/AUFTRAG-NOELIA-UMSTIEG.md`.

**Argument:** $ARGUMENTS — eine Phasennummer (0–5) oder `alles`. Ohne Argument:
`alles`, und das heißt: **ab Phase 0**, nicht ab Phase 1.

## Was du zuerst liest

1. `docs/AUFTRAG-NOELIA-UMSTIEG.md` — ganz
2. `~/Projects/Noelia/MIGRATION.md` — **den Abschnitt des Sprungs, den du gerade
   machst.** Nicht alle auf einmal, nicht aus dem Gedächtnis
3. Die Fertigkeit **`wt-nachweis`** — die Wortliste gilt weiter
4. Die Fertigkeit **`wt-tore`** — die sechs Fallen
5. `~/Projects/Noelia/CLAUDE.md` — was Noelia von einem Verbraucher erwartet

## Die Reihenfolge, und sie ist nicht verhandelbar

**Phase 0 zuerst, in `~/Projects/Demo`.** Ein grüner Build beweist nichts. Was
bewiesen werden muss, bevor dieses Repositorium angefasst wird: ein gespeicherter
Widerruf, eine bestehende Sitzung und ein alter Passwort-Hash überleben den
Identitätswechsel.

Dann **Sprung für Sprung**, nicht 4.4.0 → 6.4.0 in einem Satz:

```
4.4.0 → 4.4.1 → 4.4.2 → 4.4.3 → 5.0.0 → 5.1.0 → 5.2.0 → 5.3.0
      → 6.0.0 → 6.1.0 → 6.2.0 → 6.3.0 → 6.4.0
```

Zwischen jedem Sprung, **getrennt**:

```bash
dotnet build                      # 0 Warnungen
./scripts/test-dotnet.sh          # eigener Aufruf, Zahl auf dem Schirm
```

Der Sprung **4.4.3 → 5.0.0** ist der Identitätswechsel und bekommt einen eigenen
Commit, der nichts anderes tut.

## Die drei Dinge, an denen dieser Umstieg scheitern kann

1. **Der Widerruf, der still nicht mitkommt.** Zähle die Widerrufe im alten
   Präfix und im neuen. Weichen sie ab, schalte nicht um. Bei einem Produkt,
   dessen Aktivposten der Einwilligungsledger ist, sieht ein verlorener Widerruf
   danach aus wie eine erteilte Einwilligung.
2. **Der Pfeffer.** Nach `noelia.passwords.primary`, **bevor** ein Dienst das
   erste Mal auflöst. Ohne ihn meldet sich niemand mehr an.
3. **Die Prüfspur.** Alte Kette mit 4.4.3 archivieren *und prüfen*, dann eine
   neue beginnen. Ein Löschnachweis nach ADR-0027, der auf eine verschwundene
   Kette zeigt, ist keiner.

## Nach der Umbenennung

`git status --ignored` lesen, nicht nur `git status`. In PR #87 hat
`.gitignore` zwei Quelldateien aus einem Commit verschluckt, während lokal alles
weiterbaute — `nachweis/` ohne führenden Schrägstrich plus `core.ignorecase`.
Nach 363 berührten Dateien fällt so etwas nicht auf.

## Zum Schluss

- Die Kästchen aus Abschnitt 5 **einzeln** durchgehen und je sagen, woran man
  das sieht
- ADR-0045, und es nennt, was der Umstieg an ADR-0027 ändert
- `docs/SESSIONS.md` fortschreiben
- Zweig, PR, deutsche Betreffzeile im Ton der letzten zehn

Wenn ein Tor nicht lief, sage, dass es nicht lief. Nicht, dass es grün war.
