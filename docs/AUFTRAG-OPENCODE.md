# Auftrag für OpenCode: Phase 5, 6 und 7 zu Ende bringen

> **ABGESCHLOSSEN am 11.09.2026. Dieses Dokument ist Geschichte, keine
> Aufgabenliste.** Es stand bis dahin mit **39 offenen Kästchen** da, und
> **keines** davon war noch offen — eine Stichprobe von sechs war sechsmal
> längst gebaut. Ein Dokument mit offenen Kästchen liest sich wie eine
> Aufgabenliste, und die nächste Sitzung hätte angefangen, etwas zweimal zu
> bauen. Die Kästchen sind am 11.09.2026 einzeln gegen den Baum geprüft und
> stehen jetzt auf dem Gemessenen.
>
> **Was offen blieb:** genau eine Zeile, und sie ist ein halbes Versprechen
> statt eines fehlenden — 6.1 sagte „parallel mit Begrenzung (max. 3
> gleichzeitig), mit sichtbarem Fortschritt". Gebaut ist `Promise.all` über
> **alle** gewählten Stellen, und `setFortschritt` wird in `JobsPage.tsx` nur
> mit `null` gerufen: die Fortschrittsanzeige und ihr Katalogschlüssel
> `stellen.fortschritt` sind da und werden nie erreicht. Das steht als Aufgabe
> im [PLAN](PLAN-TRANSFERMARKT.md), denn dorthin gehört offene Arbeit.
>
> **Wo das Ergebnis heute steht:** Phase 5 in notification- und
> applications-service (`Benachrichtigungsart.ApplicationReceived`,
> `GET /internal/companies/{tenantId}/members`), Phase 6 in `web/src/features/
> work` und `web/src/features/company`, Phase 7 als `scout-service` (ADR-0036)
> und `advisor-service` (ADR-0037). Die Begründungen unten gelten unverändert —
> nur die Kästchen logen.

**Stand 05.09.2026.** Phase 0–4 sind fertig und grün: **875 .NET-Tests, 0 rot,
0 Warnungen.** Dieser Auftrag beschreibt **nur den Rest**. Der vollständige Plan
mit Begründungen steht in [`AUFTRAG-BEWERBUNG-UND-SCOUT.md`](AUFTRAG-BEWERBUNG-UND-SCOUT.md);
lies ihn, bevor du eine Entscheidung triffst, die dort schon begründet ist.

---

## 0. Regeln, die dich sonst Zeit kosten

Lies **CLAUDE.md** ganz. Diese sechs haben in dieser Sitzung real Zeit gekostet:

1. **`dotnet build` und `dotnet test` NIE im selben Aufruf.** `build && test`
   killt Testcontainers, und es sieht aus wie neun echte Testfehler.
2. **Nie `dotnet test` über die Lösung.** Nur `./scripts/test-dotnet.sh`
   (läuft die 16 Reihen einzeln) oder ein einzelnes `.csproj`.
3. **Nach jeder Modelländerung sofort `dotnet ef migrations add`.** Sonst
   scheitern **alle** Tests der Reihe in ~18 ms mit
   `PendingModelChangesWarning` — das liest sich wie ein kaputter Build.
4. **Typografische Anführungszeichen in Zeichenketten.** `„…"` mit ASCII-`"`
   als Schluss beendet den String und bricht den Build. Zweimal passiert (in
   `de.ts` und in einer C#-Interpolation). Nimm `“` (U+201C) oder gar keine.
5. **Warnungen sind Fehler** (`Directory.Build.props`). Auch `else if (false)`
   ist ein Build-Fehler (unerreichbarer Code) — bei Gegenproben also einen
   Bruch wählen, der **kompiliert**.
6. **E2E erst ganz am Ende, einmal.** Nicht nach jeder Änderung — die Reihe
   braucht ~11 Minuten.

**Weitere harte Regeln:** RFC 9457 überall · keine Werte in Protokollen, nur
Gestalten · Dienst-zu-Dienst-Rümpfe sind **typisierte Verträge**, nie anonyme
Objekte · jede Person-Tabelle ist Löschempfänger (`LoeschempfaengerTests` liest
das EF-Modell) · **jede** neue Route braucht einen Eintrag in
`docs/routenkarte.yml`, sonst geht `RoutenkarteTests` rot · jeder Text in
**drei** Katalogen (`de/en/fr`), sonst gehen die Wächter in
`web/src/core/i18n/schluessel.test.ts` rot · **keine Farbliterale** in
Komponenten, nur `src/styles/tokens/`.

---

## 1. Was schon steht (nicht neu bauen!)

### resume-service — Unterlagen und Vorlage

```
POST   /resumes/me/documents            multipart: file, name, kind   → 201 UnterlageV1
GET    /resumes/me/documents                                          → [UnterlageV1]
GET    /resumes/me/documents/{id}/content                             → inline bytes
DELETE /resumes/me/documents/{id}                                     → 204
PUT    /resumes/me/template              {"template":"schlicht|klassisch|modern"} → 204
GET    /resumes/{subjectId}/documents          (Firma, Ledger-geprüft) → [UnterlageV1]
GET    /resumes/{subjectId}/documents/{id}/content (Firma, Ledger)     → inline bytes
```

`LebenslaufV1` trägt jetzt zusätzlich `template`.
`UnterlageV1` = `{id, name, kind, content_type, size_bytes, uploaded_at}`.
Grenzen: **10 Unterlagen, je 5 MB**, nur **PNG/JPEG/PDF** (nach Signatur, nicht
nach Endung). `kind` ∈ `zeugnis|zertifikat|sonstiges`.

### applications-service — Entwürfe

```
POST   /applications/drafts              {"job_ids":[…]}      → 201 [EntwurfV1]
POST   /applications/drafts/{id}/write                        → EntwurfV1  (KI schreibt)
GET    /applications/drafts                                   → [EntwurfV1]
GET    /applications/drafts/{id}                              → EntwurfV1
PATCH  /applications/drafts/{id}         {subject, body}      → EntwurfV1
PUT    /applications/drafts/{id}/attachments {shares_resume, documents:[…]} → EntwurfV1
POST   /applications/drafts/{id}/comments   {text, quote}     → EntwurfV1
POST   /applications/drafts/{id}/revise                       → EntwurfV1
POST   /applications/drafts/{id}/approve                      → EntwurfV1
POST   /applications/drafts/{id}/send                         → EntwurfV1
DELETE /applications/drafts/{id}                              → 204
```

`EntwurfV1` = `{id, job_id, subject, body, status, version, error,
shares_resume, documents[], comments[], updated_at}`
`AnmerkungV1` = `{id, text, quote, resolved, created_at}`

**`status`** ∈ `generating | review | needs_changes | approved | sent | failed`.

**Statuscodes:** 404 = gibt es nicht *oder* gehört jemand anderem (bewusst
ununterscheidbar) · 409 = Schritt von hier aus nicht erlaubt · 422 = Eingabe ·
**503 = kein KI-Anbieter eingerichtet** (nicht 500 — es ist nichts kaputt).

`BewerbungV1` trägt jetzt zusätzlich `documents: [Guid]`.

**Wichtig:** `POST /drafts` legt nur an (Stand `generating`). Das Schreiben ist
ein **eigener** Aufruf je Entwurf (`/write`) — die Oberfläche ruft ihn N-mal und
zeigt Fortschritt. Ein Entwurf, der in `generating` hängen bleibt (Browser zu),
bekommt einen Knopf „weiter schreiben".

---

## 2. Phase 5 — die Benachrichtigung an das Unternehmen

**Gebaut, gemessen am 11.09.2026:** `Benachrichtigungsart.ApplicationReceived`
steht an allen vier Stellen; `GET /internal/companies/{tenantId}/members` liegt
in `InterneEndpoints.cs` und der Vertrag in
`WorkerTransfer.Contracts.Identity/Unternehmensmitglieder.cs`; beide Sender
(`Entwuerfe.cs:536`, `BewerbungAbschicken.cs:111`) schreiben je Mitglied eine
Zeile, und `BewerbungsreiseTests.Bewerben_hinterlaesst_je_Mitglied_eine_inhaltsfreie_Absicht`
hält fest, dass sie inhaltsfrei ist.

**Ziel:** Wenn eine Bewerbung eingeht, bekommen die **Mitglieder** des
Unternehmens eine Mail. Eine Firma hat kein Postfach, Menschen haben eines.

- [x] **5.1** Neue Art `ApplicationReceived` in
      `src/notification-service/…/Benachrichtigungen/Benachrichtigungsart.cs`:
      Enum-Wert, `Alle`-Liste, `Wort()` → `"application_received"`, `Lies()`.
      **Alle vier Stellen**, sonst fällt die Nachricht still weg.
- [x] **5.2** Empfängerauflösung: applications-service braucht die
      Mitglieder-IDs eines Mandanten. identity-service hat
      `GET /companies/me/members` (nur eigenes Unternehmen) — **prüfe zuerst**,
      ob ein dienstinterner Weg existiert; wenn nicht, baue
      `GET /internal/companies/{tenantId}/members` mit demselben geteilten
      Geheimnis wie `Notifications__Geheimnis`. **Typisierter Vertrag** in
      `src/shared/WorkerTransfer.Contracts.Identity` — **nie** `new { … }`
      (der Benachrichtigungsdraht kam viermal nie an, genau deshalb).
- [x] **5.3** In `EntwurfSendenHandler` (Datei
      `…Application/Entwuerfe/Entwuerfe.cs`, ganz unten) **und** in
      `BewerbungAbschickenHandler`: je Mitglied eine Outbox-Zeile mit der neuen
      Art. Der Postausgang bleibt **inhaltsfrei** (ADR-0025): Kennung und Art,
      **nie** ein Name, nie eine Stelle, nie eine E-Mail-Adresse.
- [x] **5.4** Test: eine eingegangene Bewerbung erzeugt je Mitglied genau eine
      Zeile; die Zeile trägt keinen Inhalt. Und: wer die Art abbestellt hat,
      bekommt keine Mail (Muster: `notification-journey.spec.ts:88`).

---

## 3. Phase 6 — die Oberfläche (das Herzstück, hier liegt die Arbeit)

**Gebaut bis auf eine halbe Zeile, gemessen am 11.09.2026** — siehe 6.1. Die
Seiten liegen in `web/src/features/work/pages` (`JobsPage`, `DraftsPage`,
`DraftPage`) und `web/src/features/company/pages` (`CompanyApplicationPage`);
der Briefbogen ist `shared/components/Briefbogen.tsx`, das Lebenslaufblatt
`features/person/components/lebenslauf/Lebenslaufblatt.tsx` und wird von beiden
Seiten gerufen.

Alles unter `web/src/`. Struktur: `features/<name>/{components,pages,store,types}`,
API-Aufrufe in `features/<name>/api/`. Muster zum Abschauen:
`features/person/api/github.ts` und `features/person/pages/GitHubPage.tsx`.

### 6.1 Mehrfachauswahl auf `/jobs`

- [x] Kästchen an jeder Stellenkarte (`features/work/pages/JobsPage.tsx`).
- [x] Eine Leiste unten: **„N ausgewählt · Für alle bewerben"**.
- [x] Klick → `POST /applications/drafts {job_ids}` → dann **je Entwurf**
      `POST /applications/drafts/{id}/write` → danach nach
      `/applications/drafts`.
- [x] **…parallel mit Begrenzung (max. 3 gleichzeitig), mit sichtbarem
      Fortschritt** — eingelöst am 12.09.2026 als PBI-8.1, und damit ist auch
      die letzte offene Zeile dieses Auftrags zu. Sie war bis dahin die
      einzige: gebaut war `Promise.all` über **alle** gewählten Stellen, und
      `setFortschritt` wurde in `JobsPage.tsx` nur mit `null` gerufen, sodass
      die Anzeige und ihr Katalogschlüssel `stellen.fortschritt` da waren und
      nie erreicht wurden. Jetzt: eine Schlange und drei Arbeiter in
      `features/work/lib/entwuerfe.ts`, ein `Fortschrittsmelder` durch
      `JobsPage`. Das ist der Fall, den `AUFTRAG-BEWERBUNG-UND-SCOUT.md` unter
      „Übernommen aus JobPilot" als *begrenzte Parallelität* führt — jetzt
      wirklich übernommen. Gegenproben und Messung stehen im
      [PLAN](PLAN-TRANSFERMARKT.md).
- [x] Nur für Angemeldete. Ohne Anmeldung: Kästchen gar nicht zeigen.

### 6.2 Liste `/applications/drafts`

- [x] Neue Route in `core/router`. Zustandsanzeigen als Chips (Farben aus
      Tokens; **kein Grün als Hausfarbe** — Grün ist im System das Signal
      „erteilt").
- [x] Je Zeile: Stellentitel (aus jobs-service nachladen), Fassungsnummer,
      Anzahl offener Anmerkungen, Knöpfe je nach Stand.
- [x] `failed` zeigt `error` und einen Knopf „nochmal versuchen" (`/write`).

### 6.3 Prüfansicht `/applications/drafts/:id` — die wichtigste Seite

- [x] Das Anschreiben **wie ein Brief gesetzt**: Seitenspiegel (max. ~21 cm
      breit), großzügige Ränder, Serifenschrift, Absatzabstände. Es soll
      aussehen wie das, was es ist — ohne eine Datei zu sein.
- [x] **Text markieren → „Kommentieren"**: `window.getSelection()` liefert das
      Zitat, es reist als `quote` mit. Markierung optional.
- [x] Anmerkungsliste seitlich oder darunter; erledigte durchgestrichen/blass.
- [x] Knöpfe: **Überarbeiten** (nur bei offenen Anmerkungen) · **Freigeben**
      (nur ohne) · **Senden** (nur aus `approved`). **Zwei getrennte Knöpfe für
      Freigeben und Senden — niemals einer.**
- [x] Beilagen wählen: Schalter „Lebenslauf mitschicken" + Liste der eigenen
      Unterlagen mit Kästchen → `PUT /attachments`.
- [x] Eigenes Bearbeiten von Betreff/Text → `PATCH`.
- [x] 503 vom Server heißt **„kein Anbieter eingerichtet"** und wird als Satz
      gezeigt, nicht als Fehlerblock.

### 6.4 Die Mappe für das Unternehmen

- [x] Route `/company/applications/:id` (Firmenkontext).
- [x] **Scrollbare Reiterleiste** (MUI `Tabs` mit `variant="scrollable"`,
      `scrollButtons="auto"`): **Anschreiben · Lebenslauf · je ein Reiter pro
      Zertifikat**. Die Anzahl ist unbekannt, die Leiste muss scrollen.
- [x] *Anschreiben*: `BewerbungV1.message`, in derselben Brief-Darstellung wie
      in der Prüfansicht (gemeinsame Komponente
      `shared/components/Briefbogen.tsx`).
- [x] *Lebenslauf*: `GET /resumes/{subject_id}` → in der **vom Bewerber
      gewählten Vorlage** rendern (`template` aus der Antwort). Nur zeigen,
      wenn `shares_resume`.
- [x] *Zertifikate*: je `documents[]`-Eintrag ein Reiter,
      `GET /resumes/{subject_id}/documents/{id}/content` — Bilder als `<img>`,
      PDF als `<object>`/`<iframe>`. **Nicht herunterladen, anzeigen.**
- [x] Leere Liste = keine Freigabe **oder** nichts vorhanden. Beides sieht
      gleich aus; nicht unterscheiden (das wäre eine Tatsache über die Person).

### 6.5 Lebenslauf-Vorlagen als CSS

- [x] `web/src/features/person/components/lebenslauf/` mit drei Vorlagen:
      `Schlicht`, `Klassisch` (zweispaltig, Kopfzeile), `Modern` (kräftige
      Überschriften, viel Weißraum). **Eine** Komponente `Lebenslaufblatt`, die
      die Vorlage als Prop nimmt.
- [x] **Dieselbe Komponente für beide Seiten** — die Person sieht exakt, was das
      Unternehmen sieht. Zwei Darstellungen wären zwei Wahrheiten.
- [x] Nur Tokens, keine Farbliterale. Druckbar (`@media print`), damit ein PDF
      über den Browser entsteht — der Server erzeugt keins (ADR-0035).

### 6.6 `/resume`: Unterlagen und Vorlagenwahl

- [x] Hochladen (Datei wählen, Name, Art), Liste, Löschen.
- [x] Fehler benennen: falscher Typ (422 „only PNG, JPEG and PDF are
      accepted"), zu groß (413), zu viele (422).
- [x] Vorlagenwahl als drei Vorschaukacheln → `PUT /resumes/me/template`.

### 6.7 Kataloge

- [x] Jeder neue Text in `de.ts`, `en.ts`, `fr.ts`. Deutsch ist die Quelle,
      die anderen sind Übersetzungen — **keine Kopien** (der Wächter prüft das).
- [x] Fehlermeldungen der API-Schicht sind **Schlüssel**, keine Sätze
      (`shared/api/fehler.ts`).

---

## 4. Phase 7 — Scout und Berater

**Gebaut am 11.09.2026:** `scout-service` (ADR-0036, Hafen 8012) und
`advisor-service` (ADR-0037, Hafen 8013), beide mit ihren Auflagen als Tests
(`AuflagenTests`) und beide ab der ersten Tabelle in
`Loeschempfaenger.Fremde`. Die Nachricht heisst `profile_discovered`. Nachträge
zum Auftrag: `assessment-service` kam als dritter dazu (ADR-0042), und
`GET /candidates` ist gefallen statt zu bleiben.

**Vor dem Code steht je ein ADR.** Grundlage ist gelegt: ADR-0033 (Beleg und
Sichtbarkeit) und die Bestandsaufnahme in
[`SCOUT-UND-BERATER-BESTAND.md`](SCOUT-UND-BERATER-BESTAND.md) — **lies die
Bestandsaufnahme zuerst, sie widerlegt Teile des Entwurfs**.

- [x] **ADR-0036 `scout-service`.** Löst `GET /candidates` ab (die harten Teile
      — Ledger je Zeile über `/check-batch`, **keine Gesamtzahl**, Firmenzwang —
      sind fertig und werden **mitgenommen, nicht neu erfunden**). Vier Auflagen
      als Tests: **keine Sortierung nach Passung** · **keine Zahl** · **nur
      Genanntes ist durchsuchbar** (Belege werden zum Treffer dazugeholt, nie
      zum Finden benutzt) · **die Ansprache ist ein Entwurf**, der Dienst
      schreibt niemandem.
- [x] **Die Nachricht „dein Profil wurde entdeckt"** (ADR-0033): eigene
      Benachrichtigungsart, **nennt kein Unternehmen**, über den Postausgang,
      **höchstens eine je Person und Tag**. Es entsteht **kein** `/me/scouting`
      und **keine** Tabelle „wer hat wen angesehen".
- [x] **ADR-0037 `advisor-service`.** Das Mandat ist eine **Sicht** auf Ledger
      (Sichtbarkeit) und Marktstatus (Verfügbarkeit) plus vier eigene Felder
      (Eintrittstermin, Gehaltsspanne, Pensum, ausgeschlossene Unternehmen).
      Eine eigene Mandatstabelle für Sichtbarkeit **verstößt gegen ADR-0020**.
- [x] Beide Dienste sind ab der ersten Tabelle **Löschempfänger** — sonst geht
      `LoeschempfaengerTests` rot, und zwar zu Recht.

---

## 5. Abnahme — genau in dieser Reihenfolge

```bash
dotnet build                      # 0 Warnungen, 0 Fehler
./scripts/test-dotnet.sh          # ≥875 grün, 0 rot, 0 uebersprungen
                                  # (Stand 11.09.2026: es sind deutlich mehr —
                                  #  die gültige Zahl steht im PLAN, nicht hier)
cd web && pnpm check && pnpm test && pnpm build
make up                           # falls nicht schon oben
make routenkarte                  # jede neue Route braucht einen Eintrag
cd web && pnpm exec playwright test      # ERST HIER, einmal
```

- [x] **Neue E2E-Reise** `web/e2e/application-letter-journey.spec.ts`: Stellen
      mehrfach auswählen → Entwürfe entstehen → **ohne KI-Anbieter sagt die
      Seite das** (nicht still nichts tun) → kommentieren → freigeben scheitert
      → überarbeiten → freigeben → senden → Unternehmen sieht die Mappe mit
      scrollbaren Reitern.
- [x] `docs/routenkarte.yml` um alle neuen Endpunkte ergänzen — **erst raten,
      dann `make routenkarte` messen, dann auf das Gemessene korrigieren.**
- [x] **CLAUDE.md nachziehen:** siebtes Paket in `src/shared/`
      (`WorkerTransfer.Ablage`), die neue Fähigkeit
      `documents.visibility:tenant:<id>`, die neue Benachrichtigungsart, die
      neuen ADRs 0034/0035 (+0036/0037), und dass es 16 Testreihen sind.
      *(Nachgetragen; die Zahl der Reihen läuft seither weiter — 11.09.2026
      sind es 19. Eine Zahl in Prosa veraltet, das ist ihre Natur.)*

---

## 6. Die drei Sätze, die alles tragen

1. **Nichts geht ohne zwei Klicks eines Menschen hinaus.** Freigeben und Senden
   sind getrennt, und es gibt keinen Weg von `generating` nach `sent`.
2. **Keine Zahl über einen Menschen** — auch nicht über die eigene Bewerbung.
   Kein Score, kein Rang, keine Sortierung nach Passung.
3. **Was unvollständig ist, sagt es.** Leere Bereiche, gekürzte Mengen und
   fehlende Belege werden benannt, nicht ausgegraut (ADR-0022 §3).
