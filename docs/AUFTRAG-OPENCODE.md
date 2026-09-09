# Auftrag für OpenCode: Phase 5, 6 und 7 zu Ende bringen

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

**Ziel:** Wenn eine Bewerbung eingeht, bekommen die **Mitglieder** des
Unternehmens eine Mail. Eine Firma hat kein Postfach, Menschen haben eines.

- [ ] **5.1** Neue Art `ApplicationReceived` in
      `src/notification-service/…/Benachrichtigungen/Benachrichtigungsart.cs`:
      Enum-Wert, `Alle`-Liste, `Wort()` → `"application_received"`, `Lies()`.
      **Alle vier Stellen**, sonst fällt die Nachricht still weg.
- [ ] **5.2** Empfängerauflösung: applications-service braucht die
      Mitglieder-IDs eines Mandanten. identity-service hat
      `GET /companies/me/members` (nur eigenes Unternehmen) — **prüfe zuerst**,
      ob ein dienstinterner Weg existiert; wenn nicht, baue
      `GET /internal/companies/{tenantId}/members` mit demselben geteilten
      Geheimnis wie `Notifications__Geheimnis`. **Typisierter Vertrag** in
      `src/shared/WorkerTransfer.Contracts.Identity` — **nie** `new { … }`
      (der Benachrichtigungsdraht kam viermal nie an, genau deshalb).
- [ ] **5.3** In `EntwurfSendenHandler` (Datei
      `…Application/Entwuerfe/Entwuerfe.cs`, ganz unten) **und** in
      `BewerbungAbschickenHandler`: je Mitglied eine Outbox-Zeile mit der neuen
      Art. Der Postausgang bleibt **inhaltsfrei** (ADR-0025): Kennung und Art,
      **nie** ein Name, nie eine Stelle, nie eine E-Mail-Adresse.
- [ ] **5.4** Test: eine eingegangene Bewerbung erzeugt je Mitglied genau eine
      Zeile; die Zeile trägt keinen Inhalt. Und: wer die Art abbestellt hat,
      bekommt keine Mail (Muster: `notification-journey.spec.ts:88`).

---

## 3. Phase 6 — die Oberfläche (das Herzstück, hier liegt die Arbeit)

Alles unter `web/src/`. Struktur: `features/<name>/{components,pages,store,types}`,
API-Aufrufe in `features/<name>/api/`. Muster zum Abschauen:
`features/person/api/github.ts` und `features/person/pages/GitHubPage.tsx`.

### 6.1 Mehrfachauswahl auf `/jobs`

- [ ] Kästchen an jeder Stellenkarte (`features/work/pages/JobsPage.tsx`).
- [ ] Eine Leiste unten: **„N ausgewählt · Für alle bewerben"**.
- [ ] Klick → `POST /applications/drafts {job_ids}` → dann **je Entwurf**
      `POST /applications/drafts/{id}/write`, parallel mit Begrenzung (max. 3
      gleichzeitig), mit sichtbarem Fortschritt → danach nach
      `/applications/drafts`.
- [ ] Nur für Angemeldete. Ohne Anmeldung: Kästchen gar nicht zeigen.

### 6.2 Liste `/applications/drafts`

- [ ] Neue Route in `core/router`. Zustandsanzeigen als Chips (Farben aus
      Tokens; **kein Grün als Hausfarbe** — Grün ist im System das Signal
      „erteilt").
- [ ] Je Zeile: Stellentitel (aus jobs-service nachladen), Fassungsnummer,
      Anzahl offener Anmerkungen, Knöpfe je nach Stand.
- [ ] `failed` zeigt `error` und einen Knopf „nochmal versuchen" (`/write`).

### 6.3 Prüfansicht `/applications/drafts/:id` — die wichtigste Seite

- [ ] Das Anschreiben **wie ein Brief gesetzt**: Seitenspiegel (max. ~21 cm
      breit), großzügige Ränder, Serifenschrift, Absatzabstände. Es soll
      aussehen wie das, was es ist — ohne eine Datei zu sein.
- [ ] **Text markieren → „Kommentieren"**: `window.getSelection()` liefert das
      Zitat, es reist als `quote` mit. Markierung optional.
- [ ] Anmerkungsliste seitlich oder darunter; erledigte durchgestrichen/blass.
- [ ] Knöpfe: **Überarbeiten** (nur bei offenen Anmerkungen) · **Freigeben**
      (nur ohne) · **Senden** (nur aus `approved`). **Zwei getrennte Knöpfe für
      Freigeben und Senden — niemals einer.**
- [ ] Beilagen wählen: Schalter „Lebenslauf mitschicken" + Liste der eigenen
      Unterlagen mit Kästchen → `PUT /attachments`.
- [ ] Eigenes Bearbeiten von Betreff/Text → `PATCH`.
- [ ] 503 vom Server heißt **„kein Anbieter eingerichtet"** und wird als Satz
      gezeigt, nicht als Fehlerblock.

### 6.4 Die Mappe für das Unternehmen

- [ ] Route `/company/applications/:id` (Firmenkontext).
- [ ] **Scrollbare Reiterleiste** (MUI `Tabs` mit `variant="scrollable"`,
      `scrollButtons="auto"`): **Anschreiben · Lebenslauf · je ein Reiter pro
      Zertifikat**. Die Anzahl ist unbekannt, die Leiste muss scrollen.
- [ ] *Anschreiben*: `BewerbungV1.message`, in derselben Brief-Darstellung wie
      in der Prüfansicht (gemeinsame Komponente
      `shared/components/Briefbogen.tsx`).
- [ ] *Lebenslauf*: `GET /resumes/{subject_id}` → in der **vom Bewerber
      gewählten Vorlage** rendern (`template` aus der Antwort). Nur zeigen,
      wenn `shares_resume`.
- [ ] *Zertifikate*: je `documents[]`-Eintrag ein Reiter,
      `GET /resumes/{subject_id}/documents/{id}/content` — Bilder als `<img>`,
      PDF als `<object>`/`<iframe>`. **Nicht herunterladen, anzeigen.**
- [ ] Leere Liste = keine Freigabe **oder** nichts vorhanden. Beides sieht
      gleich aus; nicht unterscheiden (das wäre eine Tatsache über die Person).

### 6.5 Lebenslauf-Vorlagen als CSS

- [ ] `web/src/features/person/components/lebenslauf/` mit drei Vorlagen:
      `Schlicht`, `Klassisch` (zweispaltig, Kopfzeile), `Modern` (kräftige
      Überschriften, viel Weißraum). **Eine** Komponente `Lebenslaufblatt`, die
      die Vorlage als Prop nimmt.
- [ ] **Dieselbe Komponente für beide Seiten** — die Person sieht exakt, was das
      Unternehmen sieht. Zwei Darstellungen wären zwei Wahrheiten.
- [ ] Nur Tokens, keine Farbliterale. Druckbar (`@media print`), damit ein PDF
      über den Browser entsteht — der Server erzeugt keins (ADR-0035).

### 6.6 `/resume`: Unterlagen und Vorlagenwahl

- [ ] Hochladen (Datei wählen, Name, Art), Liste, Löschen.
- [ ] Fehler benennen: falscher Typ (422 „only PNG, JPEG and PDF are
      accepted"), zu groß (413), zu viele (422).
- [ ] Vorlagenwahl als drei Vorschaukacheln → `PUT /resumes/me/template`.

### 6.7 Kataloge

- [ ] Jeder neue Text in `de.ts`, `en.ts`, `fr.ts`. Deutsch ist die Quelle,
      die anderen sind Übersetzungen — **keine Kopien** (der Wächter prüft das).
- [ ] Fehlermeldungen der API-Schicht sind **Schlüssel**, keine Sätze
      (`shared/api/fehler.ts`).

---

## 4. Phase 7 — Scout und Berater

**Vor dem Code steht je ein ADR.** Grundlage ist gelegt: ADR-0033 (Beleg und
Sichtbarkeit) und die Bestandsaufnahme in
[`SCOUT-UND-BERATER-BESTAND.md`](SCOUT-UND-BERATER-BESTAND.md) — **lies die
Bestandsaufnahme zuerst, sie widerlegt Teile des Entwurfs**.

- [ ] **ADR-0036 `scout-service`.** Löst `GET /candidates` ab (die harten Teile
      — Ledger je Zeile über `/check-batch`, **keine Gesamtzahl**, Firmenzwang —
      sind fertig und werden **mitgenommen, nicht neu erfunden**). Vier Auflagen
      als Tests: **keine Sortierung nach Passung** · **keine Zahl** · **nur
      Genanntes ist durchsuchbar** (Belege werden zum Treffer dazugeholt, nie
      zum Finden benutzt) · **die Ansprache ist ein Entwurf**, der Dienst
      schreibt niemandem.
- [ ] **Die Nachricht „dein Profil wurde entdeckt"** (ADR-0033): eigene
      Benachrichtigungsart, **nennt kein Unternehmen**, über den Postausgang,
      **höchstens eine je Person und Tag**. Es entsteht **kein** `/me/scouting`
      und **keine** Tabelle „wer hat wen angesehen".
- [ ] **ADR-0037 `advisor-service`.** Das Mandat ist eine **Sicht** auf Ledger
      (Sichtbarkeit) und Marktstatus (Verfügbarkeit) plus vier eigene Felder
      (Eintrittstermin, Gehaltsspanne, Pensum, ausgeschlossene Unternehmen).
      Eine eigene Mandatstabelle für Sichtbarkeit **verstößt gegen ADR-0020**.
- [ ] Beide Dienste sind ab der ersten Tabelle **Löschempfänger** — sonst geht
      `LoeschempfaengerTests` rot, und zwar zu Recht.

---

## 5. Abnahme — genau in dieser Reihenfolge

```bash
dotnet build                      # 0 Warnungen, 0 Fehler
./scripts/test-dotnet.sh          # ≥875 grün, 0 rot, 0 uebersprungen
cd web && pnpm check && pnpm test && pnpm build
make up                           # falls nicht schon oben
make routenkarte                  # jede neue Route braucht einen Eintrag
cd web && pnpm exec playwright test      # ERST HIER, einmal
```

- [ ] **Neue E2E-Reise** `web/e2e/application-letter-journey.spec.ts`: Stellen
      mehrfach auswählen → Entwürfe entstehen → **ohne KI-Anbieter sagt die
      Seite das** (nicht still nichts tun) → kommentieren → freigeben scheitert
      → überarbeiten → freigeben → senden → Unternehmen sieht die Mappe mit
      scrollbaren Reitern.
- [ ] `docs/routenkarte.yml` um alle neuen Endpunkte ergänzen — **erst raten,
      dann `make routenkarte` messen, dann auf das Gemessene korrigieren.**
- [ ] **CLAUDE.md nachziehen:** siebtes Paket in `src/shared/`
      (`WorkerTransfer.Ablage`), die neue Fähigkeit
      `documents.visibility:tenant:<id>`, die neue Benachrichtigungsart, die
      neuen ADRs 0034/0035 (+0036/0037), und dass es 16 Testreihen sind.

---

## 6. Die drei Sätze, die alles tragen

1. **Nichts geht ohne zwei Klicks eines Menschen hinaus.** Freigeben und Senden
   sind getrennt, und es gibt keinen Weg von `generating` nach `sent`.
2. **Keine Zahl über einen Menschen** — auch nicht über die eigene Bewerbung.
   Kein Score, kein Rang, keine Sortierung nach Passung.
3. **Was unvollständig ist, sagt es.** Leere Bereiche, gekürzte Mengen und
   fehlende Belege werden benannt, nicht ausgegraut (ADR-0022 §3).
