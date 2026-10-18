# Drittanbieter — was angebunden ist und was noch kommt

**Stand:** 18.10.2026 · **Zweck:** eine Liste aller fremden Systeme, mit denen
WorkerTransfer spricht oder sprechen soll, damit vor dem Start nichts
vergessen wird und nach dem MVP klar ist, was die Erfahrung für Menschen und
Firmen verbessert.

**Regeln, die für jede Zeile gelten:**

- **Erst messen, dann bauen.** Jede Schnittstelle wird gegen die echte Gegenseite
  gemessen, bevor Code entsteht; was gemessen ist, steht im zugehörigen ADR.
- **Nur offizielle Wege.** Kein Scraping, nichts gegen Nutzungsbedingungen
  (ADR-0004, ADR-0052). Ob die Bedingungen eines Anbieters unsere Nutzung
  tragen, prüft der Jurist (J14 und folgende).
- **Jeder Host steht in der Konfiguration.** Die Egress-Grenze lässt nur durch,
  was dort genannt ist; ein neuer Anbieter braucht seinen Eintrag in compose,
  im Chart und in `.env.example`.
- **So wenig wie möglich geht hinaus.** Ein fremdes Konto liefert nie mehr als
  das, wofür es angebunden ist; kein Schlüssel und kein Token wird länger
  gehalten, als der Schritt dauert, wo das geht.

Legende: **gebaut** · **MVP offen** (Phase 6 der Bauliste) · **vor dem Start**
· **nach dem MVP** · **nie**

## 1. Stellen aus dem Bewerbermanagement (ATS)

| Anbieter | Stand | Weg | Bemerkung |
|---|---|---|---|
| Greenhouse | **gebaut** (ADR-0052) | öffentliche Job Board API | Bindung über einen Code im Inserat |
| Lever | **gebaut** (ADR-0052) | öffentliche Postings API | Bindung über einen Code im Inserat |
| Personio | **gebaut** (ADR-0052) | öffentlicher XML-Feed | Beschreibungen oft leer; dann verweist das Inserat auf die Quelle |
| Eigener Feed | **gebaut** (ADR-0052) | JSON-Liste von schema.org `JobPosting` auf der eigenen Domain | gebunden durch den Ort |
| Recruitee, SmartRecruiters, Teamtailor, Workable, JOIN, softgarden, d.vinci, rexx, BITE, Workday, SAP SuccessFactors | **nach dem MVP** | jeweils die offizielle öffentliche Schnittstelle, falls vorhanden | einzeln messen; nicht jedes hat eine öffentliche Leseschnittstelle |
| Echter Login je ATS (Greenhouse Harvest, Lever OAuth, Personio API) | **nach dem MVP** | API-Schlüssel bzw. OAuth der Firma | ersetzt den Code und öffnet den Rückweg; Lever gibt OAuth-Apps selbst frei |
| Rückweg ins ATS (Bewerbung landet im ATS der Firma) | **nach dem MVP** (N3) | über den echten Login | heute laufen Bewerbungen über die Plattform (L5) |
| Automatische Bindung, wo das Board auf die Domain zeigt | **nach dem MVP** | Greenhouse `absolute_url` | gemessen: Airbnb, Stripe ja; GitLab nein |
| Jobbörse der Bundesagentur | **nie** | — | keine offizielle Schnittstelle; bundesAPI ist eine inoffizielle Sammlung |

## 2. KI-Modelle

| Anbieter | Stand | Wofür | Bemerkung |
|---|---|---|---|
| Ollama (lokal) | **gebaut**, Plattform-Standard bis zum Start (ADR-0051) | alle KI-Hilfen | `make ai-check` prüft den echten Weg |
| Anthropic | **gebaut** — eigener Schlüssel einer Person | ihre KI-Hilfen statt des Plattform-Modells | Adresse setzt die Plattform, nie die Person |
| OpenAI | **gebaut** — eigener Schlüssel einer Person | wie oben | wie oben |
| Plattform-Anbieter im Betrieb (Kandidaten: Anthropic, Mistral als EU-Anbieter) | **vor dem Start** | Modell der Plattform | mit dem Geschäftsmodell: Abo oder Kontingent, ohne dass ein Anschreiben am leeren Kontingent abbricht |
| KI-Schlüssel der Firma | **nach dem MVP** | Inserat-Hilfe, Scout, Antworten der Firma | heute nur das Plattform-Modell |
| Gemini, Ollama Cloud, Hugging Face | **nach dem MVP** | weitere eigene Schlüssel | je Anbieter Adresse festlegen und messen |

## 3. Anmeldung

| Anbieter | Stand | Bemerkung |
|---|---|---|
| Google, Microsoft, Apple | **MVP offen** (N3) | vorher ein ADR: wo die Anmeldung lebt, und dass ein fremdes Konto nie mehr liefert als Name und E-Mail |
| LinkedIn | **nach dem MVP** (N1) | |
| Keycloak (für die Noelia Control Plane) | **vor dem Start** (I2, Q4) | heute läuft sie nur lokal ohne Identitätsanbieter |

## 4. Profile, Nachweise, Dateien

| Anbieter | Stand | Bemerkung |
|---|---|---|
| GitHub | **gebaut** (ADR-0033) | eigene, bewiesene Verbindung; OAuth oder Gist |
| XING | **MVP offen** (N2) | Link im Profil; Import aus dem eigenen Export erst nach Messung |
| LinkedIn-Import aus dem eigenen Export | **nach dem MVP** (N1) | |
| Google Drive, OneDrive, Dropbox | **MVP offen** (N3) | nur die gewählte Datei, kein Ordnerzugriff; Hosts in der Egress-Grenze |
| Open Badges / Credly, GitLab | **nach dem MVP** (N3) | |

## 5. Kommunikation und Termine

| Anbieter | Stand | Bemerkung |
|---|---|---|
| E-Mail-Versand im Betrieb (SMTP-Anbieter) | **vor dem Start** | lokal Mailpit; im Betrieb ein Versanddienst, möglichst in der EU |
| Bewerbungen aus dem eigenen Postfach | **nach dem MVP** (P1) | |
| Videotermin-Link, Termine als `.ics` | **nach dem MVP** (E1, N3) | eigene kleine Lösung statt Kalender-Anbindung |

## 6. Geld

| Anbieter | Stand | Bemerkung |
|---|---|---|
| Zahlungsanbieter für Abo oder Kontingent | **vor dem Start** | hängt am Geschäftsmodell (G1, G2, Q1) — bewusst der letzte Punkt |
| Treuhand für die Ablöse zwischen Firmen | **nach dem MVP** (C4) | nur über einen lizenzierten Zahlungsanbieter; heute bewegt die Plattform kein Geld |
| kununu als Gehaltsquelle | **nach dem MVP** | nur per Partnervertrag |

## 7. Betrieb

| System | Stand | Bemerkung |
|---|---|---|
| Hosting | **vor dem Start** (Q2, Q3) | wer betreibt, wo läuft es |
| Infisical | **vor dem Start** | füllt die Umgebung; der Mechanismus bleibt `.env` |
| Noelia Control Plane | **gebaut** lokal, Lizenz **vor dem Start** | |
| Valkey, Postgres, Jaeger | **gebaut**, selbst betrieben | keine fremden Dienste |
| Geokodierung | **nie** fremd (ADR-0032) | eigener Ortsdatensatz statt eines fremden Geokoders |
