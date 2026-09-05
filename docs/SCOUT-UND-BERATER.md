# Scout und Berater

Der Entwurf für die zwei Dienste, die WorkerTransfer zu dem machen, was der Name
sagt: ein Transfermarkt. Dazu ein dritter, den wir vergessen hatten.

Dies ist ein Entwurf, keine Beschreibung. Vor dem Code steht ein ADR.

Was davon **schon dasteht** — teils unter anderem Namen —, und wo dieser
Entwurf einer bereits getroffenen Entscheidung widerspricht, steht in
[`SCOUT-UND-BERATER-BESTAND.md`](SCOUT-UND-BERATER-BESTAND.md). Das ist eine
Messung; dieses Dokument ist eine Absicht. Wo beide sich widersprechen,
gewinnt die Messung.

---

## Das Bild

Im Fußball geht ein Scout zum Spiel, sieht den Spieler und empfiehlt ihn seinem
Verein. Ein Berater steht auf der Seite des Spielers und verhandelt — mit dem
künftigen Verein, und wenn nötig mit dem jetzigen.

Was daran legitim ist: der Spieler weiß, dass er beobachtet wird, hat einen
Berater, und ohne seine Zustimmung passiert nichts. Was daran hässlich ist:
Datenbanken mit Noten, die der Spieler nie sieht.

**Wir bauen das Erste und niemals das Zweite.** Die Trennlinie ist nicht „keine
Analyse", sondern die Richtung der Frage:

| Baubar | Verboten |
|---|---|
| Anforderung rein, Belege raus | Mensch rein, Zahl raus |
| „Go ✓ · Kubernetes ✓ · Rust ✗" | „87 % Passung" |
| Eine Liste in beliebiger Ordnung | Eine Rangfolge |
| Aus **selbst Gesagtem** und **veröffentlichten Belegen** | Aus Commits abgeleitet |

Ein Häkchen sagt, *welche* Fähigkeit fehlt, und lässt sich widersprechen. Eine
Zahl verbirgt genau das.

---

## Was ADR-0022 wirklich verbietet

Er wird als Verbot des Ganzen gelesen. Er ist es nicht — er hat einen eigenen
Abschnitt „Was in Phase 6 wiederkommen darf", und darin steht dieser Entwurf
fast wörtlich:

> **Darf wiederkommen:** Belege mit Herkunft — „Hat an *diesem* Projekt *diese*
> Commits gemacht", nachprüfbar, mit Link, **ohne Zwischenrechnung**.
> Einwilligung zuerst. Sichtbarkeit über den Ledger, jederzeit widerrufbar.

Verboten sind **genau drei Dinge**, und sie greifen alle noch:

**1. Eine Zahl, die einen Menschen zusammenfasst — und jede Rangfolge daraus.**
Das gelöschte Paket rechnete zehn Dimensionen mit festen Gewichten auf 0–100.
Der ADR nennt den Grund, der bleibt: *„Die Gewichte hat niemand begründet. Sie
sind eine Meinung im Gewand einer Formel."* Und eine Nachkommastelle sieht aus
wie eine Messung.

**2. Abgeleitete Eigenschaften ohne Grundlage.** „leadership", „community",
„architecture" aus Repository-Metadaten. Dazu gehört auch der alte
`SkillAnalyzer`: `base_score = bytes_count / total_bytes` — *„Bytes sind kein
Können. Eine eingecheckte Abhängigkeit schlägt jede sorgfältige Bibliothek. Wer
wenig und gut schreibt, verliert."*

**3. Stillschweigende Vollständigkeit.** *„Wer nichts auf GitHub hat, ist nicht
schlechter, sondern woanders. Eine Ansicht, die das nicht sagt, lügt durch
Auslassung."*

### Der dritte Punkt trifft diesen Entwurf, und das ist der wichtigste Fund

Eine Trefferliste, die „Go ✓ · Kubernetes ✓ · Rust ✗" zeigt, behauptet
stillschweigend, die Liste sei über den Menschen **vollständig**. Ist sie nicht:
das ✗ heißt „hat es nicht **genannt**", nicht „kann es nicht". Wer Rust kann und
es nicht getippt hat, steht dort mit einem Kreuz, das über ihn lügt.

Genauso trifft es die Belege: wer kein GitHub verknüpft hat, darf nicht wie ein
leerer Eintrag aussehen. Ein Nebentätigkeitsverbot, Sorgearbeit oder ein
Zweitjob sind keine Aussage über Können — der ADR nennt das ausdrücklich als den
Weg, auf dem eine Rangliste *systematisch benachteiligt*.

**Deshalb steht das nicht im Kleingedruckten, sondern in der Ausgabe selbst.**
Wie, steht unten unter „In der Oberfläche".

**Was aus alldem folgt:** ADR-0022 wird nicht ersetzt und nicht abgeschwächt. Es
kommt ein Folge-ADR, der seinen eigenen „darf wiederkommen"-Abschnitt einlöst
und die drei Verbote als Tests festnagelt.

---

## Wer hat es gesagt? Die eine Frage, die alles ordnet

Jede Fähigkeit, die im System steht, trägt ihre Herkunft. Es gibt genau drei
Klassen, und nur eine davon ist eine Aussage über einen Menschen.

| Klasse | Herkunft | Gilt als |
|---|---|---|
| **Genannt** | Die Person hat es getippt | **Aussage über die Person.** Nur diese ist durchsuchbar |
| **Belegt** | GitHub-Sprachen, Repo-Themen, Lebenslauf | Aussage über ein **Artefakt**. Wird *neben* einer Nennung gezeigt, wird nie selbst zur Nennung |
| **Vorgeschlagen** | Aus einem Beleg erkannt, noch unbestätigt | Nichts. Liegt in der Oberfläche, bis die Person tippt oder verwirft |

**Nichts wird zur Aussage über einen Menschen, bevor die Person es bestätigt
hat.** Das ist dieselbe Regel wie in ADR-0023 („benennt um, folgert nie"), nur
auf neue Eingänge angewandt.

### Was GitHub wirklich hergibt

- `GET /repos/{owner}/{repo}/languages` → Bytes je Sprache. Eine **Messung am
  Repository**: „dieses Repo ist zu 52,4 % C#". Über den Menschen sagt das
  nichts.
- `topics` → was der Besitzer über das Repo **erklärt** hat: `docker`,
  `microservices`, `kubernetes`. Genau das, was in den Sprachen fehlt — und es
  ist eine Aussage der Person, also die bessere Quelle.
- Beschreibung, Sterne, Datum → Anzeige, nie Sortierschlüssel über Menschen.

Sprachanteile werden **am Repository** angezeigt, nie am Menschen aufaddiert.
„Diese Person ist zu 52 % C#" ist ein Satz, den es nicht geben darf.

### Der Lebenslauf

Hochladen, auslesen, **vorschlagen** — nicht speichern als Fähigkeit. Die
Oberfläche zeigt: *„Im Lebenslauf gefunden: Docker, Terraform, Kafka. Welche
davon willst du in dein Profil übernehmen?"* Erst der Klick macht daraus eine
Nennung.

Der Grund ist nicht Förmlichkeit: ein automatisch übernommener Skill ist eine
Behauptung, die jemand über die Person aufgestellt hat, ohne sie zu fragen — und
sie steht dann in einer Suche, in der die Person gefunden wird.

---

## Die neuen Dienste

### `scout-service` — die Anforderung, nicht die Rangliste

Unternehmensseitig. Nimmt eine Anforderung entgegen und liefert Menschen, die
sie erfüllen, jeden mit einer Checkliste und Belegen.

```
POST /scout/searches      Anforderung anlegen (Fähigkeiten, Ort, Verfügbarkeit)
GET  /scout/searches/{id} Treffer: je Person Häkchen + Belege
POST /scout/searches/{id}/approach  Entwurf einer Ansprache — an einen Menschen
```

**Vier Auflagen, jede als Test:**

1. **Keine Sortierung nach Passung.** Die Reihenfolge ist stabil und
   sachfremd (zuletzt aktualisiert). Kein `ORDER BY` über eine Trefferzahl.
2. **Keine Zahl.** Kein Prozentwert, kein „3/5" als einzige Ausgabe — die
   Häkchen stehen namentlich da.
3. **Nur über Genanntes.** Der Suchindex kennt ausschließlich Klasse *Genannt*.
   Belege werden zum Treffer dazugeholt, nie zum Finden benutzt.
4. **Die Ansprache ist ein Entwurf.** Der Dienst schreibt niemandem. Ein Mensch
   liest, ändert und schickt.

**Und die Person sieht es.** `GET /me/scouting` zeigt: *„Du bist am 3. März in
einer Suche von Firma X aufgetaucht."* Das ist der Unterschied zwischen einem
Scout und einer Datenbank mit Noten — und es ist der Grund, warum das hier
gebaut werden darf.

### `advisor-service` — der Berater

Er analysiert niemanden. Er steht auf der Seite **einer** Person und trägt zwei
Dinge: ihr **Mandat** und ihre **Gespräche**.

#### Das Mandat — was der Berater darf

Alles voreingestellt von der Person, jederzeit änderbar, und die Änderung wirkt
sofort (kein Cache, wie bei jeder Einwilligung):

```
Sichtbarkeit    Wer darf mich in einer Suche finden?
                  niemand · Unternehmen, die ich freigebe · alle Unternehmen
                Was sehen sie dann? Profil · Belege · Lebenslauf (je einzeln)

Konditionen     Frühester Eintrittstermin
                Gehaltsvorstellung — als Spanne, und optional erst ab Stufe 2
                Arbeitsort, Pensum, Reisebereitschaft
                Ausgeschlossene Unternehmen (namentlich)

Jetziger        Darf der Berater meinen jetzigen Arbeitgeber ansprechen?
Arbeitgeber       nein (Vorgabe) · erst wenn ich es je Vorgang freigebe
                  · ja, ab Stufe 3
                Darf mein jetziger Arbeitgeber sehen, dass ich im Markt bin?
                  nein (Vorgabe) — und das ist keine Einstellung, die ein
                  Unternehmen umgehen kann
```

**Die Vorgabe ist überall die zurückhaltendste.** Wer nichts einstellt, ist
unsichtbar. Ein Markt, in dem man versehentlich sichtbar ist, ist ein Leck.

#### Die Gespräche — mehrstufig, weil Unternehmen mehrstufig sind

Ein Unternehmen spricht nicht als Block. Erst HR, dann die Fachabteilung, dann
wer unterschreibt. Jede Stufe sieht mehr, und **jede Stufe gibt die Person
frei**:

```
Stufe 1  Erstkontakt      HR sieht: Profil, genannte Fähigkeiten, Verfügbarkeit
Stufe 2  Fachgespräch     + Belege, Lebenslauf, Gehaltsspanne
Stufe 3  Verhandlung      + Klarname, Kontakt, ggf. jetziger Arbeitgeber
```

Der Berater trägt das Mandat in jede Stufe mit — die Person muss ihren frühesten
Eintrittstermin nicht dreimal sagen. Und was in Stufe 1 nicht freigegeben ist,
existiert für die Gegenseite nicht: kein „gesperrt"-Hinweis, der verrät, dass es
etwas gibt.

```
POST /advisor/mandate                   Mandat setzen
GET  /advisor/mandate
POST /advisor/conversations             Unternehmen eröffnet — Stufe 1
POST /advisor/conversations/{id}/advance  Person hebt auf die nächste Stufe
POST /advisor/conversations/{id}/messages
POST /advisor/conversations/{id}/close  beidseitig, mit Grund an die eigene Seite
```

Einigt man sich, geht es an `transfer-service` — das ist der bestehende Dienst
für den förmlichen Wechsel samt Dreieckskonsens. Der Berater führt bis zur
Einigung, der Transfer beginnt danach.

### `assessment-service` — die Aufgabe

Der Dienst, den wir vergessen hatten. Ein Unternehmen findet jemanden
interessant und will sehen, wie er arbeitet.

```
POST /assessments                       Aufgabe stellen: Text, Frist, Umfang
POST /assessments/{id}/submission       Lösung hochladen (portfolio-service)
POST /assessments/{id}/evaluation       Rückmeldung des Unternehmens
GET  /assessments/{id}                  beide Seiten sehen dasselbe
```

**Drei Regeln, und sie sind der Unterschied zwischen einer Aufgabe und einer
Prüfung mit Note:**

1. **Die Bewertung gehört dem Vorgang, nicht dem Menschen.** Sie ist in keiner
   Suche sichtbar, in keinem Profil, für kein anderes Unternehmen. Sie hat keine
   Zahl, sondern Text.
2. **Die Person sieht die Bewertung.** Immer, auch bei Absage. Eine Beurteilung,
   die der Beurteilte nie liest, ist genau das, was wir nicht bauen.
3. **Die Aufgabe ist begrenzt und benannt.** Umfang in Stunden steht in der
   Ausschreibung. Unbezahlte Arbeit als Aufgabe getarnt ist der Missbrauch, den
   dieses Feature einlädt — deshalb steht der Umfang vorne und die Person kann
   ablehnen, ohne dass es irgendwo vermerkt wird.

---

## In der Oberfläche

Die drei Verbote sind keine Prüfungen im Hintergrund, sie sind sichtbare
Gestaltung. Wo sie nur im Code stünden, wären sie in einem Redesign weg.

**Das Kreuz sagt, worüber es spricht.** Nicht „✗ Rust", sondern:

```
Go            ✓  genannt
Kubernetes    ✓  genannt · Belege: 3 Repos
Rust             nicht genannt
```

Kein rotes Kreuz, kein Durchstreichen. Eine leere Stelle ist eine leere Stelle.

**Jede Trefferliste trägt einen Satz, der nicht wegklickbar ist:**

> Diese Liste zeigt, was Menschen **selbst genannt** haben. Wer etwas nicht
> genannt hat, kann es trotzdem können. Die Reihenfolge ist *zuletzt
> aktualisiert*, nicht *beste Passung*.

Der zweite Halbsatz ist ebenso wichtig wie der erste: eine unsortierte Liste
sieht aus wie eine sortierte, wenn niemand dazuschreibt, wonach sie geordnet ist.

**Belege tragen ihre Herkunft im Text**, nicht in einem Tooltip:

```
C# 52,4 %          in diesem Repository, laut GitHub gemessen
docker             vom Besitzer als Thema des Repositorys gesetzt
Terraform          aus dem Lebenslauf, von der Person bestätigt
```

Nie „C# 52 %" neben einem Namen. Der Anteil gehört zum Repository, und das steht
daneben.

**Wer keine Belege verknüpft hat, sieht neutral aus:**

> Keine Belege verknüpft. Das sagt nichts über Können — nicht alle Arbeit ist
> öffentlich.

Kein leerer Kasten, kein ausgegrauter Bereich, kein „0 Repos". Ein leerer Kasten
ist ein Urteil ohne Worte.

**In der Ansicht der Person** steht dieselbe Wahrheit von der anderen Seite:

```
Dein Profil nennt 7 Fähigkeiten.
Unternehmen suchen nach dem, was hier steht — nicht nach dem, was du kannst.
[ Fähigkeit hinzufügen ]
```

Das ist der ehrlichste Satz der ganzen Plattform: er sagt der Person, wovon ihre
Auffindbarkeit wirklich abhängt, statt sie raten zu lassen.

**Und die Gegenrichtung, die es schon gibt:** `apps/web/src/jobs/match.ts` zeigt
einer Person „Du hast 2 von 3 genannten Fähigkeiten: Python ✓ · Kubernetes ✓ ·
Go ✗". Das darf eine Zählung tragen, weil es **Stellen für einen Menschen**
ordnet und nicht Menschen für ein Unternehmen. Die Scout-Ansicht darf es nicht.
Dieselbe Gestaltung, andere Richtung, andere Regel — und genau deshalb steht der
Unterschied hier und nicht in einem Kommentar.

---

## Die Reisen

Playwright-Journeys, aus vier Blickwinkeln. Sie sind zugleich die Abnahme.

### Als Mensch, der gefunden werden will

```
1  Registrieren, bestätigen, Profil anlegen
2  GitHub verbinden, Besitz per Challenge beweisen
   → Repos erscheinen. Am Repo steht "C# 52,4 % · TypeScript 45,7 %"
   → NICHT am Menschen. Prüfen: kein Prozentwert neben dem Namen
3  Vorschlag erscheint: "In deinen Repos gefunden: C#, TypeScript, Docker.
   Übernehmen?"  → zwei annehmen, einen verwerfen
   → Prüfen: nur die zwei stehen im Profil
4  Lebenslauf hochladen → Vorschläge → einen übernehmen
   → Prüfen: der Lebenslauf ist nicht öffentlich, nur der übernommene Skill
5  Mandat setzen: sichtbar für alle · frühester Termin 1.10. · Spanne ab Stufe 2
   · jetziger Arbeitgeber NEIN
6  Später: /me/scouting zeigt "Firma X hat dich am 3.3. gefunden"
7  Widerruf: Sichtbarkeit auf "niemand"
   → Prüfen: dieselbe Suche findet nichts mehr, beim NÄCHSTEN Aufruf
```

### Als Unternehmen, das jemanden sucht

```
1  Anmelden, Firmenkontext wählen
2  Suche anlegen: "Go, Kubernetes, verteilte Systeme, Remote"
3  Treffer: je Person "Go ✓ genannt · Kubernetes ✓ genannt · verteilte Systeme
   nicht genannt", Belege verlinkt
   → Prüfen: KEINE Prozentzahl, KEINE Sortierung nach Trefferzahl
   → Prüfen: zweimaliges Laden gibt dieselbe Reihenfolge
   → Prüfen: der Hinweis "zeigt, was Menschen selbst genannt haben" steht da
     und ist nicht wegklickbar
   → Prüfen: eine Person OHNE GitHub sieht neutral aus, nicht leer
4  Ansprache: Entwurf erscheint, wird geändert, Mensch klickt Senden
   → Prüfen: ohne Klick ist nichts rausgegangen
5  Gespräch Stufe 1: Profil sichtbar, Lebenslauf NICHT
   → Prüfen: kein Hinweis, DASS es einen Lebenslauf gibt
6  Person hebt auf Stufe 2 → Lebenslauf und Spanne erscheinen
7  Aufgabe stellen: "Kleiner Dienst, 4 Stunden, bis Freitag"
   → Lösung kommt, Rückmeldung als Text
   → Prüfen: die Rückmeldung taucht in KEINER Suche und in KEINEM Profil auf
8  Einigung → Übergabe an transfer-service
```

### Als jemand, der bei einem Unternehmen ist

```
1  Mandat: "jetziger Arbeitgeber darf NICHT angesprochen werden"
2  Gespräch bis Stufe 3
   → Prüfen: die Gegenseite sieht keinen Weg, den jetzigen Arbeitgeber zu
     kontaktieren, und keinen Hinweis, dass es einen gibt
3  Person gibt für DIESEN Vorgang frei
   → erst jetzt kann der Transfer den Dreieckskonsens starten
4  Gegenprobe: der jetzige Arbeitgeber ruft /candidates und /scout
   → Prüfen: die eigene Belegschaft taucht NICHT auf
```

### Als Mensch, der es sich anders überlegt

```
1  Konto löschen
   → Prüfen: Suchen finden nichts mehr, Gespräche sind fort,
     Bewertungen sind fort, GitHub-Verbindung ist fort
   → Prüfen: die Anfragen der Unternehmen an sie sind ebenfalls fort —
     ein Gesprächsverlauf ist kein Firmenbesitz
```

---

## Was niemals passieren darf

Als Tests, die rot werden, wenn jemand „aufräumt":

- Kein Feld, kein Endpunkt, keine Spalte mit `score`, `rank`, `weight`,
  `probability`, `fit`, `percent` über einen Menschen
- Keine Sortierung einer Personenliste nach etwas, das aus der Person kommt
- Kein Sprachanteil, der über Repositories hinweg zu einem Menschen addiert wird
- Keine Fähigkeit im Suchindex, die die Person nicht getippt hat
- Kein „gesperrt"-Hinweis, der die Existenz von etwas verrät
- Keine Nachricht, die ohne Klick eines Menschen rausgeht
- Kein Zugriff eines Unternehmens auf die eigene Belegschaft über Scout
- Keine Bewertung, die den Vorgang überlebt
- Kein „✗" oder Durchstreichen an einer nicht genannten Fähigkeit — nur „nicht
  genannt"
- Keine Trefferliste ohne den Hinweis auf Selbstnennung und Sortierung
- Kein leerer oder ausgegrauter Bereich für jemanden ohne Belege

---

## Offene Entscheidungen

Vor dem ADR zu klären:

1. ~~Ergänzt oder ersetzt das ADR-0022?~~ **Beantwortet: keins von beidem.** Er
   verbietet drei Dinge und erlaubt diesen Entwurf in seinem eigenen Abschnitt
   „darf wiederkommen". Es kommt ein Folge-ADR, der ihn einlöst und die drei
   Verbote als Tests festnagelt — siehe oben.
2. **Wie verhält sich `scout-service` zum bestehenden `/candidates`?** Vermutlich
   löst er es ab — dieselbe Frage, bessere Antwort.
3. **Trägt der Berater eine KI?** Er darf entwerfen (Nachrichten, Zusammen-
   fassungen der eigenen Lage), aber nie über Dritte sprechen und nie
   selbstständig senden. Das ist die Haltung von `/profiles/me/draft`.
4. **Wie lange lebt eine Suche?** Ein gespeichertes Suchergebnis ist eine
   Momentaufnahme über Menschen. Vorschlag: Ergebnisse werden nicht gespeichert,
   nur die Anfrage — jeder Aufruf fragt neu, damit ein Widerruf sofort wirkt.
