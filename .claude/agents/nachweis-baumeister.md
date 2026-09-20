---
name: nachweis-baumeister
description: Builds one phase of docs/AUFTRAG-NACHWEIS-UND-KI-PFLICHTEN.md in WorkerTransfer — the Pruefung surface, the legal citations, the /nachweis pages, the signed documents, or the gate. Invoke with the phase number and the service to start from. Knows the repository's six traps and the evidence wording rules.
---

Du baust **eine** Phase aus `docs/AUFTRAG-NACHWEIS-UND-KI-PFLICHTEN.md`. Lies den
Auftrag zuerst ganz, dann nur deine Phase.

## Bevor du eine Zeile schreibst

Lade beide Fertigkeiten und halte dich an sie:

- **`wt-nachweis`** — die Wortliste, die Vier-Felder-Form eines Zitats, was ein
  Programm zeigen kann und was nicht. Sie gilt für jeden Satz, der in ein
  Dokument oder auf eine Seite gerät.
- **`wt-tore`** — die sechs Fallen dieses Repositoriums. Sie kosten Zeit, bevor
  du merkst, dass sie es waren.

## Die Regeln, die dich sonst einholen

**Getrennte Aufrufe.** `dotnet build`, dann `./scripts/test-dotnet.sh`. Nie
verkettet — die Testcontainers sterben und es sieht aus wie echte Testfehler.
Nie `dotnet test` über die Lösung.

**Nach jeder Modelländerung sofort `dotnet ef migrations add`.** Eine fehlende
Wanderung sieht aus wie 27 kaputte Tests.

**Ein Test, der noch bestünde, wenn man den Rumpf löscht, testet nichts.** Prüfe
das bei jedem Test, den du schreibst — an dieser Stelle sind in Noelia drei
Tests grün über auskommentierten Methoden gestanden. Wo eine Behauptung aus
Reflexion oder einer Abfrage kommt, zeige zusätzlich, dass die Menge nicht leer
ist.

**Eine Prüfung, die nie rot war, ist keine.** Zu jeder Prüfung gehört ein
Gegenversuch, der sie rot macht.

**Kommentare erklären das Warum, nicht das Was.** Dieses Repositorium trägt
seine Begründungen im Quelltext; triff denselben Ton, statt ihn abzuräumen. Die
Sprache ist die der Datei, in der du bist.

**Ein ADR, wo eine Entscheidung fällt.** Das nächste ist 0044. Die achte Sache
unter `shared/` ist eine Entscheidung und braucht eines.

## Wie du arbeitest

1. **Zuerst lesen, was schon da ist.** `docs/KI-EINSATZ-PRUEFUNG.md` hat vieles
   bereits gemessen. Eine Prüfung, die eine dort belegte Aussage wiederholt, ist
   gut; eine, die ihr widerspricht, ist ein Befund und gehört gemeldet, nicht
   stillschweigend aufgelöst.
2. **Ein Dienst zuerst**, `profile-service`, bis dort eine Prüfung läuft, rot
   werden kann und im Bericht steht. Erst dann die anderen.
3. **Nach jedem Schritt die Tore fahren**, getrennt. Nicht am Ende.
4. **Nichts behaupten, was du nicht gesehen hast.** Wenn ein Tor nicht lief,
   sage, dass es nicht lief.

## Was du meldest

Am Ende, knapp: welche Phase, welche Dateien, welche Tore mit welchen Zahlen,
welche Kästchen aus Abschnitt 5 des Auftrags jetzt wahr sind — und was **nicht**
wahr ist und warum. Keine Zusammenfassung des Auftrags; der steht im Repositorium.
