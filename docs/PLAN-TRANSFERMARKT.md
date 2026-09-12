# Plan: der Transfermarkt für **alle** Arbeiter

**Stand:** 11.09.2026 · Begonnen nach dem Review in
[`REVIEW-09-09.md`](REVIEW-09-09.md); PBI-1 bis PBI-6 sind gefahren.

**Dies ist die Aufgabenliste.** Die acht Dokumente `AUFTRAG-*.md` und
`MIGRATION-AUFTRAG.md` daneben sind **Geschichte** — sie tragen seit dem
11.09.2026 oben eine Statuszeile, die das sagt. Wer offene Arbeit sucht, sucht
sie hier: PBI-7. Sonst nirgends — PBI-6 §6.5 ist am 12.09.2026 entschieden,
PBI-8 abgearbeitet bis auf die zwei Zeilen, die keine Codeaufgabe sind (8.2 ist
eine Entscheidung, 8.4 startet ein Mensch).

Dies ist der Plan bis zum Produkt, das der Name verspricht: ein Transfermarkt
wie im Fußball, **aber für Arbeiter** — und zwar für alle, nicht für
Softwareentwickler.

Jede Arbeit steht als **PBI** mit Nutzergeschichte, Aufgaben und
Abnahmekriterien. Die Reihenfolge und die fertigen Sitzungs-Prompts stehen in
[`SESSIONS.md`](SESSIONS.md).

---

## Der Befund, der alles ordnet

**WorkerTransfer ist heute eine Plattform für Softwareentwickler.** Das steht
nirgends und war nie entschieden — es ist die Summe von Einzelentscheidungen:

| Was | Wen es meint |
|---|---|
| `github-service` als einzige Belegquelle | Entwickler |
| Fähigkeiten als Freitext („Python, Kubernetes") | Entwickler |
| Die Häkchenliste über „genannte Fähigkeiten" | Entwickler |
| Profilüberschrift + Text als einziger Aushang | Wissensarbeit |

Ein Metallbauer, eine Elektronikerin, ein Pfleger haben **kein GitHub** — und
sie haben etwas anderes: **Zeugnisse, Zertifikate, Nachweise, Arbeitsproben**.
Davon ist das meiste schon gebaut (`Unterlage`, `portfolio-service`), es wird
nur nicht angeboten.

**Die Entscheidung, die daraus folgt:** ein Beleg ist *berufsabhängig*. GitHub
ist **eine** Quelle unter mehreren, nicht die Quelle. Das braucht ein ADR
(PBI-1), und daran hängt alles Weitere.

---

## PBI-1 — Berufsfeld beim Konto, und Belege danach

> **Als Metallbauer** will ich mich anmelden, ohne dass die Plattform mich nach
> einem GitHub-Konto fragt, **damit** ich meine Zeugnisse und Nachweise zeigen
> kann statt eines Repositoriums, das ich nie haben werde.

**Warum zuerst:** Scout und Berater suchen nach Belegen. Solange „Beleg" nur
GitHub heißt, sucht der Scout an drei Vierteln der Arbeitswelt vorbei.

### Aufgaben

- [x] **ADR-0039: Belege sind berufsabhängig.** Was ein Berufsfeld ist, welche
      Belegarten es zulässt, und warum GitHub dadurch *nicht* abgewertet wird.
      Die drei Herkunftsklassen aus ADR-0033 (genannt / belegt / vorgeschlagen)
      gelten unverändert — es kommen nur Quellen dazu.
- [x] `berufsfeld` auf `users` (nullable, aus einer **geschlossenen** Liste),
      gesetzt bei der Registrierung, änderbar in den Einstellungen
      (`PUT /account/occupational-field`, leer heisst entfernen).
- [x] Die Liste selbst: **kein Freitext.** Ein Feld, aus dem eine Navigation
      folgt, muss endlich sein. Vorschlag als Startpunkt (erweiterbar, jede
      Erweiterung ist eine Entscheidung):
      `handwerk` · `industrie_technik` · `bau` · `gesundheit_pflege` ·
      `logistik_verkehr` · `gastronomie_hotel` · `handel_verkauf` ·
      `buero_verwaltung` · `it_software` · `bildung_soziales` · `sonstiges`
- [x] **Belegarten je Feld**, als Tabelle im ADR und als Wert im Code
      (`web/src/shared/lib/berufsfelder.ts`; jedes Feld erbt die allgemeinen):
      | Feld | Belege, die zählen |
      |---|---|
      | `it_software` | GitHub, Portfolio, Zertifikate |
      | `handwerk`, `industrie_technik`, `bau` | Gesellenbrief, **Meisterbrief**, Schweißerpass, Staplerschein, Arbeitsproben (Fotos) |
      | `gesundheit_pflege` | Berufsurkunde, Fortbildungen, Führungszeugnis (**nie hochgeladen**, nur genannt) |
      | `logistik_verkehr` | Führerscheinklassen, ADR-Schein, Fahrerkarte |
      | alle | Arbeitszeugnisse, Zertifikate, Referenzen |
- [x] **Navigation folgt dem Feld**: `/github` erscheint nur bei `it_software`.
      Die Route bleibt erreichbar (wer sie kennt, darf sie nutzen) — sie wird
      nur nicht angeboten. Verstecken ist keine Zugriffskontrolle.
- [x] Registrierung: ein Auswahlfeld, **kein Pflichtfeld**. Wer nichts wählt,
      bekommt die neutrale Ansicht. Ein Pflichtfeld an der Anmeldung ist eine
      Hürde vor dem ersten Nutzen.
- [x] Der Wortschatz (`WorkerTransfer.Skills`) bekommt die Handwerksbegriffe:
      `MIG/MAG`, `WIG`, `CNC`, `SPS`, `Hubwagen`, `Gerüstbau`, … Die Regel aus
      ADR-0023 gilt unverändert: **benennt um, folgert nie.**

### Abnahme — erfüllt (10.09.2026)

- Ein Konto mit `handwerk` sieht **kein** GitHub in der Navigation, und die
  Profilseite bietet stattdessen Nachweise an. — `SiteHeader.test.tsx` fährt
  die drei Fälle, `e2e/occupational-field-journey.spec.ts` die ganze Naht.
- Ein Konto ohne Berufsfeld sieht die heutige Ansicht — nichts wird schlechter.
  Die Spalte ist nullbar ohne Vorgabe, es gibt **kein** Wanderungsskript, und
  nichts wird geraten: eine GitHub-Verbindung macht niemanden zu `it_software`.
- `Skills`-Test: ein Handwerksbegriff wird kanonisiert, **keiner** wird
  gefolgert — `Aus_MIG_MAG_folgt_kein_Schweissen` und
  `Ein_Schein_ist_keine_Maschine` stehen neben `Aus_React_folgt_kein_JavaScript`.
- Drei Kataloge, drei Sprachen.

**Gegenproben gefahren**, alle vier fielen und keine war ein Übersetzungsfehler:
`zeigtGitHub` auf „immer" → die Handwerk-Reihe fällt; `nurGenannt` vom
Führungszeugnis genommen → die Belegreihe fällt; die 422-Prüfung am Endpunkt
entfernt → `Ein_unbekanntes_Feld_wird_abgesagt` fällt; `SaveAsync` im Befehl
weggelassen → `Nachtragen_und_zuruecknehmen_wirken_beide` fällt.

---

## PBI-2 — Rollen erzwingen

> **Als Inhaberin eines Unternehmens** will ich, dass ein `member` meine
> Stellen nicht löschen kann, **damit** „admin" mehr ist als ein Wort in einer
> Tabelle.

**Die größte echte Lücke im Baum.** Heute: **null** `RequirePermission`. Die
Navigation *versteckt* Firmeneinträge; der Server antwortet 403 nur dort, wo
jemand daran gedacht hat. `Mitgliedschaftsrecht` liest die Rolle je Anfrage —
aber niemand fragt.

### Aufgaben

- [x] Jede Firmen-Route durchgegangen. Die **Linie**, einmal aufgeschrieben,
      damit die nächste Route sie nicht neu erfindet: `admin` ist, wer das
      Unternehmen **bindet oder ändert**, wer dazugehört. `member` ist die
      tägliche Arbeit im Namen der Firma — lesen, entwerfen, schreiben,
      ansprechen, Bewerbungen bearbeiten. **Im Zweifel `member`:** ein zu enges
      Recht macht aus einer Einladung eine Zuschauerkarte, und dann legt jemand
      einen zweiten Admin an, um arbeiten zu können — dann ist „admin" wieder
      ein Wort in einer Tabelle.

      | Route | Recht |
      |---|---|
      | `POST /companies/{id}/invitations` | `company.invite` |
      | `DELETE /companies/{id}/invitations/{id}` | `company.invitations.withdraw` |
      | `DELETE /companies/{id}/members/{id}` | `company.members.remove` |
      | `POST /jobs/{id}/publish` | `jobs.publish` |
      | `POST /jobs/{id}/close` | `jobs.close` |
      | `PUT /companies/me/profile` | `company.profile.write` |
      | `POST /transfers/{id}/offer` | `transfer.offer` |
      | `POST /transfers/{id}/complete` | `transfer.complete` |

      Drei Entscheidungen, die auch anders hätten ausfallen können, und warum
      nicht: `POST /jobs` und `PUT /jobs/{id}` bleiben beim Mitglied (ein
      Entwurf steht niemandem gegenüber — die Linie liegt am Aushang, nicht am
      Text); `POST /transfers/{id}/withdraw` ebenso (wer anfangen darf, muss
      aufhören dürfen, sonst ist die Einladung eine Falle); `GET
      /companies/{id}/members` und `GET .../invitations` ebenso (eine Firma muss
      ihrer eigenen Belegschaft nicht verschweigen, wer dazugehört und wen sie
      sucht).
- [x] Die Richtlinien hängen an den Endpunkten (`Permission:*`, aufgelöst von
      Girders `PermissionPolicyProvider` — derselbe Name, den
      `[RequirePermission]` setzt; geschrieben als `RequireAuthorization`, wie
      die zwei, die es schon gab).
- [x] **Die Rolle kommt aus der Mitgliedschaftstabelle, je Anfrage.** Sie liegt
      in identity-service und nirgends sonst (ADR-0004), also fragen die anderen
      Dienste über `GET /internal/companies/{id}/members/{sub}/role` hinter dem
      gemeinsamen Geheimnis. `ServiceDefaults.Rollen` trägt den Mechanismus, die
      **Liste** steht je Dienst im eigenen `Program.cs` — der Mechanismus darf
      nicht elfmal beantwortet werden, die Liste ist eine Entscheidung.
      **Kein Zwischenspeicher**, aus demselben Grund wie beim Ledger (ADR-0013):
      wer entfernt wird, ist bei der nächsten Anfrage draußen.
- [x] **Ein Schweigen der Rollenauskunft ist 503, nie 403.** Ein
      Autorisierungshandler kann nur „ja" sagen; er hinterlässt deshalb eine
      Notiz am `HttpContext`, und `Ablehnungsgestalt` macht daraus 503. Ein 403
      läse sich als „dir wurde das Recht genommen", und niemand suchte nach
      einem Ausfall.
- [x] `docs/routenkarte.yml` hat die vierte Spalte `mitglied`, und
      `scripts/routenkarte.sh` fährt sie. Das vierte Konto entsteht wie ein
      Mensch: registriert, mit `role: "member"` eingeladen, beigetreten. Das
      Skript **weigert sich zu messen**, wenn es nicht denselben Mandanten und
      die Rolle `member` trägt — ohne diese Prüfung mäße ein misslungener
      Beitritt still eine Person ohne Firma, und alle acht Zeilen sähen trotzdem
      grün aus, weil 403 dort auch für eine Person richtig ist.
- [x] Ein Test je geschützter Route: `member` 403, `admin` 200 — in derselben
      Reihe, denn ein Endpunkt, der IMMER 403 antwortet, ist von einem richtig
      geschützten nicht zu unterscheiden. `RollenTests` in jobs, companies und
      transfer; `UnternehmensreiseTests` in identity.

### Abnahme — erfüllt (11.09.2026)

- `make routenkarte` fährt **vier** Spalten, alle wie aufgeschrieben.
- **Gegenprobe gefahren:** `RequireAuthorization` an `POST /jobs/{id}/publish`
  entfernt → **drei** Reihen fielen
  (`Nur_ein_Administrator_veroeffentlicht`, `Gefragt_wird_nach_dem_Mandanten…`,
  `Eine_schweigende_Rollenauskunft…`). Sie **kompilierte** — ein Build-Fehler
  läse sich in der Ausgabe wie ein bestandener Test. Danach zurückgenommen und
  mit `--no-incremental` neu gebaut.
- Eine zweite Gegenprobe fiel nebenbei an und ist wertvoller als die erste:
  `ZeitlimitTests` suchte wörtlich nach `client.Timeout` und wurde rot an
  `HttpFirmenrollen`, das sein Zeitlimit an `klient` setzt — also an
  **richtigem** Code. Gesucht wird jetzt die Zuweisung `.Timeout =` statt eines
  Variablennamens. Ein Wächter, der bei richtigem Code rot wird, wird beim
  nächsten Mal weggeschaltet statt gelesen.

### Was PBI-2 NICHT getan hat

- **Die Rolle steht weiterhin nicht im Token.** Das wäre der kürzere Weg und
  der schlechtere: eine Entfernung wirkte dann erst beim Ablauf.
- **`GET`-Routen haben keine Richtlinie bekommen.** Was nicht in der Liste
  steht, darf jedes Mitglied — die Prüfung „handelst du für eine Firma?" steht
  schon am Endpunkt, und eine zweite Richtlinie daneben wäre eine zweite Stelle
  für dieselbe Frage.
- **Die Oberfläche versteckt weiterhin, und das ist jetzt nur noch Bequemlich-
  keit.** Niemand hat die Navigation angefasst; ein `member` sieht heute
  Firmeneinträge, die 403 antworten. Das ist die richtige Reihenfolge (erst der
  Server, dann die Anzeige), aber es ist Arbeit, die noch aussteht.

---

## PBI-3 — `scout-service`

> **Als Unternehmen** will ich eine Anforderung stellen und Menschen sehen, die
> sie erfüllen — **mit Häkchen und Belegen, nie mit einer Zahl.**

ADR-0036 ist **angenommen**. **Gebaut am 11.09.2026.**

### Aufgaben

- [x] Neuer Dienst nach dem Muster der elf anderen (`Api`/`Application`/
      `Domain`/`Infrastructure`/`Contracts`, eigene Datenbank, `AddGirder`).
      Hafen 8012, Route `/scout/{rest}` im Gateway, fünf Zeilen in der
      Routenkarte — gegen den laufenden Stapel gefahren.
- [x] `Suche` (Aggregat): **fünf Filter genannt, fünf beantwortet** — drei
      gebaut, zwei als Entscheidung. Diese Zeile stand einmal abgehakt hier,
      mit zwei Fehlenden in einem Klammersatz und der Verfügbarkeit gar nicht
      erwähnt. So sieht sie ehrlich aus:

      | genannt | Stand |
      |---|---|
      | Fähigkeiten | **gebaut** (ODER, mit Häkchenliste) |
      | Ort | **gebaut** (Teiltext) |
      | Remote | **gebaut** — stand nicht in dieser Liste und ist trotzdem da |
      | Umkreis | **gebaut, aber nicht als Filter** — ADR-0041: Stufe am Profil, Anwesenheit an der Anzeige, daraus ein Häkchen |
      | Berufsfeld | **wird nie gebaut** — ADR-0036 §6 |
      | Verfügbarkeit | **wird nicht gebaut** — ADR-0036 §7 |

      **Gespeichert wird die Anfrage, nie das Ergebnis** — die Tabelle
      `searches` hat keine Spalte für einen Treffer, und eine Reihe misst das
      an der gespeicherten Zeile. Und für ADR-0041 entstand **keine
      Koordinatenspalte**: der Ort wird zur Suchzeit aufgelöst.
- [x] Die Treffer kommen aus profile-service, über eine interne Tür hinter dem
      gemeinsamen Geheimnis. Die harten Teile sind **mitgenommen**: Ledger je
      Zeile über `/check-batch`, keine Gesamtzahl, kein Auffüllen, Firmenzwang.
- [x] Belege werden zum Treffer **dazugeholt**, nie zum **Finden** benutzt —
      und erst NACH der Freigabe: über wen nichts freigegeben ist, wird auch
      nichts nachgeschlagen.
- [x] Die Nachricht „dein Profil wurde entdeckt" (ADR-0033): eigene Art,
      nennt kein Unternehmen, über den Postausgang, höchstens eine je Person
      und Tag — und die Kappe nimmt den **Postfacheintrag** mit, nicht nur die
      Mail.
- [x] Löschempfänger ab der ersten Tabelle: `"scout"` steht in
      `Loeschempfaenger.Fremde` und in `LoeschempfaengerTests.Dienste`.

### Abnahme — die vier Auflagen als Tests

Alle vier stehen in `AuflagenTests`, und **jede hat eine Gegenprobe, die
gemessen gefallen ist**:

- [x] Keine Sortierung nach Passung. Gegenprobe: ein `OrderByDescending` über
      die gesetzten Häkchen → rot.
- [x] Keine Zahl: `Adr0022Tests`-Muster über Domäne und Verträge, plus die
      geschlossene Feldmenge von `TrefferV1`. Gegenprobe: ein Feld `fit` → rot.
- [x] Nur Genanntes ist durchsuchbar — ein nur *belegtes* Wort findet
      niemanden, dieselbe Person ist über ihr genanntes Wort sehr wohl zu
      finden, und der Beleg taucht dann am Treffer auf. Gegenprobe: Belege vor
      der Freigabe holen → rot.
- [x] Die Ansprache ist ein **Entwurf**; der Dienst schreibt niemandem.
      Gegenprobe: ein `IVersand` in der Anwendungsschicht → rot.

### Der Umzug der Oberfläche

- [x] `/scout` statt `/candidates`: Seite, Karte, Navigation und sechs
      E2E-Reisen. Die Häkchen und die Belege kommen aus dem Vertrag und werden
      **nicht** im Browser gerechnet (ADR-0036 Entscheidung 3).
- [x] `GET /candidates` ist **gefallen** — Endpunkt, Gateway-Route, Client. Die
      Routenkarte hält den Pfad als tote Tür fest: 404 in allen vier Spalten,
      und `LandkarteTests` misst nach, dass wirklich keine Route mehr
      dahintersteht.
- [x] Mit ihm fielen der UND-Zweig im Profilspeicher und die Sammelfrage in
      profile-service' Einwilligungstor: beide hatten keinen Aufrufer mehr, und
      ein Zweig ohne Aufrufer ist das, was später falsch wiederbelebt wird.
- [x] Nebenbei behoben: die Liste las `next_cursor`, der Server schrieb `next`.
      Der Zeiger war damit immer `null`, „mehr laden" erschien nie, und es gab
      **keinen Weg auf Seite 2** — die Lücke, die
      `SCOUT-UND-BERATER-BESTAND.md` notiert hat. Die Ursache war ein Feldname.

### ADR-0041 — Entfernung als Häkchen

- [x] **Pendelbereitschaft** und **Umzugsbereitschaft** am Profil: zwei Stufen,
      beide freiwillig, beide nullbar. Eine Wanderung mit zwei nullbaren
      Spalten — kein Nachtragen an bestehenden Zeilen, weil ein Profil ohne
      diese Angabe vollständig ist.
- [x] **Die Anwesenheit an der Anzeige gab es schon.** ADR-0041 §2 verlangte ein
      Feld `remote / hybrid / vor_ort`; jobs-service führt es seit jeher als
      `Remotegrad` (`none` / `hybrid` / `full`) — mit derselben Begründung im
      Quelltext: *„hybrid ist der häufigste Fall und keine Zwischenstufe von
      wahr."* Es entstand deshalb **kein zweites Feld**: das wäre eine zweite
      Wahrheit über dieselbe Frage gewesen.
- [x] **Das Häkchen im Scout**, drei Zustände, **keine Kilometerzahl** — sie
      steht in keinem Feld und auf keinem Draht. `?stelle=<id>` **filtert
      nicht**: wer weiter weg wohnt, bleibt in der Liste und trägt ein Kreuz.
- [x] **Nichts Neues gespeichert**: `Ortskunde` löst den vorhandenen
      Freitext-Ort zur Suchzeit auf, im Arbeitsspeicher, für die Dauer einer
      Antwort.
- [x] Die Oberfläche: zwei Auswahlfelder am Profil, eine Stellenauswahl im
      Scout, ein Häkchen auf der Karte — in drei Sprachen.

### Was offen bleibt

Nichts an PBI-3.

---

## PBI-4 — `advisor-service`

> **Als Arbeiter mit laufendem Vertrag** will ich einen Berater, der mein
> Mandat kennt, **damit** ich meinen Eintrittstermin nicht dreimal sagen muss
> und mein jetziger Arbeitgeber nichts erfährt.

ADR-0037 ist **angenommen**. **Gebaut am 11.09.2026.**

### Aufgaben

- [x] Neuer Dienst nach dem Muster der zwölf anderen (`Api`/`Application`/
      `Domain`/`Infrastructure`/`Contracts`, eigene Datenbank, `AddGirder`).
      Hafen 8013, Route `/advisor/{rest}` im Gateway, **zwölf** Zeilen in der
      Routenkarte — gegen den laufenden Stapel gefahren (624 Antworten, alle
      wie aufgeschrieben).
- [x] **Das Mandat ist eine Sicht**, kein zweiter Speicher: Sichtbarkeit lebt
      im Ledger, Verfügbarkeit im Marktstatus (ADR-0020 verbietet die Kopie).
      Eigen sind nur: Eintrittstermin, Gehaltsspanne, Pensum, ausgeschlossene
      Unternehmen. `AuflagenTests` liest das **EF-Modell** und geht rot, sobald
      eine Spalte `sichtbar`, `visible`, `public`, `freigabe`, `stage` oder
      `stufe` im Namen trägt — die letzten zwei Worte sind die schärfere
      Hälfte: eine Stufenspalte wäre die Sichtbarkeit als zweite Tür.
- [x] Gespräche in drei Stufen, jede Stufe eine Freigabe der Person. Es sind
      die **bestehenden** Fähigkeiten und keine drei neuen; neu ist genau eine,
      `advisor.identity:tenant:<uuid>`, und sie trägt **keine Ziffer** —
      `advisor.stage1` würde der Parser ablehnen.
- [x] Was in einer Stufe nicht frei ist, **existiert für die Gegenseite nicht**:
      das Feld fehlt im JSON, es ist nicht `null`. Und es fehlt **genauso**,
      wenn die Stufe frei ist und die Person nichts eingetragen hat. Ein
      Gespräch auf Stufe 0 fällt ganz aus der Firmenliste.
- [x] Einigung → Übergabe an `transfer-service`. Der Dreieckskonsens wird
      **nicht** nachgebaut: `hand-over` ruft dessen `POST /transfers` mit dem
      Token des Unternehmens, und ein `AuflagenTests`-Wächter geht rot, sobald
      ein Wort wie `ansprechbar`, `marktstatus` oder `brauchtfreigabe` in
      Domäne oder Anwendungsschicht auftaucht.
- [x] Löschempfänger ab der ersten Tabelle: `"advisor"` steht in
      `Loeschempfaenger.Fremde` und in `LoeschempfaengerTests.Dienste`. Die
      Kaskade schreibt jetzt **zwölf** Absichten statt elf.
- [x] Die Oberfläche: `/advisor` für die Person (Mandat und Stufen),
      `/company/advisor` für das Unternehmen, ein „Gespräch eröffnen" auf der
      Scout-Karte — in drei Sprachen.

### Abnahme — erfüllt (11.09.2026)

- **Der jetzige Arbeitgeber sieht die eigene Belegschaft nicht im Scout** — und
  zwar über den Ledger, nicht über ein zweites Tor im Scout. Der Ledger kennt
  keine Verneinung, also gibt es den Modus „alle" nicht mehr, sobald jemand ein
  Unternehmen ausschliesst: `MandatSchreibenHandler` widerruft
  `profile.visibility:public` im selben Schritt. Gemessen am laufenden Stapel:
  die Reise `advisor-journey` trägt eine Domain ein und findet den Schalter auf
  der Profilseite danach **aus**.
- **Eine Stufenfreigabe wirkt sofort, eine Rücknahme ebenso.** Dieselbe Reise
  fährt 1 → 2 → 3 und liest jedes Mal auf der Firmenseite nach; dann zurück auf
  2 (Klarname weg, Spanne bleibt) und ganz zurück (das Gespräch fällt aus der
  Liste).

**Gegenproben gefahren**, alle drei fielen und keine war ein Übersetzungsfehler:
den Widerruf von `profile.visibility:public` abgeschaltet → zwei Reihen fielen;
die Stufengrenzen in `Gespraechsansicht.Baue` entfernt → drei; eine
`stage`-Spalte ins EF-Modell gelegt → `Keine_Spalte_haelt_eine_Sichtbarkeit`.
Danach zurückgenommen und mit `--no-incremental` neu gebaut.

### Was die Reise gefunden hat, und was kein Test gefunden hätte

„Alles zurückziehen" stand als Beschriftung am Rücknahme-Knopf, und es war
**zu viel versprochen**: wer sein Profil auf „für alle Unternehmen" gestellt
hat, bleibt darüber sichtbar, und die Stufe fällt dann nicht auf 0. Das
Verhalten ist richtig — ein Knopf in *einem* Gespräch darf keinen
plattformweiten Schalter umlegen —, aber die Beschriftung log. Sie heisst jetzt
„Freigabe zurücknehmen" bzw. „Auf Stufe N zurück", und der Hinweis darunter
sagt, wo der andere Schalter steht. Gefunden hat das die E2E-Reise, weil nur sie
beide Seiten zugleich fährt.

---

## PBI-5 — `assessment-service`

> **Als Unternehmen** will ich eine Arbeitsprobe stellen, **damit** ich sehe,
> wie jemand arbeitet — ohne daraus eine Note über einen Menschen zu machen.

**Gebaut am 11.09.2026** — [ADR-0042](adr/0042-die-arbeitsprobe.md). Der dritte
aus `SCOUT-UND-BERATER.md` und der mit dem größten Missbrauchspotenzial:
unbezahlte Arbeit als Aufgabe getarnt.

**Die Nummer ist 0042 und nicht 0040.** Dieser Abschnitt sagte „ADR-0040 ist
vergeben" und meinte damit die *nächste* freie; beim Schreiben war auch 0041
vergeben (Entfernung). Dieselbe Entscheidung, die nächste freie Nummer.

### Aufgaben

- [x] **Ein eigenes ADR zuerst.** Drei Regeln, die den Unterschied zwischen einer
      Aufgabe und einer Prüfung mit Note ausmachen:
      1. Die Bewertung gehört dem **Vorgang**, nicht dem Menschen — in keiner
         Suche, in keinem Profil, für kein anderes Unternehmen, ohne Zahl.
      2. Die Person **sieht** die Bewertung. Immer, auch bei Absage.
      3. Der **Umfang in Stunden steht in der Ausschreibung**, und Ablehnen
         wird nirgends vermerkt.
- [x] Danach der Dienst. Jede der drei Regeln ist ein Mechanismus und kein
      Absatz: EIN Bewertungsfeld, eine byte-gleiche Antwort an beide Seiten,
      ein Ledger, der nur die Firmenseite bewacht, Pflichtangabe 1–8 Stunden,
      und keine Route zum Ablehnen — die Endpunktmenge ist geschlossen und
      steht als Test.

---

## PBI-6 — Aufräumen, was das Review offen ließ

> **Der Abschnitt stimmte selbst nicht**, und das war der größte Posten daran.
> Er nannte vier Punkte; zwei waren längst erledigt, einer ist keine Codeaufgabe
> und der wichtigste fehlte ganz. So sieht er nach der Nachmessung vom
> 11.09.2026 aus.

### 6.1 Die Auftragsdokumente logen — der größte Posten, und er fehlte hier

`docs/AUFTRAG-OPENCODE.md` stand mit **39 offenen Kästchen** da,
`docs/AUFTRAG-BEWERBUNG-UND-SCOUT.md` mit **20**. Gemessen am 11.09.2026 gegen
den Baum: **58 der 59 waren gebaut.** Eine Stichprobe von sechs traf sechsmal
daneben — scout-service, advisor-service, `ApplicationReceived`, die
Mehrfachauswahl auf `/jobs`, die Entwurfsliste, die Prüfansicht: alles da.

Das ist schlimmer als eine veraltete Notiz. Eine veraltete Prosa liest sich wie
Geschichte; **ein offenes Kästchen liest sich wie ein Auftrag**, und die nächste
Sitzung fängt an, etwas zum zweiten Mal zu bauen. Genau das ist die Verwechslung,
die ein Auftragsdokument von einem Plan trennt: **ein Auftrag ist Geschichte, der
Plan ist die Aufgabenliste.**

- [x] Alle **acht** Auftragsdokumente (`docs/AUFTRAG-*.md` und
      `docs/MIGRATION-AUFTRAG.md`) tragen oben eine Statuszeile: abgeschlossen
      am X · was offen blieb · wo das Ergebnis heute steht.
- [x] Jedes Kästchen einzeln gegen den Baum geprüft — abgehakt, was gebaut ist;
      offen gelassen, was wirklich offen ist. **Nichts gelöscht:** die
      Begründungen darin sind der Wert, nur die Kästchen logen.
- [x] Was wirklich offen ist, steht jetzt **hier** und nicht dort. Es war
      wenig, und es war genau dieses:

      | aus | offen | wohin |
      |---|---|---|
      | OPENCODE 6.1 / BEWERBUNG „aus JobPilot übernommen" | **begrenzte Parallelität und sichtbarer Fortschritt beim Erzeugen von Entwürfen** | PBI-8 unten |
      | HAERTUNG H2 | die Geheimnisfrage (`AddSecretManagement`, `AddEncryption`, Infisical daneben oder an ihrer Stelle) | PBI-8 unten |
      | HAERTUNG H5 | `/code-review ultra` über WorkerTransfer, Girder und Skillswap | **startet ein Mensch**, kein Agent |
      | ENTLASTUNG Phase 4 | die Kommentardichte | 6.5 unten — entschieden am 12.09.2026 |

### 6.2 Fund 7 — das tote Bewerbungsformular

- [x] **Gelöscht.** `web/src/features/work/pages/JobApplyPage.tsx` waren 300
      Zeilen, und der Grund, warum das Formular tot war, ist lehrreicher als
      seine Länge: der Umleitungszweig stand **davor**. Für eine angemeldete
      Person mit gültiger Stellenkennung legte ein `useEffect` einen Entwurf an
      und leitete nach `/applications/drafts/{id}`; die Seite gab danach nur
      noch eine Ladekarte zurück. Alles darunter — Anschreibenfeld, die zwei
      Freigabekästchen, `submit()`, `apply()` — war ab dieser Zeile
      unerreichbar. Es sah aus wie ein Formular und war ein Grabstein.
- [x] **Die Route ist mitgefallen.** `/jobs/:jobId/apply` war zuletzt eine
      *Weiche*: eine Adresse, die niemand sehen soll, mit einer Ansicht, die
      niemand sieht. Ihre zwei Aufrufer — die Stellenliste und die
      Karriereseite — rufen jetzt `features/work/lib/entwuerfe.ts`, und das
      Anlegen steht damit an **einer** Stelle statt an zweien.
- [x] **Die verwaisten Katalogschlüssel sind weg**: der ganze Block
      `bewerbung.*`, siebzehn Schlüssel in drei Sprachen. Kein einziger hatte
      nach der Löschung noch einen Leser.
- [x] `karriere.bewerben` ist jetzt ein **Knopf** statt eines Verweises, und er
      trägt beide Wege, die die Weiche trug: angemeldet legt er den Entwurf an,
      abgemeldet merkt er die Stelle und schickt zur Anmeldung.

**Was dabei auffiel und NICHT angefasst wurde** (es ist eine eigene
Entscheidung, siehe PBI-8): `merkeStelle` schreibt die gemerkte Stelle nach
`localStorage`, und **niemand liest sie zurück**. `gemerkteStelle()` hat außer
in der eigenen Datei keinen Aufrufer; `LoginPage.tsx:66` hat den Rückweg
ausdrücklich stillgelegt und den Satz hinterlassen, wie man ihn zurückholt. Das
ist kein toter Code aus Versehen, sondern ein halb geparkter Mechanismus — aber
der Kommentar in `JobsPage.tsx` behauptet dabei, sein Knopf sei „die EINZIGE
Stelle, an der die Absicht entsteht", und das stimmte schon vorher nicht.

### 6.3 `GET /notifications` → 405

- [x] **War schon behoben, in H4** — dieser Punkt stand hier, ohne dass jemand
      nachgesehen hatte. Der Diensteingang ist nach `/internal/notifications`
      umgezogen, wo es keine Gateway-Route gibt; damit verhalten sich alle drei
      Dienst-zu-Dienst-Türen gleich (`/erasure`, `/internal/notify`,
      `/internal/notifications`). `/notifications` antwortet jetzt auf **jede**
      Methode 404, nicht unterscheidbar von einem Pfad, den es nicht gibt.
      `docs/routenkarte.yml:536` erklärt es bereits im Imperfekt („**STAND**
      hier mit 405"), und die Zeilen 497 und 561 messen 404 in allen vier
      Spalten. Nichts zu tun — nur nachzusehen.

### 6.4 `make k8s-up` einmal wirklich fahren

- [x] **Gefahren, am 11.09.2026, zum ersten Mal auf dieser Maschine.** Und der
      Befund ist der bestmögliche: **das Chart trägt, das Skript log.**

**Was sofort funktionierte**, ohne eine einzige Änderung: kind-Cluster,
beide Images, `helm upgrade --install --wait`, und danach **19 Pods bereit, null
Neustarts**. Postgres, Mailpit, Jaeger, das Gateway, alle vierzehn Dienste und
`web`. Die Migrationen liefen; die Registrierung legte ein Konto an und die
Bestätigungsmail lag in Mailpit.

**Was fiel, waren die BEWEISE — dreimal, und jedes Mal, weil das Skript gegen
einen Baum geschrieben war, den es nicht mehr gibt.** Das ist genau der Fall,
den `CLAUDE.md` über die Routenkarte festhält: *eine Liste, die niemand fährt,
ist am Tag nach ihrer Entstehung falsch.* Ein Beweis, den niemand fährt, auch.

| fiel bei | behauptet | gemessen | warum |
|---|---|---|---|
| Beweis 2 | `GET /jobs` → **401** | **200** | die Stellenliste ist öffentlich geworden. Mit ihr fiel der Beleg, der an ihr hing: das RFC-9457-Dokument mit `correlationId`. |
| Beweis 2 | `GET /` → die Oberfläche | **404** | ADR-0040: das Gateway liefert keine Oberfläche mehr, `web` ist `ClusterIP`. Das ist richtig so, und das ADR sagt es selbst. |
| Beweis 2b | Direktlink mit `Sec-Fetch-Dest: document` | — | prüfte die `Navigation`-Zwischenschicht, die mit ADR-0040 **gelöscht** wurde. Ein Beweis für etwas, das es nicht gibt, kann nur rot werden. |
| Beweis 3 | `POST /auth/register` → **201** | **422 `invalid: display_name`** | das Skript schickte `displayName`, der Vertrag liest `display_name`. |

**Der vierte ist der lehrreichste, und er ist ein alter Bekannter.** camelCase
gegen snake_case kommt nicht *falsch* an — es kommt **gar nicht** an: der Wert
ist beim Empfänger leer, und die Antwort beanstandet ein Feld, das man
geschickt zu haben glaubt. Dieselbe Naht hat den Benachrichtigungsweg schon
einmal viermal still fallen lassen (`CLAUDE.md`, „Konventionen, die beißen").
Hier fand sie ein Skript, weil es endlich jemand fuhr.

- [x] **`scripts/k8s-up.sh` steht jetzt auf dem Gemessenen**, und die drei
      Beweise sagen wieder etwas:
      - **Beweis 2** fragt **zwei verschiedene Dienste** statt Dienst und
        Oberfläche: `GET /jobs` → 200 mit der Seitengestalt (`items`) von
        jobs-service, `GET /consent/me` → 401 mit einem Problemdokument samt
        `correlationId` von consent-service. Zwei Ziele, zwei **Gestalten** —
        und das ist der eigentliche Beleg: eine fehlende Route wäre Ocelots
        leeres 404, ein toter Dienst ein 502, und beides sähe an einem
        einzelnen Statuscode gleich aus. Dieselbe Wahl wie im `images`-Auftrag
        der CI, aus demselben Grund.
      - **Beweis 2b** ist gefallen, mit dem Grund im Quelltext.
      - **Beweis 3** schickt `display_name`, und darüber steht, warum.
      - Der Schlusstext sagt jetzt geradeheraus, dass es **keine Oberfläche**
        gibt und wer sie will, `web` einen eigenen Eingang gibt.
- [x] **Danach grün, Ende zu Ende** — und dreimal hintereinander gefahren, denn
      ein Skript, das nur beim ersten Mal durchläuft, ist kein Beweis.

**Und ein Widerspruch, der dabei auffiel:**
`docs/prompts-naechste-schritte.md` (gelöscht am 12.09.2026) meldete
`make k8s-up` seit dem 08.08.2026 als eingelöst — *„15 Pods bereit, null Neustarts, `GET /jobs` → 200,
`POST /auth/register` → 201 samt Mail"* —, während `CLAUDE.md` sagt, es sei nie
gelaufen. **Beides stimmt:** der Lauf war in der Python-Ära, und das Skript
wurde seither neu geschrieben. Wer die Zeile liest und nicht das Datum, hält
den Punkt für erledigt. Genau dafür steht sie jetzt in der Liste unten.

### 6.5 Kommentardichte — die Entscheidung ist getroffen (12.09.2026)

> **Ein Kommentar verdient seinen Platz, wenn sein Fehlen jemanden einen Defekt
> NEU EINBAUEN ließe. Alles andere erzählt den Code nach.**
>
> Das ist die Grenze. Sie steht wortgleich in `CLAUDE.md` bei den Konventionen
> und ausführlich in [`AUFTRAG-ENTLASTUNG.md`](AUFTRAG-ENTLASTUNG.md) Phase 4,
> die damit **geschlossen** ist. **Weg 2 ist abgesagt, nicht vertagt.** Weg 1
> wurde selektiv gefahren: von den sieben wortgleichen `Personenzeile`-Kopien
> fielen **drei** — die drei, die `Personenzeile` selbst beim Namen nennt und
> neben denen keine Schwestertabelle mit eigener `subject_id` steht. Die
> anderen vier bleiben und sagen jetzt auch, wovor sie warnen.

Dieser Punkt stand hier als Aufgabe und war keine. Er war eine
**Entscheidung**, und sie gehörte einem Menschen. Die Messung, die sie getragen
hat: Neu vermessen am 11.09.2026 über `src/`, ohne
`obj/`, `bin/` und Wanderungen:

| | |
|---|---|
| Dateien / Zeilen | 539 / 51.996 |
| **Kommentarzeilen** | **16.157 — 31 %** |
| davon `///` (XML) · `//` | 13.697 · 2.373 |
| `<summary>` · `<remarks>` | 2.967 · 1.108 |
| **Zeilen innerhalb `<remarks>`** | **8.748 — 17 % des Baums, 54 % aller Kommentare** |

Die Dichte ist seit dem 03.09.2026 **gestiegen** (drei Dienste kamen dazu). Sie
ist aber nicht der Gegenstand. Der Befund von damals hält jeder Nachmessung
stand: **in dieser Codebasis sind die Kommentare überwiegend die Begründung.**
Der mechanische Schnitt wurde dreimal verschieden scharf gefahren, und der
schärfste entfernte unter anderem die Sätze, die erklären, *warum 503 und nicht
404* — die Kernregel aus ADR-0020. Sie tragen keines der Merkmale, weil sie als
**Argument** geschrieben sind („404 hieße zu behaupten…") und nicht als Befehl.

**Was zu entscheiden ist, in einem Satz:** wo genau verläuft die Grenze
zwischen einem Kommentar, der eine *Messung, ein Verbot oder eine Falle*
festhält — der Wert dieses Baums —, und einem, der den Code *nacherzählt*.

Die zwei Wege, die dranhängen, brauchen einander nicht:

- **Weg 1 — die Kopien an Aufrufstellen löschen.** Klein, sicher, kein Verlust:
  derselbe Begründungssatz steht mehrfach wortgleich im Baum, obwohl der
  kanonische an der Sache selbst hängt. Gezählt: „Konfiguration aus der
  Umgebung. VOR CreateBuilder…" in **15** `Program.cs` (45 Zeilen), „Der
  Schluessel IST der Mensch…" in **7** Kontexten (~35), „Kein AlsAussteller()"
  in **12** (~24), „Erst wandern, dann bedienen (ADR-0010)" in **15** (15).
  Rund **120 Zeilen**. Die letzte ist der Grenzfall und gehört ausdrücklich
  entschieden: sie ist keine Kopie einer Begründung, sondern ein *Zeiger* auf
  eine — und ein Zeiger dort, wo man ihn braucht, ist billig.
- **Weg 2 — `<remarks>` aufgeben und die Gründe in die ADRs ziehen.** 8.748
  Zeilen. Dann liegt der Grund an EINER Stelle — aber nicht mehr dort, wo
  jemand ihn braucht, sondern dort, wo er ihn suchen muss. Und es ist nicht
  umkehrbar: was einmal in ein ADR gewandert ist, wandert nicht zurück an die
  Zeile.

- [x] **Die Entscheidung ist getroffen** (12.09.2026) und steht oben. Ein Agent
      hat sie nicht getroffen, sondern festgeschrieben — in `CLAUDE.md`, in
      `AUFTRAG-ENTLASTUNG.md` und, je Stelle entschieden, im Code.

### 6.6 Was sonst veraltet ist

- [x] **`REVIEW-09-09.md` nannte 966 .NET-Tests** (und 150 Frontend-Tests in 20
      Dateien). Nachgemessen am 11.09.2026 stehen daneben die heutigen Zahlen —
      die alten bleiben stehen, denn ein Review ist ein Datum.
- [x] **`bugs/` ist sauber**, und das ist bestätigt statt angenommen: ein
      Eintrag (`korrelationskennung-steht-nicht-auf-der-konsole.md`),
      geschlossen am 09.09.2026 und auf Girder 4.4.0 nachgemessen. Nichts zu
      tun.
- [x] **Die 37 Dateien in `docs/` durchgesehen**, und am 12.09.2026 sind die
      **vierzehn** aus den beiden ersten Gruppen gelöscht. Die Liste steht unten
      in [„Was in `docs/` einen verschwundenen Zustand beschrieb"](#was-in-docs-einen-verschwundenen-zustand-beschrieb)
      und ist damit ein Grabstein statt einer Bestandsliste. Git behält die
      Dateien, das Löschen ist umkehrbar; das Stehenlassen war es nicht, denn
      jede von ihnen behauptete **Gegenwart**.

---

## Was in `docs/` einen verschwundenen Zustand beschrieb

Durchgesehen am 11.09.2026, alle 37 Dateien in `docs/` (ohne `adr/`, `vision/`,
`uebergabe/`, `skills/`, `superpowers/`). **Am 12.09.2026 sind die vierzehn aus
den beiden ersten Gruppen gelöscht**; die Spalte rechts sagt, weshalb. Sie steht
weiter hier, weil sonst niemand mehr wüsste, was in der Geschichte liegt und
warum es dort liegt.

Mitgelöscht wurden die **Verweise** darauf: ein toter Link ist schlimmer als
eine veraltete Datei, weil er beim Lesen erst am Ziel scheitert. Betroffen waren
zwei ADRs (0021, 0022), `MIGRATION-STAND.md`, `MIGRATION-AUFTRAG.md`,
`vision/README.md`, `.opencode/skill/README.md`, elf Spezifikationen unter
`docs/superpowers/` und **zwei Quelldateien** in identity-service, deren
`<remarks>` auf `docs/MIGRATION-PROMPT.md` zeigten. Die letzten beiden zeigen
jetzt auf das, was wirklich trägt — die Regel in `CLAUDE.md` und die beiden
Tokenform-Reihen.

### Beschrieben einen Baum, den es nicht mehr gibt

| Datei | was nicht mehr stimmt |
|---|---|
| `dotnet-README.md` | *„Python liegt in `../apps` und `../packages`, das Frontend in `../apps/web`"* — alle drei sind weg. Auch die Pfade (`../docs/…`) stammen aus einem `dotnet/`-Unterordner, den es nicht mehr gibt. Die Datei beschreibt die Zeit, in der .NET und Python nebeneinander lagen. |
| `phase-2-prep.md` | 16.07.2026, Python, Zweig `phase-2-identity-tenancy` @ `aa805e2`. Der früheste Zustand im Ordner. |
| `oberflaeche-erwartete-ansichten.md` | *„Gelesen aus `apps/web/src/app.tsx`"*, 29 Routen der **alten** Oberfläche (TanStack Query/Router, `packages/ui`). `web/` wurde neu gebaut; die Routen heißen heute anders und sind mehr. |
| `oberflaeche-routenkarte.md` | dieselbe Ära, Schnitt E2.5 derselben Spezifikation. |
| `befund-e3a-bewerbung.md`, `-e3b-eigene-daten.md`, `-e3c-konto.md`, `-e3d-unternehmen.md`, `-e3e-markt-geruest.md` | fünf Befunde vom 12./13.08.2026 an der alten Oberfläche. |
| `befund-kandidatenliste-haengt.md` | ein Befund über `/candidates` — den Endpunkt **gibt es nicht mehr** (ADR-0036, gefallen am 11.09.2026). |

### Beschrieben einen Plan, den ein anderer abgelöst hat

| Datei | was nicht mehr stimmt |
|---|---|
| `ULTRAPLAN.md` | der Masterplan vor der Migration, auf `vision/kon.txt`. Seine Phasen sind nicht die Phasen dieses Baums. Abgelöst durch diesen Plan. |
| `ROADMAP.md` | der Pull-Through-Index zu `ULTRAPLAN.md`, 1.519 Zeilen. Hängt mit ihm. |
| `MIGRATION-PROMPT.md` | *„Einstieg für jede Sitzung, die an der Migration arbeitet"* — die Migration ist durch. |
| `prompts-naechste-schritte.md` | 08.08.2026. **Und hier liegt ein echter Widerspruch:** die Datei meldet `make k8s-up` als eingelöst (*„15 Pods bereit, null Neustarts, `GET /jobs` → 200, `POST /auth/register` → 201 samt Mail"*), während `CLAUDE.md` sagt, es sei auf dieser Maschine nie gelaufen. Beides kann stimmen — der Lauf war in der **Python**-Ära, und das Skript wurde seither neu geschrieben. Wer die Zeile liest und nicht das Datum, hält den Punkt für erledigt. |

### Sind Momentaufnahmen und altern deshalb von selbst

| Datei | |
|---|---|
| `UEBERGABE.md` | *„Branch `dotnet-migration` · nichts gepusht (kein Upstream)"*, 02.09.2026. |
| `KI-EINSATZ-PRUEFUNG.md` | eine Prüfung vom 02.09.2026, ausdrücklich **lesend** gemacht. |
| `ANALYSE-STAND-UND-LUECKEN.md` | eine Bestandsaufnahme vom 03.09.2026. |
| `REVIEW-09-09.md` | das Review vom 09.09.2026. |
| `GIRDER-ANPASSUNGEN.md` | 03.09.2026; sagt *„Alle sieben Tickets aus `bugs/` sind behoben und die Dateien deshalb gelöscht"* — seither kam eines dazu und wurde ebenfalls geschlossen. |

**Diese fünf sind kein Aufräumfall.** Eine Momentaufnahme mit Datum davor ist
kein veraltetes Dokument, sondern ein datiertes. Der Unterschied zu den beiden
Gruppen darüber ist, dass jene sich als **Gegenwart** lesen.

### Tragen und bleiben

`MIGRATION-STAND.md` · `architecture.md` · `product-scope.md` · `glossary.md` ·
`frontend.md` · `erkenntnisse-girder.md` · `routenkarte.yml` · `SESSIONS.md` ·
`PLAN-TRANSFERMARKT.md` · `SCOUT-UND-BERATER.md` (ausdrücklich als **Entwurf**
gekennzeichnet) · `SCOUT-UND-BERATER-BESTAND.md` · die acht Auftragsdokumente
(jetzt mit Statuszeile).


---

## Die KI-Frage: LangChain, LangGraph, RAG?

**Empfehlung: beim dünnen Port bleiben. Und hier ist der Grund, nicht die
Meinung.**

### Was heute steht

`IAnschreiber` ist ein Port; `HttpAnschreiber` sind **520 Zeilen**, die *zwei*
Protokolle sprechen — Anthropic Messages und `openai_compatible`. Damit sind
abgedeckt: Anthropic, OpenAI, **Ollama**, **MiniMax**, vLLM, LiteLLM, LM Studio,
Groq, Together. Streaming über SSE **und** NDJSON. Der Anbieter hängt am
**Konto** (`KiZugang`), der Schlüssel liegt verschlüsselt und geht nie an den
Browser.

### Warum kein Framework

1. **Es gäbe eine zweite Fehlergestalt.** RFC 9457 gilt hier überall; ein
   Framework bringt seine eigene mit. Genau diese Divergenz wird später in der
   Oberfläche zugekleistert — dieselbe Begründung, aus der Girders
   `ICommand<T>` nicht benutzt wird.
2. **Ein Graph besitzt den Ablauf — und der gehört hier dem Menschen.**
   ADR-0034 ist genau darüber: kein Aufruf ohne Handlung, keine
   Reflexionsschleife, Freigeben und Senden getrennt. Ein Orchestrierer, dessen
   Zweck das selbstständige Weiterlaufen ist, arbeitet gegen die Zusage.
3. **Das .NET-Ökosystem ist nicht der Ort.** LangChain/LangGraph sind
   Python-erst; die .NET-Ports hinken und sind dünner als die 520 Zeilen, die
   sie ersetzen würden. Wir tauschten geprüften Code gegen eine Abhängigkeit
   mit weniger Deckung.
4. **Wir haben schon den Preis bezahlt.** Streaming, zwei Protokolle,
   Zeitlimits, Fehlerarten — das ist die Arbeit, die ein Framework abnimmt, und
   sie ist getan und getestet.

### Wo RAG *ehrlich* hingehört — und wo nicht

**Nicht über Menschen.** ADR-0033: durchsuchbar ist nur, was eine Person
**selbst genannt** hat. Ein Einbettungsindex über Profile und Lebensläufe wäre
genau die Suche „nach Ähnlichkeit zu einem Menschen", die ADR-0022 ausschließt
— und er läge als Kopie personenbezogener Daten neben der Löschung.

**Ehrlich ist RAG über die *eigenen* Unterlagen einer Person:**

> „In deinen hochgeladenen Zeugnissen steht *Schweißfachmann DVS*. Willst du
> das als Fähigkeit ins Profil übernehmen?"

Das ist die **Brücke aus ADR-0033** (belegt → vorgeschlagen → genannt), nur mit
Volltext statt GitHub-Topics. Es spricht über *ihre eigenen* Dokumente, zu ihr,
und macht daraus nichts ohne zwei Klicks. **Das ist PBI-7**, und es ist der
einzige Ort, an dem ein Index personenbezogener Texte hier vertretbar wäre —
mit einer Auflage: **er lebt im selben Dienst wie die Unterlagen und fällt mit
der Löschung.**

### Als Referenzprojekt

Der Wert liegt nicht in einer LangChain-Demo — davon gibt es tausende. Er liegt
in dem, was hier selten ist: eine **KI-Naht mit Zusagen**, die man vorzeigen
kann. Typisierter Kontext als Grenze, Feldliste als Test, kein Gedächtnis,
Anbieter je Konto, Schlüssel verschlüsselt, jeder Schritt eine Handlung eines
Menschen. Das ist der Teil, den kaum jemand baut.

---

## PBI-7 — Vorschläge aus den eigenen Unterlagen (optional, nach PBI-1)

> **Als Elektroniker** will ich, dass die Plattform mir aus meinem hochgeladenen
> Zertifikat vorschlägt, was ich in mein Profil schreiben könnte, **damit** ich
> nicht raten muss, wonach Unternehmen suchen.

- [ ] Texterkennung nur auf Auslösung, nie im Hintergrund (ADR-0004).
- [ ] Der Index lebt in resume-service und fällt mit der Löschung.
- [ ] Zwei Handlungen bis zur Nennung — Klick füllt das Feld, **Speichern**
      macht daraus eine Aussage.
- [ ] Nichts davon ist durchsuchbar, bevor die Person gespeichert hat.

---

## PBI-8 — Was aus den Auftragsdokumenten wirklich offen war

> **Als nächste Sitzung** will ich die offene Arbeit an **einer** Stelle finden,
> **damit** ich sie nicht aus acht Dokumenten mit lügenden Kästchen
> zusammensuche.

Aus 59 offenen Kästchen in acht Auftragsdokumenten blieb nach der Nachmessung
vom 11.09.2026 genau das hier übrig. Es steht jetzt hier, weil ein
Auftragsdokument Geschichte ist und der Plan die Aufgabenliste.

### 8.1 Begrenzte Parallelität und sichtbarer Fortschritt beim Erzeugen

`AUFTRAG-OPENCODE.md` 6.1 verlangt *„parallel mit Begrenzung (max. 3
gleichzeitig), mit sichtbarem Fortschritt"*, und
`AUFTRAG-BEWERBUNG-UND-SCOUT.md` führt die begrenzte Parallelität unter dem,
was **aus JobPilot übernommen wurde**. Gemessen: sie ist nicht übernommen.

- [x] **Begrenzt auf drei** (12.09.2026). `entwuerfe.ts` fuhr `Promise.all` über
      **alle** gewählten Stellen — wer zwölf ankreuzte, schickte zwölf
      Schreibaufträge gleichzeitig an applications-service und von dort an den
      KI-Anbieter. Jetzt: eine Schlange und drei Arbeiter (`GLEICHZEITIG = 3`).
- [x] **Der Fortschritt ist angeschlossen** — nicht entfernt, denn mit der
      Begrenzung gibt es endlich einen zu zeigen. `starteEntwuerfe` nimmt einen
      `Fortschrittsmelder` entgegen und ruft ihn einmal vorab mit `0` und dann
      nach jedem fertigen Entwurf; `JobsPage` reicht ihn auf dem Sammelweg
      durch. Der Katalogschlüssel `stellen.fortschritt` hat damit einen Leser.

**Das eine hing am anderen**, und in der Umsetzung zeigte es sich noch einmal:
ohne Begrenzung gibt es keinen Fortschritt zu zeigen, weil alles gleichzeitig
läuft und gleichzeitig fertig wird.

**Eine Schlange, kein `chunk(3)`.** Blockweise wäre kürzer und langsamer: ein
Block wartet auf seinen langsamsten Entwurf, und bei einem Anbieter mit
schwankenden Antwortzeiten stünden zwei von drei Plätzen einen grossen Teil der
Zeit leer. `entwuerfe.test.ts` pinnt beides getrennt — die Grenze *und* das
Nachziehen —, und die `chunk(3)`-Gegenprobe fällt genau an der zweiten.

**Drei Gegenproben, alle gefallen, alle kompilierten:** `GLEICHZEITIG = 12`
(die Grenze fällt), `chunk(3)` statt Schlange (Nachziehen und Fortschritt
fallen), `anlegen(jobIds)` ohne Melder (die Leiste fällt). Die erste deckte
eine **schwache Zusage** auf: der Test verglich zuerst nur gegen `GLEICHZEITIG`
und blieb deshalb grün, als die Konstante stieg. Die Drei steht jetzt als Zahl
im Test, weil der Auftrag „max. 3" zusagt und nicht „so viel wie die Konstante
sagt".

### 8.2 Die Geheimnisfrage (H2 aus `AUFTRAG-HAERTUNG.md`)

- [ ] `AddSecretManagement` ist **ungemessen** — CLAUDE.md führt es als „offen,
      gehört zu H2". Die Frage lautet nicht „einschalten oder nicht", sondern:
      nimmt dieses Modul den Platz von Infisical ein, oder steht es daneben?
- [ ] `AddEncryption` verlangt `IDataEncryptionService` **und**
      `IMasterKeyProvider`. Wir verschlüsseln heute auf Feldebene nichts; wer
      damit anfängt, entscheidet **zuerst**, wo der Hauptschlüssel liegt.
- [ ] `AddResourceAuthorization` ist ebenfalls ungemessen. Die Richtlinien
      stehen seit PBI-2; ob dieses Modul darüber hinaus etwas trägt, weiß
      niemand.

### 8.3 Die gemerkte Stelle wird geschrieben und nie gelesen

- [x] **`intent.ts` ist gefallen** (12.09.2026), samt seinen zwei
      Schreibstellen (`JobsPage`, `CareerPage`) und dem Katalogschlüssel
      `stellen.kontoNoetig` in drei Sprachen, der *„danach geht es direkt zur
      Bewerbung"* versprach und ebenfalls keinen Leser hatte. Von den zwei
      vertretbaren Auswegen ist der kleinere gefahren: der Zustand dazwischen —
      eine Absicht, die 24 Stunden im Browser einer Person liegt, ohne dass
      irgendetwas sie je einlöst — ist damit weg, und zwar ganz.
- [x] Der Kommentar in `JobsPage.tsx` über „die EINZIGE Stelle, an der die
      Absicht entsteht" ist mit ihr gefallen. An seiner Stelle steht in
      `LoginPage.tsx` der Satz, der für die Rückkehr zählt: **wer den Rückweg
      will, baut beide Hälften in einem Zug.** Nur eine davon ist genau der
      Zustand, der hier beseitigt wurde.
- [x] `JobsPage.test.tsx` pinnt die neue Zusage statt der alten: der
      Bewerben-Knopf ohne Konto legt **nichts** im Browser ab
      (`localStorage.length === 0`). Eine Rückkehr ist damit eine Entscheidung
      und kein Versehen — sie muss diesen Test zuerst umschreiben.

### 8.4 `/code-review ultra` (H5) — startet ein Mensch

- [ ] Über WorkerTransfer, Girder und Skillswap. **Kein Agent kann das
      auslösen**, und keiner sollte es. Steht hier, damit es nicht in einem
      abgeschlossenen Auftragsdokument verschwindet.

