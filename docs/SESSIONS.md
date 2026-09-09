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

Also je Sitzung: `git switch develop && git pull && git switch -c <name>`,
am Ende ein PR nach `develop`. Fahre `./scripts/test-dotnet.sh` und
`pnpm check` lokal, bevor du den PR aufmachst — die CI ist grün, und wer sie
rot hinterlässt, nimmt der nächsten Sitzung ihren Maßstab.

| # | Sitzung | Hängt ab von | Umfang |
|---|---|---|---|
| 1 | Berufsfelder und Belege | — | groß |
| 2 | Rollen erzwingen | — | mittel |
| 3 | `scout-service` | 1 | groß |
| 4 | `advisor-service` | 1 | groß |
| 5 | `assessment-service` | 3, 4 | mittel |
| 6 | Aufräumen | — | klein |

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
7. **Dein `.env` ist nicht das `.env` der CI.** `make env` baut aus
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
add; ein aenderndes SichereAsync braucht .AsTracking(); Warnungen sind Fehler;
dein .env ist NICHT das der CI — leere Zugangsdaten in .env.example heissen
"nicht eingerichtet", und eine Pruefung dagegen ist lokal gruen und in der CI
rot; E2E einmal am Ende, mit eindeutigen Selektoren.
```

---

## Sitzung 2 — Rollen erzwingen

```
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
Warnungen sind Fehler; dein .env ist NICHT das der CI — leere Zugangsdaten in
.env.example heissen "nicht eingerichtet"; eine Gegenprobe muss KOMPILIEREN,
sonst liest sich der Build-Fehler wie ein bestandener Test.
```

---

## Sitzung 3 — `scout-service`

```
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
Vertraege, nie anonyme Objekte; Warnungen sind Fehler; dein .env ist NICHT das
der CI — leere Zugangsdaten in .env.example heissen "nicht eingerichtet";
E2E einmal am Ende.
```

---

## Sitzung 4 — `advisor-service`

```
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
```

---

## Sitzung 5 — `assessment-service`

```
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
```

---

## Sitzung 6 — Aufräumen

```
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
```

---

## Optional, nach Sitzung 1 — Vorschläge aus den eigenen Unterlagen

```
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
```
