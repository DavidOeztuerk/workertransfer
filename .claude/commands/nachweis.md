---
description: Den Auftrag Nachweis und KI-Pflichten bauen — Phase für Phase, mit Toren dazwischen
argument-hint: [Phase 1-5 | "alles"] [optional: Dienst]
---

Baue `docs/AUFTRAG-NACHWEIS-UND-KI-PFLICHTEN.md`.

**Argument:** $ARGUMENTS — eine Phasennummer (1–5), oder `alles` für alle fünf
nacheinander. Ohne Argument: `alles`.

## Zuerst

Lies in dieser Reihenfolge, bevor du etwas änderst:

1. `docs/AUFTRAG-NACHWEIS-UND-KI-PFLICHTEN.md` — ganz
2. Die Fertigkeit **`wt-nachweis`** — sie gilt für jeden Satz, der in ein
   Dokument oder auf eine Seite gerät
3. Die Fertigkeit **`wt-tore`** — die sechs Fallen dieses Repositoriums
4. `docs/KI-EINSATZ-PRUEFUNG.md` — was am 02.09.2026 schon gemessen wurde
5. `docs/adr/0022-*`, `0024-*`, `0026-*`, `0027-*`, `0040-*` — sie regieren

## Dann

Für jede Phase im Auftrag, in dieser Reihenfolge:

1. **Bauen.** Ein Dienst zuerst — `profile-service` —, bis dort eine Prüfung
   läuft, **rot werden kann** und im Bericht steht. Erst dann die anderen.
2. **Tore fahren, getrennt:**
   ```bash
   dotnet build                      # 0 Warnungen
   ./scripts/test-dotnet.sh          # eigener Aufruf, Zahl auf dem Schirm
   ```
3. **Gegenversuch.** Zeige, dass die neue Prüfung rot wird, wenn das eintritt,
   wovor sie warnt. Eine Prüfung, die nie rot war, ist keine.
4. **Erst dann weiter.** Nicht alle fünf Phasen bauen und am Ende testen.

Wo eine Entscheidung fällt: ein ADR. Das nächste ist **0044**.

## Zum Schluss

- Die Kästchen aus Abschnitt 5 des Auftrags durchgehen und **einzeln** sagen,
  welches wahr ist und woran man das sieht
- `docs/SESSIONS.md` fortschreiben, wie es dieses Repositorium hält
- Einen Zweig, einen PR, deutsche Betreffzeile im Ton der letzten zehn — eine
  Aussage, kein Etikett

## Woran du dich nicht vorbeimogelst

- **Kein Wert, kein Schlüssel, kein Name einer Person** in Seite oder Bericht.
  Mit einem Kanarienvogel prüfen, nicht durch Hinsehen.
- **Kein `Leser` leer**, als Test.
- **Die Wörter „zertifiziert", „Zertifikat", „konform"** in keinem Geltungssatz
  und keinem Vorbehalt, als Test.
- **Kein Test, der noch bestünde, wenn man den Rumpf löscht.**
- `docs/KI-EINSATZ-PRUEFUNG.md` wird **nicht umgeschrieben** — sie ist eine
  Momentaufnahme. Oben ein Verweis, unten nichts.

Wenn ein Tor nicht lief, sage, dass es nicht lief. Nicht, dass es grün war.
