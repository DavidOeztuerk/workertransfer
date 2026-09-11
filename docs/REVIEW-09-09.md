# Review des Zweigs `dotnet-migration`, 09.09.2026

> **Ein Review ist ein Datum, und die Zahlen darin altern.** Sie bleiben hier
> stehen — ein Review, das man nachträglich fortschreibt, ist keine Messung
> mehr. Was seither dazukam, steht daneben, gemessen am 11.09.2026:
>
> | | 09.09.2026 | 11.09.2026 |
> |---|---|---|
> | .NET-Tests | 966 in 16 Reihen | **1236 in 19 Reihen**, 0 rot, 0 übersprungen |
> | Frontend-Tests | 150 in 20 Dateien | **198 in 25 Dateien** |
> | E2E-Reisen | 23 | **30** in 16 Dateien (gezählt, nicht gefahren — sie brauchen den Stapel) |
> | Dienste | elf | **vierzehn** (scout, advisor, assessment kamen am 11.09. dazu) |
>
> **Fund 7 (das tote Bewerbungsformular) ist erledigt** — PBI-6 im
> [PLAN](PLAN-TRANSFERMARKT.md).

Was seit `d3cf4f8` entstanden ist — **119 geänderte, 62 neue Dateien, +5.584
Zeilen** —, gemessen statt gelesen. Drei Sitzungen haben daran gearbeitet:
Grok (`~/.grok/sessions/…/01a0783d-…`, 33 Nutzernachrichten über drei Tage),
Antigravity, und diese hier.

**Nichts davon ist committet.** Der Vergleichspunkt ist `d3cf4f8`.

---

## Die Kurzfassung

| | Stand |
|---|---|
| .NET | **966 Tests grün, 0 rot, 0 übersprungen, 0 Warnungen** (Girder 4.4.0) |
| Frontend | 150 Tests, 20 Dateien, `tsc` sauber, Build durch |
| E2E | **23 von 23 grün** — bei Beginn dieses Reviews waren **14 rot** |
| Routenkarte | 414 Antworten, alle wie aufgeschrieben |
| Kataloge | 1038 Schlüssel, in allen drei Sprachen gleich |
| **Phase 5** | ✅ **fertig** — die Benachrichtigung steht, samt Drahttest |
| **Phase 6** | ✅ im Wesentlichen fertig |
| **Phase 7** | ADR-0036 und ADR-0037 geschrieben, **kein Code** (so gewollt) |

---

## Was ich nachgemessen habe

**Phase 5 ist gebaut, und zwar richtig.** `ApplicationReceived` steht an allen
vier Stellen (Enum, `Alle`, `Wort`, `Lies`), die Empfänger sind die *Mitglieder*
des Unternehmens, und der Weg dorthin ist ein **typisierter Vertrag**
(`Contracts.Identity/Unternehmensmitglieder.cs`) statt eines anonymen Objekts.
`MitgliederdrahtTests` prüft genau das Muster, an dem der Benachrichtigungsdraht
viermal gescheitert ist: schreiben mit dem Sendertyp, lesen mit dem
Empfängertyp, und der Wert muss überleben.

**Der DSGVO-kritische Pfad hält.** ADR-0038 führt bürgerlichen Namen und
Anschrift ein — und die Anschrift erreicht das Modell **nicht**. Der Kontexttyp
`Anschreibenkontext` hat unverändert elf Felder, `Eigenbild` trägt keine
Anschrift, und `Der_Kontext_traegt_keine_Anschrift` verbietet die Feldnamen.
Das ADR nennt den Leckweg sogar selbst („sonst zöge `HttpBewerberauskunft` sie
in den Prompt") — und der ist zu.

**Der KI-Schlüssel liegt verschlüsselt und geht nie an den Browser.**
`SchluesselVerschluesselt` in `Kontoeinstellungen`, im Klartext ausschließlich
über `InterneEndpoints` hinter `X-Notify-Secret` — die ohne Geheimnis mit
**404** antworten, nicht 401, und keine Gateway-Route haben.

**Streaming ist umgesetzt** (SSE, `stream = true`, Token-Parsing je Stück), die
Uhr läuft über Navigation hinweg weiter, und die Firmenmappe hat **scrollbare
Reiter** (`variant="scrollable"`, `scrollButtons="auto"`).

**Antigravitys zwei Korrekturen stimmen beide:** das Zeitlimit im Anschreiber
steht auf 60 Sekunden (`ZeitlimitTests` verlangt höchstens das), und
`/companies/{id}/profile` steht in der Offen-Liste von `RoutenkarteTests`.

---

## Die Funde

### 1. Ein stiller Schreibverlust — behoben

`EfUnterlagenSpeicher.SichereAsync` las die Zeile **ohne `AsTracking()`**. Der
ganze Kontext fährt `QueryTrackingBehavior.NoTracking`, also kam sie abgelöst
zurück: die Zuweisungen liefen ins Leere, `SaveChanges` sah nichts, und der
Aufrufer bekam trotzdem seine `204`.

Gemessen an `PUT /resumes/me/documents/{id}/as-cv`: Antwort 204, Art blieb
`sonstiges`.

**Warum es so lange unsichtbar war:** der Anlegepfad ist davon nicht betroffen,
weil `Add` immer verfolgt. Es fällt erst beim ersten *ändernden* Aufrufer auf —
und den gab es bis `as-cv` nicht.

**Es war der einzige von neunzehn Speichern ohne `AsTracking()`**, und der
Fehler ist meiner aus der Erstumsetzung. (Groks Runde hat denselben Fehler bei
`EfEntwurfsspeicher` bemerkt und dort nachgerüstet.)

### 2. Die Vorschau des Lebenslaufs fehlte — behoben

Auf `/resume` wählte die Person eine Vorlage aus drei Kacheln und **sah das
Ergebnis nie**. `Lebenslaufblatt` war nur auf der Unternehmensseite verdrahtet,
obwohl sein eigener Kommentar sagt „für die Person und das Unternehmen
dieselbe". ADR-0035 verlangt: *„dieselbe Darstellung, die sie selbst sieht;
nichts wird für den Empfänger anders gerendert."*

Jetzt steht dieselbe Komponente unter der Vorlagenwahl, gespeist aus dem
**laufenden Formular** — wer eine Station tippt, sieht sie im Blatt, bevor er
speichert.

### 3. Die Unterlagen hatten keine Tests — geschrieben

Die Resume-Reihe stand seit Phase 3 unverändert bei 15 und deckte nur
Lebenslauf und Anfragen ab. Jetzt **29**, mit einer *echten* Ablage auf der
Platte statt einer Probe im Speicher: die Hälfte der Zusagen aus ADR-0035
betrifft die **Datei** und nicht die Zeile.

Was die neue Reihe festhält: hochladen–zeigen–holen–löschen mit Blick auf die
Platte · eine gefälschte Endung (`zeugnis.png` auf einer Programmdatei) wird
abgewiesen und hinterlässt nichts · ein echtes PNG mit falscher Endung geht
durch (die Gegenhälfte) · zehn sind genug · ohne Freigabe sieht die Firma
nichts, und zwar *dasselbe* Nichts wie bei jemandem ohne Unterlagen · ein
Widerruf wirkt auf den nächsten Aufruf · **die Lebenslauffreigabe öffnet die
Unterlagen nicht** (zwei Fähigkeiten, zwei Entscheidungen) · eine Datei wird
zum Lebenslauf · die Löschung räumt Zeilen **und** Dateien.

### 4. Ein Farbliteral — behoben

`ProfilAvatar.tsx` schrieb eine Farbe wörtlich aus — die einzige Stelle im
Baum. Es war **vorbestehend** (aus `79fe590`), nicht aus dieser Runde. Jetzt
steht dort `common.white` aus dem Thema: welche Farbe „hell" heißt, entscheidet
das Thema und nicht eine Komponente.

### 5. Vierzehn von dreiundzwanzig E2E-Reisen rot — behoben

**Und es war kein Produktfehler, sondern ein Selektor-Zusammenstoß.**

`Zivilidentitaet` hat auf `/profile` einen zweiten Knopf gebracht — „Name und
Anschrift speichern". Achtzehn Stellen in zehn Reisen suchten den Speichern-
Knopf als `getByRole("button", { name: /Speichern/i })`, und dieser Regex traf
ab da **zwei** Knöpfe:

```
strict mode violation: getByRole('button', { name: /Speichern/i })
  resolved to 2 elements:
    1) "Speichern"
    2) "Name und Anschrift speichern"
```

Ein Regex auf ein so gewöhnliches Wort war immer zum Kollidieren verurteilt;
der zweite Knopf hat es nur ausgelöst. Alle achtzehn stehen jetzt auf
`{ name: "Speichern", exact: true }` — das räumte sieben Reisen ab.

**Die übrigen sieben hatten drei weitere Ursachen**, und zwei davon waren
dasselbe Muster:

*Zwei Felder namens „Ort".* Die Anschrift brachte ein zweites — und
`getByLabel(/Ort/i)` traf beide. Der Fund liegt aber nicht im Test: **Englisch
und Französisch unterscheiden längst** (`Location`/`City`, `Lieu`/`Ville`), nur
Deutsch nannte beide „Ort". Zwei gleichnamige Felder auf einer Seite sind auch
für einen Vorleser ununterscheidbar. Das Profilfeld heißt jetzt **„Standort"** —
das trifft, was es meint (wo jemand arbeiten will), und deckt sich mit den
anderen zwei Sprachen.

*Ein MUI-Chip trifft sich selbst.* `getByText(/Freigegeben/i)` fand zwei
Elemente, weil ein Chip den Text in einem `span` innerhalb eines `div` rendert.
Eines davon per `.first()` zu greifen wäre eine Wette, kein Test. Geprüft wird
jetzt die **Folge**: nach der Freigabe ist *Senden* bedienbar, und nach dem
Senden gibt es den Knopf nicht mehr. Das ist ohnehin die schärfere Aussage.

*Und eine echte Produktänderung.* „Bewerben" auf der Stellenkarte war ein Link
auf `/jobs/{id}/apply`; heute ist es ein Knopf, der einen Entwurf anlegt — so
gewollt und ausdrücklich gewünscht. Die Reise prüfte den Klick; was sie als
Einzige beweist, hängt aber an der **Adresse**: dass ein Deep-Link durch Router
*und* Gateway ankommt (`Sec-Fetch-Dest`). Sie holt die Kennung jetzt über die
öffentliche Schnittstelle und steuert die Adresse direkt an — dieselbe Aussage,
ohne einen Knopf zu prüfen, den es so nicht mehr gibt.

**Der eigentliche Fund liegt daneben:** die Reihe war rot, und niemand hat es
gemerkt. Die Unit-Tests konnten es nicht sehen — sie rendern gegen Proben, und
zwei Knöpfe stören dort niemanden. E2E ist das Einzige, was die Naht zwischen
Browser und Dienst fährt, und genau deshalb stand in `AUFTRAG-OPENCODE.md`
„E2E einmal am Ende". Dieses eine Mal hat gefehlt.

### 5b. Und darunter lagen noch drei Wartestellen

Nach den Selektoren blieben Fehler übrig, die **nur unter Last** auftraten — der
Lauf brauchte 22 Minuten statt 10, und drei Reisen scheiterten an
`click()`/`fill()` statt an ihrem Prüfgegenstand. Die Lehre stand längst in
diesen Dateien: *„Erst warten, dann klicken: `click()` hat nur das
actionTimeout (15 s), `expect(...).toBeVisible()` das grössere expect-Budget."*
Sie galt an drei Stellen noch nicht — der Registrierungshilfe in `stack.ts`
(zweimal) und der Wegwahl auf `/resume`.

**Nicht angefasst: das Zeitlimit der Löschkaskade.** `erasure-journey` gibt ihr
120 Sekunden, und unter der Last reichten sie einmal nicht. Ein Zeitlimit
hochzudrehen, um Rot grün zu machen, ist der falsche Griff — also erst
gemessen: **335 löschbezogene Protokollzeilen im Stapel, kein einziger Fehler.**
Die Kaskade läuft sauber, sie war langsam. Bleibt sie auf einer ruhigen Maschine
grün, war es die Last.

### 5c. Zwei Beschriftungen, die mit der Fachlichkeit gewandert sind

„Beruf, Ausbildung und Schule trennen" hieß auch: aus *Station hinzufügen* wurde
*Stelle hinzufügen*, aus *Position* wurde *Tätigkeit / Stelle*. Die Reisen
suchten die alten Wörter. Dazu legt die Wahl „selbst schreiben" bereits eine
leere Zeile an — der zusätzliche Klick der Reise erzeugte eine zweite, und
`getByLabel(/Arbeitgeber/i)` traf dann beide.

### 5d. Eine Wette, die als Test getarnt war

Die Briefreise öffnete `getByRole("button", { name: /Öffnen/i }).first()` — den
obersten Entwurf. Die Liste ist nach Änderungszeit sortiert, also mal der eine,
mal der andere. **Gesendet wurde der Entwurf zur zweiten Stelle, geprüft wurde
die erste.** Im Protokoll standen drei erfolgreiche `POST …/send → 200`, in der
Datenbank zwei Entwürfe auf `approved` ohne `sent` — daran ließ es sich
auseinanderhalten.

Eine Reihenfolge, auf die sich ein Test verlässt, ohne sie zu setzen, ist keine
Prüfung: sie wäre irgendwann von selbst grün geworden und hätte nichts bewiesen.

### 6. Die Löschbestätigung wurde weggerissen — behoben

**Eine echte Regression, und CLAUDE.md warnt an derselben Stelle davor.**

`SessionGate` bekam eine Regel: fällt der Status auf `anonymous`, navigiere zur
Startseite. Für eine *abgelaufene* Sitzung ist das genau richtig und war der
Wunsch. Aber die Regel unterscheidet nicht, **wer** die Sitzung beendet hat.

Die Löschseite beendet sie **absichtlich** und zeigt danach „Deine Löschung ist
angenommen und läuft" — die einzige Auskunft, die es zu diesem Vorgang je gibt.
Die Regel navigierte sie weg; die Person sah stattdessen die Marketingseite.
`erasure-journey` fand die Bestätigung nicht mehr.

CLAUDE.md beschreibt die verwandte Falle wörtlich: *„der angenommene Zustand
muss Vorrang vor der Anmeldeaufforderung haben, sonst sieht die Person ‚Bitte
anmelden' direkt nach dem Löschen ihres Kontos."* Das hier ist dieselbe Falle
eine Ebene höher. Die Löschseite ist jetzt ausgenommen, mit dem Grund daneben.

### 8. Klickbare Karten ohne Tastaturweg — behoben

Die Wegwahl auf `/resume` („selbst schreiben" / „Datei hochladen") und die drei
Vorlagenkacheln waren `<Card onClick>`: **keine Rolle, kein `tabIndex`, kein
Tastaturweg.** Wer mit der Tastatur oder einem Vorleser arbeitet, konnte nicht
wählen, wie sein Lebenslauf entsteht — die Wahl war sichtbar und unerreichbar.

Alle fünf sind jetzt `CardActionArea`, also echte `button`-Elemente mit Fokus,
Eingabe- und Leertaste; die Vorlagenkacheln tragen zusätzlich `aria-pressed`.

**Gefunden hat es die E2E-Reise, die einen Knopf suchte und keinen fand.** Sie
hatte recht, und der Fehler lag nicht bei ihr — das ist der Unterschied
zwischen „Test anpassen" und „nachsehen, warum er das sagt".

### 7. Das Bewerbungsformular ist unerreichbar — gemeldet, nicht angefasst

`/jobs/{id}/apply` ist heute eine **Weiche**: für eine angemeldete Person legt
die Seite einen Entwurf an und führt auf `/applications/drafts/{id}`. Genau so
gewünscht.

Die Folge ist aber, dass das alte Formular darunter — rund 120 Zeilen samt
Freigabeschaltern und Absendeknopf — von **keinem** Pfad mehr erreicht wird:
Angemeldete fängt die Weiche ab, Abgemeldete die „Konto nötig"-Karte.

**Ich habe es nicht gelöscht.** Eine Seite um 120 Zeilen zu kürzen ist eine
Produktentscheidung, keine Aufräumarbeit — und toter Code mit
Einwilligungsschaltern ist genau das, was später falsch wiederbelebt wird.
Es gehört auf die Liste, nicht in diesen Durchlauf.

---

## Was offen bleibt, und warum

| Punkt | Stand |
|---|---|
| `scout-service`, `advisor-service` | ADR-0036 und ADR-0037 **angenommen**, kein Code — und das ist die Reihenfolge, die beide ADRs selbst verlangen |
| `assessment-service` | kein ADR, kein Code. Der dritte aus `SCOUT-UND-BERATER.md`, und der mit dem größten Missbrauchspotenzial |
| Rollen (`admin` vs `member`) | **nirgends erzwungen** — null `RequirePermission` im Baum. Die Navigation *versteckt* Firmeneinträge, der Server antwortet 403. Das ist keine Zugriffskontrolle |
| `make k8s-up` | nie gelaufen. Das Diagramm lintet und rendert; nur ein Lauf beweist es |
| `GET /notifications` → 405 | in der Routenkarte festgehalten, nicht behoben |
| Kommentardichte | `docs/AUFTRAG-ENTLASTUNG.md` — gemessen, zurückgenommen, wartet auf eine Entscheidung |
| Totes Bewerbungsformular | Fund 7: unerreichbar seit der Weiche. Löschen ist eine Produktentscheidung |

---

## Der Plan

**Phase 5 braucht nichts mehr.** Was noch aussteht, in der Reihenfolge, in der
es Wert hat:

- [x] **Tests für die Unterlagen** — 15 → 29, und sie haben Fund 1 gefunden
- [x] **Fund 1: `AsTracking()`** in `EfUnterlagenSpeicher`
- [x] **Fund 2: Vorschau** auf `/resume`
- [x] **Farbliteral** in `ProfilAvatar` durch `common.white` aus dem Thema ersetzt
- [x] **`make routenkarte`**: **414 Antworten, alle wie aufgeschrieben** — und
      dabei zwei Statuscodes korrigiert, weil die Karte recht hatte:
      `POST /resumes/me/documents` ohne Multipart antwortet jetzt **415** statt
      400 (die Anfrage ist wohlgeformt, das Medienformat wird nicht bedient),
      und `POST /applications/drafts` mit leerer Liste **422** statt 201 — ein
      „Created", das nichts angelegt hat, ist eine Unwahrheit im Statuscode
- [x] **E2E**: von **14 rot** auf **23 von 23 grün** in sieben Läufen. Zehn
      Ursachen, und **keine einzige war ein kaputtes Feature**: vier zu lose
      Selektoren, zwei gewollte Produktänderungen ohne nachgezogene Reise, drei
      Wartestellen unter Last, eine Wette auf Sortierreihenfolge — **plus zwei
      echte Defekte, die nur E2E finden konnte** (Fund 6 und Fund 8)
- [ ] **Rollen erzwingen** — die größte offene Lücke, und keine kosmetische:
      heute hält allein der 403 die Grenze, und nur dort, wo jemand daran
      gedacht hat
- [ ] **`assessment-service`**: ADR schreiben, bevor Code entsteht
- [ ] **`scout-service`** nach ADR-0036, **`advisor-service`** nach ADR-0037

---

## Wo die Grok-Sitzung liegt

`~/.grok/sessions/%2FUsers%2Fdavidozturk%2FProjects%2Fworkertransfer/`

Die jüngste ist `01a0783d-255f-7002-9a91-3393cacf325f` (106 MB, 9502
Nachrichten). Lesbar sind:

| Datei | Inhalt |
|---|---|
| `summary.json` | Titel, Modell, Zusammenfassung, HEAD-Commit |
| `plan.md` | der Arbeitsplan der Sitzung |
| `chat_history.jsonl` | nur der **letzte** Zug, nicht die Historie |
| `updates.jsonl` | 38 MB, der ganze Strom — `user_message_chunk` filtern |

Die 33 echten Nutzernachrichten daraus sind die Anforderungsgeschichte dieser
Runde: Streaming statt Chat, Vor- und Nachname im Briefkopf, Lebenslauf als
Datei *oder* Formular, MUI-Dialog statt `confirm()`, Schule und Ausbildung
getrennt, und zuletzt die tote Sitzung, die zur Startseite führen muss.
