# Auftrag: Bewerbungsmappe, Anschreiben-Agent, Scout und Berater

> **ABGESCHLOSSEN am 11.09.2026. Dieses Dokument ist Geschichte, keine
> Aufgabenliste.** Es stand bis dahin mit **20 offenen Kästchen** da — Phase 5,
> 6, 7 und 8 —, und **keines** davon war noch offen. Wer die Kästchen liest und
> sonst nichts (so steht es zwei Absätze weiter unten), fing hier an, Dinge ein
> zweites Mal zu bauen. Am 11.09.2026 sind sie einzeln gegen den Baum geprüft
> und stehen jetzt auf dem Gemessenen.
>
> **Was offen blieb:** eine halbe Zeile. Unter „Was aus JobPilot übernommen
> wird" steht **die begrenzte Parallelität beim Erzeugen** — und genau die ist
> nicht gebaut: `JobsPage.tsx` ruft `Promise.all` über alle gewählten Stellen,
> und die Fortschrittsanzeige daneben wird nie gefüllt. Das ist die einzige
> offene Arbeit aus beiden Auftragsdokumenten und steht als Aufgabe im
> [PLAN](PLAN-TRANSFERMARKT.md).
>
> **Wo das Ergebnis heute steht:** die Ablage in
> `src/shared/WorkerTransfer.Ablage`, die Unterlagen in resume-service, der
> Entwurf samt Zustandsmaschine in applications-service
> (`Application/Entwuerfe/Entwuerfe.cs`), die Oberfläche in
> `web/src/features/{work,company,person}`, Scout und Berater als eigene
> Dienste (ADR-0036 / ADR-0037). Der Fließtext unten ist der Wert dieses
> Dokuments und gilt unverändert — nur die Kästchen logen.

**Stand:** 05.09.2026 · **Zweck:** ein Plan, der einen Abbruch übersteht.

> **Wo es stand (05.09.2026):** Phase 0–4 sind fertig und grün — **875
> .NET-Tests, 0 rot**. Der ganze Rückweg (Domäne, Ablage, Unterlagen, Entwurf,
> Zustandsmaschine, Anschreiben-Agent, Endpunkte, Wanderungen) liegt. **Offen
> sind Phase 5 (Benachrichtigung ans Unternehmen), Phase 6 (Oberfläche) und
> Phase 7 (Scout/Berater).** Ohne Phase 6 ist nichts davon anklickbar — das ist
> der nächste Schritt.

Jeder Punkt trägt ein Kästchen. Wer hier weitermacht, liest die Kästchen und
sonst nichts — der Fließtext erklärt nur, *warum* etwas so gebaut wird.

---

## Was gemessen wurde, bevor der Plan entstand

| Fund | Beleg |
|---|---|
| `/languages` wird ohne Token **nie** gerufen | `HttpGitHub.cs:174` — `if (Token.Length == 0) return []` |
| Es gibt **keinen Dateispeicher** | Portfolio-„Anhang" ist ein *Name* (`Eintrag.cs:109`), ADR-0021 hat den Speicher in Python beschrieben, .NET hat ihn nicht |
| Es gibt **keinen Begriff für „ich schicke selbst"** | Nur `resume.requested/granted/declined` — der Weg, auf dem ein *Unternehmen fragt* |
| Ein Unternehmen wird bei Bewerbung **nicht benachrichtigt** | `BewerbungBewegen.cs:58` schreibt nur an `bewerbung.Wer` |
| JobPilot hat den Ablauf schon | `~/Downloads/jobpilot` — Zustandsmaschine, Kommentare mit Zitat, Revision, Freigabe |

---

## Die Entscheidung, die alles ordnet: zwei Wege zum Lebenslauf

Heute gibt es einen, und deshalb steht auf der Freigabeseite ein Satz über den
jetzigen Arbeitgeber, während jemand gerade eine Bewerbung schreiben will.

| | **Transfermarkt** | **Bewerbung** |
|---|---|---|
| Wer beginnt? | Das Unternehmen fragt | **Die Person schickt** |
| Weiß die Person davon? | Sie antwortet auf eine Anfrage | Sie hat es selbst ausgelöst |
| Braucht es eine Freigabe? | Ja, je Unternehmen | **Die Handlung *ist* die Freigabe** |
| Fähigkeit im Ledger | `resume.requested/granted/declined` | `resume.visibility:tenant:<firma>` — **gibt es schon** |
| Warum getrennt? | Der jetzige Arbeitgeber darf nicht sehen, dass jemand im Markt ist | Hier gibt es nichts zu verbergen — man bewirbt sich ja |

**Der Satz „das, was dein jetziger nicht sehen soll" gilt nur für die linke
Spalte.** Er auf der Bewerbungsseite wäre Unsinn: wer sich bewirbt, schickt
seinen Lebenslauf mit, das ist der ganze Sinn. Beide Wege bleiben, aber sie
heißen ab jetzt verschieden und werden verschieden erklärt.

---

## Phase 0 — die zwei Dinge, über die du gestolpert bist ✅

- [x] **0.1 Sprachen ohne Token holen.** `SprachenFuerHoechstens` wird
      zweistufig: mit Token 30, ohne Token 10 (60 Anfragen je Stunde, ein Abruf
      kostet dann 11). Die Zahlen bleiben draußen — nur die Menge (ADR-0033).
- [x] **0.2 Sagen, wenn die Menge unvollständig ist.** ADR-0022 §3 verbietet
      stillschweigende Vollständigkeit. Ein Repository, bei dem nur die
      Hauptsprache dasteht, muss das *sagen* — sonst liest sich „Python" wie
      „nur Python". Neues Feld `sprachen_vollstaendig` am Beleg.
- [x] **0.3 Den Freigabetext auseinandernehmen.** Auf `/consents` stehen die
      zwei Wege getrennt, mit dem Satz, der heute fehlt: *„Wenn du dich selbst
      bewirbst, schickst du deine Unterlagen mit — das ist deine Handlung und
      braucht keine Freigabe."*

## Phase 1 — ADRs, bevor Code entsteht ✅

- [x] **1.1 ADR-0034: Das Anschreiben.** Der dritte KI-Verbraucher, und der
      erste, der **speichert** und **überarbeitet**. ADR-0024 verbietet beides —
      aus Gründen, die hier nicht greifen, und das muss dastehen:
      - Gespeichert wird, weil das Anschreiben ein *Dokument* ist, das die
        Person über Tage in mehreren Runden fertigstellt. Der Profilentwurf
        lebt im Formular, weil er ein Vorschlag für ein Feld ist.
      - Überarbeitet wird **nur auf eine ausdrückliche Handlung**. ADR-0024
        verbietet die *Reflexionsschleife* — zwei Aufrufe, um die Worte der
        Person ungefragt umzuschreiben. Hier kommentiert ein Mensch und drückt
        einen Knopf. Das ist keine Schleife, das ist ein Auftrag.
      - Der Kontext trägt **nur die eigenen Daten** der Person und die
        **öffentliche Anzeige**. Nie eine dritte Person.
      - **Nichts geht ohne Freigabe hinaus** — und Freigabe und Versand sind
        zwei Knöpfe, nicht einer.
- [x] **1.2 ADR-0035: Die Bewerbungsmappe.** Ablage-Port zurück (ADR-0021 in
      .NET), die zwei Wege zum Lebenslauf, `unterlagen.granted`, und warum die
      Mappe im Browser gesetzt wird statt als PDF.
- [x] **1.3 ADR-0036: `scout-service`.** Löst `/candidates` ab. Vier Auflagen
      aus `SCOUT-UND-BERATER.md` als Tests, plus die Nachricht aus ADR-0033.
- [x] **1.4 ADR-0037: `advisor-service`.** Mandat als *Sicht* auf Ledger und
      Marktstatus, nicht als zweiter Speicher (ADR-0020 verbietet das).

## Phase 2 — Die Ablage (ADR-0021, zurück in .NET) ✅

- [x] **2.1 `src/shared/WorkerTransfer.Ablage`.** `IAblage` mit
      `LegeAbAsync`/`HoleAsync`/`LoescheAsync`. `HoleAsync` gibt `null` statt zu
      werfen; `LoescheAsync` schweigt über unbekannte Schlüssel.
- [x] **2.2 `LokaleAblage`** — schreiben unter Zwischennamen, dann umbenennen.
      Ein Absturz mittendrin hinterlässt sonst eine halbe Datei unter dem
      richtigen Namen, und die sieht für jeden Leser gültig aus.
- [x] **2.3 `Typerkennung`** — Typ aus den ersten Bytes, nie aus dem, was der
      Aufrufer behauptet. Erlaubt: PNG, JPEG, PDF. Ein `Content-Type` und eine
      Dateiendung sind beide frei wählbar, eine Signatur nicht.
- [x] **2.4 Ein Band in `docker-compose.yml`** und der Pfad aus der Umgebung.
- [x] **2.5 Test:** eine gefälschte Endung (`.png` auf einer EXE) wird
      abgewiesen; Gegenprobe, dass die Prüfung wirklich greift.

## Phase 3 — resume-service: Unterlagen und Vorlage ✅

- [x] **3.1 `Unterlage`** (Aggregat): Wer, Name, Art (`zeugnis|zertifikat|
      sonstiges`), Inhaltstyp, Größe, Ablageschlüssel, Zeitpunkt.
      `Personenzeile` — der Löschwächter muss sie sehen.
- [x] **3.2 Endpunkte:** `POST /resume/documents` (multipart),
      `GET /resume/documents`, `GET /resume/documents/{id}/content`,
      `DELETE /resume/documents/{id}`.
- [x] **3.3 Löschung räumt auch die Ablage.** Zeilen zu löschen und Dateien
      liegen zu lassen wäre eine gebrochene Zusage (ADR-0027).
- [x] **3.4 Vorlagenwahl am Lebenslauf** (`schlicht|klassisch|modern`) — ein
      Wert, kein Layout im Server.
- [x] **3.5 Obergrenzen:** Anzahl und Größe je Datei, begründet.

## Phase 4 — applications-service: der Entwurf und sein Weg ✅

- [x] **4.1 Aggregat `Bewerbungsentwurf`** mit der Zustandsmaschine:
      `Entsteht → Pruefen → (Ueberarbeiten ⇄ Pruefen) → Freigegeben → Gesendet`
      plus `Fehlgeschlagen`. Übergänge im Aggregat, nicht im Endpunkt.
- [x] **4.2 `Anmerkung`** (Kommentar) mit optionalem Zitat der markierten
      Stelle. Offene Anmerkungen verhindern die Freigabe.
- [x] **4.3 Port `IAnschreiber`** mit eigenem Kontexttyp. Test hält die
      Feldliste fest — wie bei `IEntwerfer` (ADR-0024).
- [x] **4.4 Endpunkte:** `POST /applications/drafts` (mehrere Stellen auf
      einmal), `GET`, `PATCH`, `/comments`, `/revise`, `/approve`, `/send`,
      `DELETE`.
- [x] **4.5 Beim Senden:** echte Bewerbung + Ledger + Postausgang.
      **Korrektur am Plan (gemessen):** `Einwilligungsschluessel.Fuer` erteilt
      bereits `profile.visibility:tenant:<firma>` und, wenn mitgeschickt,
      `resume.visibility:tenant:<firma>` — der zweite Weg steht im Code, nur
      nicht im Text. Neu ist allein `documents.visibility:tenant:<firma>`.
- [x] **4.6 Die Mappe für das Unternehmen.** **Korrektur (gemessen):** kein
      eigener Endpunkt nötig — `BewerbungV1` trägt Anschreiben, `subject_id`
      und die Freigaben bereits. Ergänzt wurde `documents` (welche Beilagen
      hinausgingen); Lebenslauf und Inhalte holt die Oberfläche bei
      resume-service, wo der Ledger je Aufruf prüft.
- [x] **4.7 Zurückziehen widerruft die Freigabe.** Stand schon: der Rückzug
      widerruft `Einwilligungsschluessel.Alles(...)` bedingungslos — und
      `Alles` trägt jetzt auch `documents.visibility:tenant:<firma>`.

## Phase 5 — Die Benachrichtigung an das Unternehmen ✅

- [x] **5.1 Neue Art `ApplicationReceived`.** Einzeln abbestellbar wie die
      anderen vier.
- [x] **5.2 Empfänger sind die Mitglieder des Unternehmens** — eine Firma hat
      kein Postfach, Menschen haben eines. Die Mitgliedsliste kommt über einen
      **typisierten Vertrag** von identity-service, nie über ein anonymes
      Objekt (der Draht, der viermal nie ankam).
- [x] **5.3 Der Postausgang bleibt inhaltsfrei** (ADR-0025): Kennung und Art,
      nie ein Name und nie eine Stelle.

## Phase 6 — Oberfläche ✅

- [x] **6.1 `/jobs`: Mehrfachauswahl.** Kästchen an der Stellenkarte, eine
      Leiste „N ausgewählt · Für alle bewerben".
- [x] **6.2 `/applications/drafts`:** die Liste mit Zuständen.
- [x] **6.3 Die Prüfansicht.** Das Anschreiben **wie ein Brief gesetzt** —
      Seitenspiegel, Serifen, Ränder. Text markieren → kommentieren, das Zitat
      reist mit. Knöpfe: Überarbeiten · Freigeben · Senden.
- [x] **6.4 Die Mappe für das Unternehmen** unter `/company/applications/:id`:
      **scrollbare Reiter** — Anschreiben · Lebenslauf · je ein Reiter pro
      Zertifikat. Bilder werden gezeigt, PDF als eingebettetes Dokument.
- [x] **6.5 Lebenslauf-Vorlagen als CSS**, dieselbe Darstellung für die Person
      und für das Unternehmen. Aus `src/styles/tokens/`, keine Farbliterale.
- [x] **6.6 `/resume`: Hochladen** mit Fortschritt, Art und Löschen.
- [x] **6.7 Alle Texte in drei Katalogen** (ADR-0031).

## Phase 7 — Scout und Berater ✅

- [x] **7.1 `scout-service`** nach ADR-0036.
- [x] **7.2 Die Nachricht „dein Profil wurde entdeckt"** (ADR-0033): eigene
      Art, nennt kein Unternehmen, höchstens eine je Person und Tag.
- [x] **7.3 `advisor-service`** nach ADR-0037.

## Phase 8 — Abnahme ✅

- [x] **8.1** `dotnet build`, dann `scripts/test-dotnet.sh` — **getrennt**.
- [x] **8.2** `pnpm check`, `pnpm test`, `pnpm build`.
- [x] **8.3** `make routenkarte` gegen den laufenden Stapel.
- [x] **8.4** E2E **einmal am Ende**, nicht zwischendurch.
- [x] **8.5** CLAUDE.md nachziehen: siebtes Paket in `src/shared/`, die neue
      Fähigkeit, die neue Benachrichtigungsart, die drei neuen ADRs.

---

## Was aus JobPilot übernommen wird — und was nicht

**Übernommen:** die Zustandsmaschine, das Zitat am Kommentar, „Freigabe nur
ohne offene Kommentare", „Versand nur aus freigegeben", das Ausgabeformat
`BETREFF: … --- …` samt robustem Parser.

**Übernommen werden sollte auch die begrenzte Parallelität beim Erzeugen — sie
ist es nicht** (gemessen 11.09.2026). Das ist der einzige Posten aus diesem
Auftrag, der wirklich offen ist, und er steht deshalb im
[PLAN](PLAN-TRANSFERMARKT.md).

**Nicht übernommen:** SMTP-Versand nach außen (wir bleiben plattformintern),
externe Stellenquellen (Arbeitsagentur/Adzuna/Jooble), MCP-Connectoren,
`Profile` als Singleton mit `id=1` (hier gehört ein Profil einer Person), und
das Anhängen *aller* Dokumente per Vorgabe — hier wählt die Person je Bewerbung.
