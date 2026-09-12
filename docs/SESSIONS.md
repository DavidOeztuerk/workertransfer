# Die Reihenfolge, und was in welche Sitzung gehört

Jeder Block unten ist **eine Sitzung**. Kopiere den Text im Kasten hinein,
sonst nichts — jeder Prompt ist selbsttragend und nennt seine eigenen Fallen.

**Warum getrennt:** eine Sitzung, die erst den ganzen Baum liest, hat den halben
Kontext verbraucht, bevor sie etwas tut. Jeder Prompt sagt darum genau, welche
Dateien zu lesen sind.

**Die Reihenfolge ist nicht beliebig.** 1 ist die Grundlage für 3 und 4; 2 ist
unabhängig und kann jederzeit dazwischen; 5 verlangt 3 und 4.

**Erst PR #66 zusammenführen, dann anfangen.** Die .NET-Migration liegt auf
`dotnet-migration` und ist seit dem 09.09.2026 grün durch alle fünf Jobs. Jede
Sitzung unten ist eine *eigene* Arbeit und gehört auf einen *eigenen* Zweig von
`develop` aus — sonst wächst #66 unbegrenzt weiter und niemand kann ihn mehr
prüfen. Der Weg ist der aus CLAUDE.md und hat keine Abkürzung:
`feature → develop → main`, nie ein Feature-Zweig direkt nach main.

**Du musst dafür nichts tun.** Jeder Prompt unten legt seinen Zweig selbst an
und führt am Ende selbst zusammen: committen, pushen, PR nach `develop`, auf
die fünf Jobs warten, **erst dann** mergen — und danach noch einmal auf den
Lauf sehen, den der Merge auf `develop` auslöst. Du kopierst einen Kasten und
bekommst am Ende einen gemergten `develop` zurück.

Die eine Bedingung, die dabei nicht verhandelbar ist: **ein roter Lauf wird
nicht gemergt.** Kein `--admin`, kein „ist nur die eine Reise". Die Pipeline
ist seit dem 09.09.2026 grün, und sie ist der Maßstab, an dem sich die nächste
Sitzung misst — wer sie rot hinterlässt, nimmt ihn der nächsten weg.

| # | Sitzung | Hängt ab von | Umfang |
|---|---|---|---|
| 1 | Berufsfelder und Belege | — | groß |
| 2 | Rollen erzwingen | — | mittel |
| 3 | `scout-service` | 1 | groß |
| 4 | `advisor-service` | 1 | groß |
| 5 | ~~`assessment-service`~~ ✅ erledigt 11.09.2026 (ADR-0042) | 3, 4 | mittel |
| 6 | Aufräumen | — | klein |
| 7 | ~~Girder auf nuget.org~~ ✅ erledigt 10.09.2026 | — | — |

---

## Für **jede** Sitzung gilt

Diese sechs Fallen haben in echten Sitzungen Zeit gekostet. Sie stehen in jedem
Prompt unten noch einmal, damit du nur einen Kasten kopieren musst.

1. `dotnet build` und `dotnet test` **nie im selben Aufruf** — verkettet
   sterben die Testcontainers und sehen dabei aus wie echte Testfehler.
2. **Nie `dotnet test` über die Lösung.** Nur `./scripts/test-dotnet.sh`.
3. Nach jeder Modelländerung **sofort** `dotnet ef migrations add` — sonst
   fallen *alle* Tests der Reihe in 18 ms mit `PendingModelChangesWarning`.
4. Ein `SichereAsync`, das eine vorhandene Zeile ändert, braucht
   **`.AsTracking()`** — alle Kontexte fahren `NoTracking`, sonst schreibt es
   lautlos nichts.
5. **Warnungen sind Fehler.** Auch `else if (false)` ist ein Build-Fehler, also
   bei Gegenproben einen Bruch wählen, der kompiliert.
6. **E2E einmal am Ende.** Und der Selektor muss eindeutig sein: ein
   `getByRole("button", { name: /Speichern/i })` traf ab dem zweiten
   Speichern-Knopf zwei Elemente und legte vierzehn Reisen still lahm.
8. **Ein neuer Aufruf nach draußen wird ABGEWIESEN, wenn er nicht in der
   Konfiguration steht.** Jeder Dienst fährt Girders Egress-Grenze
   (`AddSovereignPlatform`), und sie weist ab statt zu protokollieren. Die
   erlaubten Hosts leitet `Dienstgrundlage.GerufeneHosts` aus der
   **Konfiguration** ab — jeder Wert, der eine absolute http-Adresse ist, gibt
   seinen Host her. Wer in Sitzung 3, 4 oder 5 einen Dienst baut, der irgendwo
   hinruft, trägt das Ziel also in `docker-compose.yml` ein, sonst läuft es
   lokal nie.

   **Und was in der DATENBANK steht, sieht sie nicht.** Genau daran starb am
   10.09.2026 der Anschreiben-Agent: der KI-Zugang einer Person ist eine Zeile,
   keine Konfiguration. Der Auftrag lief an, schrieb nie, und **in der
   Oberfläche passierte nichts** — der Fehler starb in einem
   Hintergrundarbeiter, wo niemand hinsieht. Ein vom Menschen wählbares Ziel
   braucht deshalb eine Erklärung des Betreibers (`Draft__ErlaubteZiele__*`).

9. **Dein `.env` ist nicht das `.env` der CI.** `make env` baut aus
   `.env.example`, und dort sind Zugangsdaten **leer** — leer heißt „nicht
   eingerichtet", und die Oberfläche zeigt dann bewusst etwas anderes. Eine
   Prüfung, die gegen deinen eingerichteten Stapel geschrieben ist, ist lokal
   grün und in der CI rot, **ohne dass eine der beiden Seiten unrecht hat**.
   Gemessen am 09.09.2026: der Knopf „Mit GitHub anmelden" erscheint nur mit
   gesetztem `GitHub__OAuth__*`, und `POST /github/me/oauth/finish` antwortet
   ohne ihn 404 statt 422. Wer eine Reise oder eine Kartenzeile schreibt, fragt
   sich vorher: hängt das an etwas, das nur bei mir gesetzt ist?

---

## Sitzung 1 — Berufsfelder und Belege

```
ZUERST, ohne zu fragen: git switch develop && git pull && git switch -c feature/berufsfelder-und-belege
Diese Sitzung arbeitet NIE direkt auf develop.

Du arbeitest im Repository WorkerTransfer (.NET 10 auf Girder 4.4.0, React).
Lies: CLAUDE.md, docs/PLAN-TRANSFERMARKT.md (PBI-1), docs/adr/0033-beleg-und-sichtbarkeit.md,
docs/adr/0023-skill-vocabulary-renames-never-infers.md.

Setze PBI-1 um: WorkerTransfer ist heute faktisch eine Plattform fuer
Softwareentwickler, und das war nie entschieden. Ein Metallbauer hat kein
GitHub — er hat Zeugnisse, Nachweise und Arbeitsproben, und das meiste davon
ist schon gebaut (Unterlage in resume-service, portfolio-service), es wird nur
nicht angeboten.

1. SCHREIBE ZUERST ADR-0039 "Belege sind berufsabhaengig". Erst danach Code.
   Er haelt fest: was ein Berufsfeld ist, welche Belegarten je Feld zaehlen,
   und dass die drei Herkunftsklassen aus ADR-0033 (genannt/belegt/
   vorgeschlagen) unveraendert gelten — es kommen nur Quellen dazu. GitHub wird
   dadurch NICHT abgewertet, es ist eine Quelle unter mehreren.

2. `berufsfeld` auf `users`, nullable, aus einer GESCHLOSSENEN Liste. Kein
   Freitext: ein Feld, aus dem eine Navigation folgt, muss endlich sein.
   Startpunkt: handwerk, industrie_technik, bau, gesundheit_pflege,
   logistik_verkehr, gastronomie_hotel, handel_verkauf, buero_verwaltung,
   it_software, bildung_soziales, sonstiges.

3. Registrierung: Auswahlfeld, KEIN Pflichtfeld. Wer nichts waehlt, bekommt die
   heutige neutrale Ansicht — nichts wird schlechter.

4. Navigation folgt dem Feld: /github erscheint nur bei it_software. Die Route
   bleibt erreichbar — Verstecken ist keine Zugriffskontrolle, das steht so
   schon in CLAUDE.md.

5. Der Wortschatz (src/shared/WorkerTransfer.Skills) bekommt Handwerksbegriffe
   (MIG/MAG, WIG, CNC, SPS, Staplerschein, Geruestbau, ...). Die Regel aus
   ADR-0023 gilt unveraendert: benennt um, folgert NIE. "MIG/MAG" impliziert
   nicht "Schweissen".

6. Alle Texte in drei Katalogen (de/en/fr), Deutsch ist die Quelle.

ABNAHME: ein Konto mit handwerk sieht kein GitHub und stattdessen Nachweise;
ein Konto ohne Berufsfeld sieht die heutige Ansicht; ein Skills-Test zeigt
Kanonisierung ohne Folgerung.

FALLEN: build und test nie zusammen; nie dotnet test ueber die Loesung, nur
./scripts/test-dotnet.sh; nach jeder Modelaenderung sofort dotnet ef migrations
add; ein aenderndes SichereAsync braucht .AsTracking(); Warnungen sind Fehler; ein neuer Aufruf nach draussen muss in der
Konfiguration stehen, sonst weist die Egress-Grenze ihn ab — und was in der
DATENBANK steht, sieht sie nicht;
dein .env ist NICHT das der CI — leere Zugangsdaten in .env.example heissen
"nicht eingerichtet", und eine Pruefung dagegen ist lokal gruen und in der CI
rot; E2E einmal am Ende, mit eindeutigen Selektoren.

ZUM SCHLUSS — SELBER ERLEDIGEN, nicht zurueckfragen:
1. Lokal gruen machen, in GETRENNTEN Aufrufen:
     dotnet build WorkerTransfer.slnx
     ./scripts/test-dotnet.sh
     cd web && pnpm check && pnpm test && pnpm build && cd ..
2. Committen. Die Nachricht nennt den GRUND, nicht die geaenderte Datei.
3. git push -u origin feature/berufsfelder-und-belege
4. gh pr create --base develop --fill
5. Warten, bis ALLE FUENF Jobs gruen sind:  gh pr checks --watch
6. ERST DANN:  gh pr merge --merge --delete-branch
7. UND DANN NOCH EINMAL HINSEHEN — der PR prueft den Merge-VORSCHLAG, nicht
   das Ergebnis. Am 09.09.2026 war #67 gruen und develop danach rot:
     gh run watch $(gh run list --branch develop --limit 1 --json databaseId --jq '.[0].databaseId')
   Ist der rot, ist es deine Aufgabe, nicht die der naechsten Sitzung.

Rot heisst reparieren und wiederholen. NIE einen roten Lauf mergen, nie
--admin. Wer die Pipeline rot hinterlaesst, nimmt der naechsten Sitzung ihren
Massstab.
```

---

## Sitzung 2 — Rollen erzwingen

```
ZUERST, ohne zu fragen: git switch develop && git pull && git switch -c feature/rollen-erzwingen
Diese Sitzung arbeitet NIE direkt auf develop.

Du arbeitest im Repository WorkerTransfer (.NET 10 auf Girder 4.4.0).
Lies: CLAUDE.md (Abschnitte "Request context" und "Die Girder-Module"),
docs/PLAN-TRANSFERMARKT.md (PBI-2), docs/routenkarte.yml.

Setze PBI-2 um. Heute gibt es NULL `RequirePermission` im ganzen Baum: die
Navigation versteckt Firmeneintraege, und der Server antwortet 403 nur dort, wo
jemand daran gedacht hat. `Mitgliedschaftsrecht` liest die Rolle je Anfrage aus
der Mitgliedschaftstabelle — aber niemand fragt. Das ist keine
Zugriffskontrolle, und es ist die groesste echte Luecke im Baum.

1. Geh docs/routenkarte.yml durch — sie ist bereits vollstaendig — und
   entscheide je Firmen-Route: admin oder member?
2. `[RequirePermission(...)]` an die Endpunkte, die es brauchen. Der
   Richtlinienanbieter (GirderModule.Authorization) laeuft bereits; die Rechte
   stehen NICHT im Token (eine Entfernung wirkte sonst erst beim Ablauf).
3. docs/routenkarte.yml bekommt eine VIERTE Spalte `mitglied`. Heute
   unterscheidet die Karte drei Handlungsformen (ohne Token, Person, Firma);
   die vierte macht diese Arbeit ueberhaupt erst pruefbar.
4. scripts/routenkarte.sh muss die vierte Spalte fahren.
5. Ein Test je geschuetzter Route: member 403, admin 200.

ABNAHME: `make routenkarte` faehrt vier Spalten, alle wie aufgeschrieben. Eine
Gegenprobe: RequirePermission an einer Route entfernen -> der Test faellt (und
danach mit --no-incremental neu bauen).

FALLEN: build und test nie zusammen; nie dotnet test ueber die Loesung;
Warnungen sind Fehler; ein neuer Aufruf nach draussen muss in der
Konfiguration stehen, sonst weist die Egress-Grenze ihn ab — und was in der
DATENBANK steht, sieht sie nicht; dein .env ist NICHT das der CI — leere Zugangsdaten in
.env.example heissen "nicht eingerichtet"; eine Gegenprobe muss KOMPILIEREN,
sonst liest sich der Build-Fehler wie ein bestandener Test.

ZUM SCHLUSS — SELBER ERLEDIGEN, nicht zurueckfragen:
1. Lokal gruen machen, in GETRENNTEN Aufrufen:
     dotnet build WorkerTransfer.slnx
     ./scripts/test-dotnet.sh
     cd web && pnpm check && pnpm test && pnpm build && cd ..
2. Committen. Die Nachricht nennt den GRUND, nicht die geaenderte Datei.
3. git push -u origin feature/rollen-erzwingen
4. gh pr create --base develop --fill
5. Warten, bis ALLE FUENF Jobs gruen sind:  gh pr checks --watch
6. ERST DANN:  gh pr merge --merge --delete-branch
7. UND DANN NOCH EINMAL HINSEHEN — der PR prueft den Merge-VORSCHLAG, nicht
   das Ergebnis. Am 09.09.2026 war #67 gruen und develop danach rot:
     gh run watch $(gh run list --branch develop --limit 1 --json databaseId --jq '.[0].databaseId')
   Ist der rot, ist es deine Aufgabe, nicht die der naechsten Sitzung.

Rot heisst reparieren und wiederholen. NIE einen roten Lauf mergen, nie
--admin. Wer die Pipeline rot hinterlaesst, nimmt der naechsten Sitzung ihren
Massstab.
```

---

## Sitzung 3 — `scout-service`

```
BEACHTE: dieser Dienst ruft nach draussen. Trage JEDES Ziel in
docker-compose.yml ein — die Egress-Grenze liest ihre erlaubten Hosts aus
der Konfiguration und weist alles andere ab, ohne es zu protokollieren.
ZUERST, ohne zu fragen: git switch develop && git pull && git switch -c feature/scout-service
Diese Sitzung arbeitet NIE direkt auf develop.

Du arbeitest im Repository WorkerTransfer (.NET 10 auf Girder 4.4.0).
Lies: docs/adr/0036-scout-service.md (angenommen, Code fehlt),
docs/adr/0033-beleg-und-sichtbarkeit.md, docs/SCOUT-UND-BERATER-BESTAND.md,
CLAUDE.md (Abschnitt "Was ADR-0022 wirklich verbietet"),
docs/PLAN-TRANSFERMARKT.md (PBI-3).

Baue scout-service nach ADR-0036. Lies die BESTANDSAUFNAHME zuerst: sie hat
Teile des Entwurfs gemessen und widerlegt.

Aufbau wie die elf anderen Dienste: Api/Application/Domain/Infrastructure/
Contracts, eigene Datenbank, AddWorkerTransferDefaults, eigene Route in
src/gateway/.../ocelot.json, Eintrag in docs/routenkarte.yml, Loeschempfaenger
ab der ersten Tabelle (LoeschempfaengerTests geht sonst zu Recht rot).

DIE VIER AUFLAGEN, jede als Test:
1. KEINE Sortierung nach Passung. Reihenfolge stabil und sachfremd.
2. KEINE Zahl ueber einen Menschen. Kein score/rank/weight/fit/percent —
   weder als Feld noch als Endpunkt.
3. NUR GENANNTES ist durchsuchbar. Belege werden zum Treffer dazugeholt, nie
   zum Finden benutzt.
4. Die Ansprache ist ein ENTWURF. Der Dienst schreibt niemandem.

Gespeichert wird die ANFRAGE, nie das Ergebnis: ein gespeichertes Ergebnis
ueber Menschen veraltet gegen einen Widerruf.

/candidates in profile-service wird abgeloest. Die harten Teile sind fertig und
werden MITGENOMMEN, nicht neu erfunden: Ledger je Zeile ueber /check-batch
(ADR-0030), KEINE Gesamtzahl (ADR-0026 — sie verriete ueber die Differenz, wie
viele Profile nicht freigegeben sind), Firmenzwang.

Dazu die Nachricht aus ADR-0033: "dein Profil wurde entdeckt" — eigene
Benachrichtigungsart, nennt KEIN Unternehmen, ueber den Postausgang
(inhaltsfrei, ADR-0025), hoechstens EINE je Person und Tag.

FALLEN: build und test nie zusammen; nie dotnet test ueber die Loesung; nach
jeder Modelaenderung sofort dotnet ef migrations add; ein aenderndes
SichereAsync braucht .AsTracking(); Dienst-zu-Dienst-Ruempfe sind TYPISIERTE
Vertraege, nie anonyme Objekte; Warnungen sind Fehler; ein neuer Aufruf nach draussen muss in der
Konfiguration stehen, sonst weist die Egress-Grenze ihn ab — und was in der
DATENBANK steht, sieht sie nicht; dein .env ist NICHT das
der CI — leere Zugangsdaten in .env.example heissen "nicht eingerichtet";
E2E einmal am Ende.

ZUM SCHLUSS — SELBER ERLEDIGEN, nicht zurueckfragen:
1. Lokal gruen machen, in GETRENNTEN Aufrufen:
     dotnet build WorkerTransfer.slnx
     ./scripts/test-dotnet.sh
     cd web && pnpm check && pnpm test && pnpm build && cd ..
2. Committen. Die Nachricht nennt den GRUND, nicht die geaenderte Datei.
3. git push -u origin feature/scout-service
4. gh pr create --base develop --fill
5. Warten, bis ALLE FUENF Jobs gruen sind:  gh pr checks --watch
6. ERST DANN:  gh pr merge --merge --delete-branch
7. UND DANN NOCH EINMAL HINSEHEN — der PR prueft den Merge-VORSCHLAG, nicht
   das Ergebnis. Am 09.09.2026 war #67 gruen und develop danach rot:
     gh run watch $(gh run list --branch develop --limit 1 --json databaseId --jq '.[0].databaseId')
   Ist der rot, ist es deine Aufgabe, nicht die der naechsten Sitzung.

Rot heisst reparieren und wiederholen. NIE einen roten Lauf mergen, nie
--admin. Wer die Pipeline rot hinterlaesst, nimmt der naechsten Sitzung ihren
Massstab.
```

---

## Sitzung 4 — `advisor-service`

```
BEACHTE: dieser Dienst ruft nach draussen. Trage JEDES Ziel in
docker-compose.yml ein — die Egress-Grenze liest ihre erlaubten Hosts aus
der Konfiguration und weist alles andere ab, ohne es zu protokollieren.
ZUERST, ohne zu fragen: git switch develop && git pull && git switch -c feature/advisor-service
Diese Sitzung arbeitet NIE direkt auf develop.

Du arbeitest im Repository WorkerTransfer (.NET 10 auf Girder 4.4.0).
Lies: docs/adr/0037-advisor-service.md (angenommen, Code fehlt),
docs/adr/0020-visibility-lives-in-the-ledger.md,
docs/SCOUT-UND-BERATER-BESTAND.md, docs/PLAN-TRANSFERMARKT.md (PBI-4).

Baue advisor-service nach ADR-0037.

DIE WICHTIGSTE REGEL: das Mandat ist eine SICHT, kein zweiter Speicher.
Sichtbarkeit lebt im Ledger (consent-service), Verfuegbarkeit im Marktstatus
(transfer-service). Eine eigene Mandatstabelle fuer Sichtbarkeit verstoesst
gegen ADR-0020 — und ein Widerruf muesste dann an zwei Stellen wirken.
Eigen sind nur vier Werte: Eintrittstermin, Gehaltsspanne, Pensum,
ausgeschlossene Unternehmen.

Gespraeche in drei Stufen, jede Stufe eine Freigabe der Person:
  Stufe 1  HR sieht Profil, genannte Faehigkeiten, Verfuegbarkeit
  Stufe 2  + Belege, Lebenslauf, Gehaltsspanne
  Stufe 3  + Klarname, Kontakt, ggf. jetziger Arbeitgeber

Was in einer Stufe nicht freigegeben ist, EXISTIERT fuer die Gegenseite nicht:
kein "gesperrt"-Hinweis, der verraet, dass es etwas gibt.

Einigung -> Uebergabe an transfer-service; der Dreieckskonsens steht dort
bereits und wird nicht nachgebaut.

ABNAHME: der jetzige Arbeitgeber sieht die eigene Belegschaft NICHT im Scout;
eine Stufenfreigabe wirkt sofort, ein Widerruf ebenso.

FALLEN: wie in Sitzung 3.

ZUM SCHLUSS — SELBER ERLEDIGEN, nicht zurueckfragen:
1. Lokal gruen machen, in GETRENNTEN Aufrufen:
     dotnet build WorkerTransfer.slnx
     ./scripts/test-dotnet.sh
     cd web && pnpm check && pnpm test && pnpm build && cd ..
2. Committen. Die Nachricht nennt den GRUND, nicht die geaenderte Datei.
3. git push -u origin feature/advisor-service
4. gh pr create --base develop --fill
5. Warten, bis ALLE FUENF Jobs gruen sind:  gh pr checks --watch
6. ERST DANN:  gh pr merge --merge --delete-branch
7. UND DANN NOCH EINMAL HINSEHEN — der PR prueft den Merge-VORSCHLAG, nicht
   das Ergebnis. Am 09.09.2026 war #67 gruen und develop danach rot:
     gh run watch $(gh run list --branch develop --limit 1 --json databaseId --jq '.[0].databaseId')
   Ist der rot, ist es deine Aufgabe, nicht die der naechsten Sitzung.

Rot heisst reparieren und wiederholen. NIE einen roten Lauf mergen, nie
--admin. Wer die Pipeline rot hinterlaesst, nimmt der naechsten Sitzung ihren
Massstab.
```

---

## Sitzung 5 — `assessment-service` ✅ erledigt 11.09.2026

Gebaut unter **ADR-0042**, nicht 0040: der Kasten unten nennt die Nummer, die
beim Schreiben des Plans frei war, und 0040/0041 waren es beim Bauen nicht
mehr. Wer den Kasten noch einmal kopiert, bekommt einen fertigen Dienst.

```
BEACHTE: dieser Dienst ruft nach draussen. Trage JEDES Ziel in
docker-compose.yml ein — die Egress-Grenze liest ihre erlaubten Hosts aus
der Konfiguration und weist alles andere ab, ohne es zu protokollieren.
ZUERST, ohne zu fragen: git switch develop && git pull && git switch -c feature/assessment-service
Diese Sitzung arbeitet NIE direkt auf develop.

Du arbeitest im Repository WorkerTransfer (.NET 10 auf Girder 4.4.0).
Lies: docs/SCOUT-UND-BERATER.md (Abschnitt assessment-service),
docs/adr/0022-*.md, docs/PLAN-TRANSFERMARKT.md (PBI-5).

SCHREIBE ZUERST ADR-0040. Erst danach Code. Dies ist der Dienst mit dem
groessten Missbrauchspotenzial: unbezahlte Arbeit, als Aufgabe getarnt.

Die drei Regeln, die eine Aufgabe von einer Pruefung mit Note unterscheiden:
1. Die Bewertung gehoert dem VORGANG, nicht dem Menschen: in keiner Suche, in
   keinem Profil, fuer kein anderes Unternehmen, und ohne Zahl — nur Text.
2. Die Person SIEHT die Bewertung. Immer, auch bei Absage. Eine Beurteilung,
   die der Beurteilte nie liest, ist genau das, was hier nicht gebaut wird.
3. Der UMFANG IN STUNDEN steht in der Ausschreibung, und Ablehnen wird nirgends
   vermerkt.

Danach der Dienst, Aufbau wie die anderen.

FALLEN: wie in Sitzung 3.

ZUM SCHLUSS — SELBER ERLEDIGEN, nicht zurueckfragen:
1. Lokal gruen machen, in GETRENNTEN Aufrufen:
     dotnet build WorkerTransfer.slnx
     ./scripts/test-dotnet.sh
     cd web && pnpm check && pnpm test && pnpm build && cd ..
2. Committen. Die Nachricht nennt den GRUND, nicht die geaenderte Datei.
3. git push -u origin feature/assessment-service
4. gh pr create --base develop --fill
5. Warten, bis ALLE FUENF Jobs gruen sind:  gh pr checks --watch
6. ERST DANN:  gh pr merge --merge --delete-branch
7. UND DANN NOCH EINMAL HINSEHEN — der PR prueft den Merge-VORSCHLAG, nicht
   das Ergebnis. Am 09.09.2026 war #67 gruen und develop danach rot:
     gh run watch $(gh run list --branch develop --limit 1 --json databaseId --jq '.[0].databaseId')
   Ist der rot, ist es deine Aufgabe, nicht die der naechsten Sitzung.

Rot heisst reparieren und wiederholen. NIE einen roten Lauf mergen, nie
--admin. Wer die Pipeline rot hinterlaesst, nimmt der naechsten Sitzung ihren
Massstab.
```

---

## Sitzung 6 — Aufräumen ✅ erledigt 11.09.2026

**Der Kasten unten war selbst der Fund.** Er nennt vier Arbeiten; **zwei waren
längst erledigt** (`GET /notifications` antwortet seit H4 auf jede Methode 404,
und Punkt 4 ist keine Codeaufgabe, sondern eine Entscheidung), und **der
größte Posten fehlte ganz**: acht Auftragsdokumente mit **59 offenen
Kästchen**, von denen **58 gebaut waren**. Ein offenes Kästchen liest sich wie
ein Auftrag — die nächste Sitzung hätte angefangen, Dinge zum zweiten Mal zu
bauen.

Was wirklich getan wurde, steht in [PBI-6](PLAN-TRANSFERMARKT.md). Kurz:

- Alle acht Auftragsdokumente tragen eine Statuszeile, jedes Kästchen ist
  gegen den Baum geprüft. **Nichts gelöscht** — die Begründungen sind der Wert.
- **Fund 7** ist weg: `JobApplyPage` (300 Zeilen), die Route
  `/jobs/:jobId/apply` und siebzehn verwaiste Katalogschlüssel in drei
  Sprachen. Das Anlegen eines Entwurfs steht jetzt an *einer* Stelle.
- **`make k8s-up` ist zum ersten Mal gefahren.** Das Chart trug auf Anhieb
  (19 Pods, null Neustarts); die **Beweise** logen an vier Stellen und stehen
  jetzt auf dem Gemessenen.
- Die offene Arbeit, die dabei herausfiel, steht als **PBI-8** im Plan.

Der Kasten bleibt als Geschichte stehen:

```
ZUERST, ohne zu fragen: git switch develop && git pull && git switch -c feature/aufraeumen
Diese Sitzung arbeitet NIE direkt auf develop.

Du arbeitest im Repository WorkerTransfer.
Lies: docs/REVIEW-09-09.md (Fund 7), docs/PLAN-TRANSFERMARKT.md (PBI-6).

Vier kleine, unabhaengige Arbeiten:

1. FUND 7: In web/src/features/work/pages/JobApplyPage.tsx ist das alte
   Bewerbungsformular (~120 Zeilen samt Freigabeschaltern und Absendeknopf)
   UNERREICHBAR, seit die Seite fuer Angemeldete eine Weiche auf den Entwurf
   ist und Abgemeldete die "Konto noetig"-Karte bekommen. Loeschen, und im
   Kommentar der Seite festhalten, dass /jobs/{id}/apply heute eine Weiche ist.
   Danach pnpm check, pnpm test, und die E2E-Reisen application-journey und
   jobs-journey einmal fahren.

2. `make k8s-up` EINMAL WIRKLICH FAHREN. Es ist nie gelaufen; das Diagramm
   lintet und rendert nur. Was scheitert, wird behoben oder aufgeschrieben —
   nicht uebersprungen. Achtung: nicht parallel zu Tests oder compose, die VM
   geht sonst auf 290 % CPU.

3. GET /notifications antwortet 405. Steht in docs/routenkarte.yml als
   gemessen, wurde nie behoben. Entscheide: beheben oder als Absicht
   begruenden.

4. docs/AUFTRAG-ENTLASTUNG.md: die Kommentardichte wurde gemessen und die
   Kuerzung zurueckgenommen. Entscheide, ob sie kommt — und schreib die
   Entscheidung dorthin.

FALLEN: build und test nie zusammen; nie Images bauen waehrend Tests laufen;
dein .env ist NICHT das der CI — was bei dir eingerichtet ist, ist es dort
nicht.

ZUM SCHLUSS — SELBER ERLEDIGEN, nicht zurueckfragen:
1. Lokal gruen machen, in GETRENNTEN Aufrufen:
     dotnet build WorkerTransfer.slnx
     ./scripts/test-dotnet.sh
     cd web && pnpm check && pnpm test && pnpm build && cd ..
2. Committen. Die Nachricht nennt den GRUND, nicht die geaenderte Datei.
3. git push -u origin feature/aufraeumen
4. gh pr create --base develop --fill
5. Warten, bis ALLE FUENF Jobs gruen sind:  gh pr checks --watch
6. ERST DANN:  gh pr merge --merge --delete-branch
7. UND DANN NOCH EINMAL HINSEHEN — der PR prueft den Merge-VORSCHLAG, nicht
   das Ergebnis. Am 09.09.2026 war #67 gruen und develop danach rot:
     gh run watch $(gh run list --branch develop --limit 1 --json databaseId --jq '.[0].databaseId')
   Ist der rot, ist es deine Aufgabe, nicht die der naechsten Sitzung.

Rot heisst reparieren und wiederholen. NIE einen roten Lauf mergen, nie
--admin. Wer die Pipeline rot hinterlaesst, nimmt der naechsten Sitzung ihren
Massstab.
```

---

## Optional, nach Sitzung 1 — Vorschläge aus den eigenen Unterlagen

```
ZUERST, ohne zu fragen: git switch develop && git pull && git switch -c feature/vorschlaege-aus-unterlagen
Diese Sitzung arbeitet NIE direkt auf develop.

Du arbeitest im Repository WorkerTransfer.
Lies: docs/PLAN-TRANSFERMARKT.md (PBI-7 und den Abschnitt "Die KI-Frage"),
docs/adr/0033-beleg-und-sichtbarkeit.md, docs/adr/0024-*.md.

Setze PBI-7 um: aus den hochgeladenen Unterlagen einer Person Vorschlaege fuer
ihr eigenes Profil gewinnen ("In deinem Zeugnis steht Schweissfachmann DVS.
Uebernehmen?").

DIE GRENZE, die diese Arbeit ueberhaupt erlaubt: es spricht ueber die EIGENEN
Dokumente der Person, ZU ihr, und macht daraus nichts ohne zwei Handlungen —
Klick fuellt das Feld, Speichern macht daraus eine Nennung (ADR-0033).

Ein Index ueber FREMDE Profile waere das Gegenteil und ist ausgeschlossen:
durchsuchbar ist nur, was jemand selbst genannt hat.

AUFLAGEN:
- Texterkennung nur auf Ausloesung, nie im Hintergrund (ADR-0004).
- Der Index lebt im selben Dienst wie die Unterlagen (resume-service) und faellt
  mit der Loeschung (ADR-0027).
- Nichts davon ist durchsuchbar, bevor die Person gespeichert hat.

ZUM SCHLUSS — SELBER ERLEDIGEN, nicht zurueckfragen:
1. Lokal gruen machen, in GETRENNTEN Aufrufen:
     dotnet build WorkerTransfer.slnx
     ./scripts/test-dotnet.sh
     cd web && pnpm check && pnpm test && pnpm build && cd ..
2. Committen. Die Nachricht nennt den GRUND, nicht die geaenderte Datei.
3. git push -u origin feature/vorschlaege-aus-unterlagen
4. gh pr create --base develop --fill
5. Warten, bis ALLE FUENF Jobs gruen sind:  gh pr checks --watch
6. ERST DANN:  gh pr merge --merge --delete-branch
7. UND DANN NOCH EINMAL HINSEHEN — der PR prueft den Merge-VORSCHLAG, nicht
   das Ergebnis. Am 09.09.2026 war #67 gruen und develop danach rot:
     gh run watch $(gh run list --branch develop --limit 1 --json databaseId --jq '.[0].databaseId')
   Ist der rot, ist es deine Aufgabe, nicht die der naechsten Sitzung.

Rot heisst reparieren und wiederholen. NIE einen roten Lauf mergen, nie
--admin. Wer die Pipeline rot hinterlaesst, nimmt der naechsten Sitzung ihren
Massstab.
```

---

## Sitzung 7 — Girder auf nuget.org  ✅ ERLEDIGT (10.09.2026)

Nichts mehr zu tun — hier steht nur noch, was daraus wurde.

==========================================================================
Lies docs/AUFTRAG-ENTLASTUNG.md (Phase 4), docs/PLAN-TRANSFERMARKT.md (PBI-8)
und CLAUDE.md.

ZUERST, ohne zu fragen:
  git switch develop && git pull && git switch -c feature/pbi-8-und-entlastung
Diese Sitzung arbeitet NIE direkt auf develop.

==========================================================================
1. DIE KOMMENTAR-ENTSCHEIDUNG IST GETROFFEN — schreib sie fest

Du hast gefragt: „wo verläuft die Grenze zwischen einem Kommentar, der eine
Messung/ein Verbot/eine Falle festhält, und einem, der den Code nacherzählt?"

DIE ANTWORT, und sie gehört wörtlich nach docs/AUFTRAG-ENTLASTUNG.md
(Phase 4 schließen) und als Absatz nach CLAUDE.md zu den Konventionen:

  > Ein Kommentar verdient seinen Platz, wenn sein Fehlen jemanden einen
  > Defekt NEU EINBAUEN ließe. Alles andere erzählt den Code nach.

WEG 2 WIRD NIE GEFAHREN. <remarks> in ADRs zu verschieben ist der teuerste
denkbare Fehler in diesem Baum, und er ist unumkehrbar. Jeder Defekt der
letzten Woche entstand, weil eine Regel NICHT DORT stand, wo jemand sie
brauchte: `secrets` im Schritt-`if`, die Egress-Grenze gegen einen Wert aus
der Datenbank, das fehlende .AsTracking(), die 404-statt-401-Tür, deren Regel
zwei Zeilen tiefer in derselben Datei schon aufgeschrieben war. Ein Grund in
einer ADR ist ein Grund, den man SUCHEN muss — und niemand sucht, was er
nicht vermisst. Schreib diesen Grund mit auf, nicht nur das Verbot.

31 % Dichte ist kein Problem, das gelöst werden muss. Dieser Baum lebt davon.

WEG 1 wird SELEKTIV gefahren, nicht pauschal. Von deinen vier Kopien:
  - „VOR CreateBuilder" 15×        → BLEIBT. Genau dort verschiebt jemand die Zeile.
  - „Kein AlsAussteller()" 12×     → BLEIBT. Verhindert eine falsche Ergänzung.
  - „Erst wandern (ADR-0010)" 15×  → BLEIBT. Ein Zeiger ist kein Nacherzählen.
  - „Der Schlüssel IST der Mensch" 7× → WEG, wo er nur beschreibt.
                                       BLEIBT, wo er vor einer zweiten Spalte warnt.
    Entscheide je Stelle, nicht pauschal, und sag mir danach, wie viele es
    wirklich waren.

Der mechanische Filter wird NICHT wieder benutzt. Er hat dreimal bewiesen,
dass er die Grenze nicht findet — er löschte „warum 503 und nicht 404", und
genau das besteht den Test oben glänzend. Halte das als Warnung fest.

==========================================================================
2. DOCS LÖSCHEN — alle fünfzehn, freigegeben

Git behält sie, das Löschen ist umkehrbar; das Stehenlassen nicht, denn jede
Datei behauptet GEGENWART. Nachgemessen: ROADMAP.md trägt 28 veraltete
Verweise, ULTRAPLAN.md 18.

Weg (beschreiben einen Baum, den es nicht gibt):
  dotnet-README.md · phase-2-prep.md · oberflaeche-erwartete-ansichten.md ·
  oberflaeche-routenkarte.md · befund-e3a…e3e · befund-kandidatenliste-haengt.md

Weg (abgelöste Pläne — gefährlicher, weil sie ERLEDIGT melden, was nie lief):
  ULTRAPLAN.md · ROADMAP.md · MIGRATION-PROMPT.md · prompts-naechste-schritte.md

BLEIBEN (datierte Momentaufnahmen, keine Gegenwartsbehauptungen):
  UEBERGABE.md · KI-EINSATZ-PRUEFUNG.md · ANALYSE-STAND-UND-LUECKEN.md ·
  REVIEW-09-09.md · GIRDER-ANPASSUNGEN.md

Prüf vor jedem Löschen, ob eine andere Datei darauf verlinkt — ein toter Link
ist schlimmer als eine veraltete Datei.

==========================================================================
3. PBI-8 ABARBEITEN

- [ ] Begrenzte Parallelität (max. 3) in entwuerfe.ts statt Promise.all über
      alle gewählten Stellen.
- [ ] Der Fortschritt ist gebaut und wird nie erreicht: setFortschritt wird in
      JobsPage.tsx nur mit null gerufen, die Leiste liest einen Wert, den
      niemand setzt, und stellen.fortschritt steht in DREI Sprachen ohne
      Leser. Entweder anschließen oder restlos entfernen — ein halber Weg ist
      schlimmer als keiner.
- [ ] merkeStelle LÖSCHEN (freigegeben): schreibt, niemand liest,
      gemerkteStelle() hat außerhalb von intent.ts keinen Aufrufer, und
      LoginPage.tsx:66 hat den Rückweg stillgelegt. Dieselbe Klasse wie
      Fund 7. Mit den Katalogschlüsseln, die danach niemand mehr liest.

NICHT anfassen: H2 (Geheimnisfrage) und H5 (/code-review ultra) — das erste
ist eine Entscheidung, das zweite startet ein Mensch. Beide bleiben als
offene Zeilen im Plan stehen.

==========================================================================
4. CLAUDE.md KORRIGIEREN — sie lügt an zwei Stellen, beide nachgemessen

  - „eighteen test projects" → es sind NEUNZEHN (find tests -name '*.csproj')
  - Die Validator-Zeile sagt, wir hätten NULL AbstractValidator geschrieben.
    Es sind SECHS Dateien in identity, github und applications. Schreib hin,
    was wirklich da ist — und ob die Aussage darunter („es fehlt nicht die
    Verdrahtung, sondern der Inhalt") noch stimmt.

==========================================================================
5. DER FLAKE IST GEKLÄRT — aber die Lehre fehlt im Baum

jobs-journey war der KALTE STAPEL, nicht deine Änderung: 4/4 grün auf beiden
Bäumen bei warmem Stapel, kontrolliert gegengeprüft. Nichts offen.

Aber das ist das DRITTE Mal in drei Sitzungen, dass eine E2E-Reise rot war und
die Ursache nicht im Code lag. CLAUDE.md warnt nur vor KONKURRENZ („never
build images and run tests at the same time") — der kalte Stapel ist eine
ANDERE Ursache mit demselben Symptom, und er steht nirgends.

Schreib ihn auf, zu der Konkurrenz-Warnung dazu:

  Ein frisch hochgefahrener Stapel ist langsam, bevor er warm ist — JIT,
  Verbindungspools, die erste Migration. Die erste E2E-Reise dagegen misst
  das Aufwärmen mit und fällt an Stellen, die mit ihrer Behauptung nichts zu
  tun haben. Gemessen am 12.09.2026: jobs-journey rot auf kaltem Stapel,
  4/4 grün auf warmem — auf BEIDEN Bäumen, also unabhängig von der Änderung.

  Die Gegenprobe, die das entscheidet, ist nicht „nochmal laufen lassen",
  sondern: dieselbe Reihe auf dem UNVERÄNDERTEN Baum fahren. Fällt sie dort
  auch, war es nie deine Änderung. Ohne diesen Schritt sucht man Stunden in
  Code, der in Ordnung ist.

  Und NICHT das Zeitlimit hochsetzen. Am 10.09.2026 hat genau das eine echte
  Ursache verdeckt (Zwischenspeicher-Verschränkung in kontext.ts): 5 → 10 s
  half nicht, weil es nie zu langsam war. Eine Wartegrenze hochzusetzen sieht
  immer nach einer Lösung aus und verbirgt genauso oft eine.

==========================================================================
FALLEN: build und test nie zusammen; nie dotnet test über die Lösung, nur
./scripts/test-dotnet.sh; nach jeder Modeländerung sofort dotnet ef migrations
add; ein änderndes SichereAsync braucht .AsTracking(); Warnungen sind Fehler;
eine Gegenprobe muss KOMPILIEREN; ein neuer Aufruf nach draußen muss in der
Konfiguration stehen; dein .env ist NICHT das der CI.

ZUM SCHLUSS — selber erledigen, nicht zurückfragen:
1. dotnet build WorkerTransfer.slnx
2. ./scripts/test-dotnet.sh
3. cd web && pnpm check && pnpm test && pnpm build && cd ..
4. Committen, pushen, gh pr create --base develop --fill
5. gh pr checks --watch
6. ERST DANN: gh pr merge --merge --delete-branch
7. UND DANN den develop-Lauf ansehen.

Ein roter Lauf wird nicht gemergt. Kein --admin.

Das Girder-Repo ist **öffentlich** (MIT), `LICENSE` und `SECURITY.md` liegen,
die README trägt eine Versionspolitik, und `publish.yml` schiebt nach GitHub
Packages **und** nach nuget.org.

**Der Schlüssel ist keiner: nuget.org macht das über OIDC** (Trusted
Publishing). Der Lauf tauscht ein von GitHub signiertes Token gegen einen
Schlüssel, der eine Stunde gilt — nichts zu rotieren, nichts, das abläuft.
Dafür trägt `publish.yml` `id-token: write`, und der nuget.org-Profilname steht
als Repository-**Variable** `NUGET_USER` (nicht als Geheimnis: `vars` ist im
`if:` eines Schritts verfügbar, `secrets` nicht).

Was noch von Hand fehlt: die Trusted-Publishing-Richtlinie auf nuget.org
(Repository Owner `DavidOeztuerk`, Repository `girder`, Workflow File
`publish.yml`, Environment leer, Scope *Push new packages and package
versions*, Glob `Girder.*`), die Variable `NUGET_USER`, und ein Release.
Fehlt eines davon, endet der Release-Lauf **grün** und sagt, was fehlt.

### Und erst wenn die Pakete auf nuget.org liegen

Dann fällt in WorkerTransfer einiges weg — **vorher nicht, sonst restauriert
nichts mehr**, weder lokal noch in Docker:

- die vier NuGet-Anmeldungen in `.github/workflows/ci.yml` und `packages: read`
- das Geheimnis `GIRDER_TOKEN`
- in `docker/dotnet-service.Dockerfile` die Zeile `--mount=type=secret,id=nuget_config`
- in `docker-compose.yml` das Geheimnis `nuget_config` samt seiner Verweise
- in `NuGet.Config` zeigt das Source Mapping für `Girder.*` auf nuget.org

Der Lohn: ein Fork-PR kann bauen, und ein frischer Klon braucht keinen Token —
heute scheitert daran schon `docker compose up`. Das ist eine eigene, kleine
Sitzung.

