# Bauliste: von heute zur Vision

Stand 07.10.2026. Vergleicht [`VISION.md`](VISION.md) mit dem, was im Baum
steht. Jede Zeile nennt die Katalogfrage, aus der sie kommt. Die Reihenfolge
folgt den Abhängigkeiten: was weiter oben steht, brauchen die Zeilen darunter.

Größe: **S** = ein Tag, **M** = wenige Tage, **L** = eine Woche und mehr.

## Schon erledigt

| Was | Frage | Stand |
|---|---|---|
| Widerruf in allen Diensten (Valkey) | I1 | gebaut 07.10.2026 |
| Sitzungen 8 Std. / 7 Tage, Abmelden in allen Tabs | K | gebaut 07.10.2026 |
| Prüfspur für alle Löschempfänger | — | gebaut 03.10.2026 |
| Anschreiben mit Freigabe vor dem Senden (das Vorbild für alle Agenten) | D2 | gebaut (ADR-0034) |
| Scout mit Häkchen, ohne Rangfolge, nach festen Regeln | B1 | gebaut (ADR-0036) — fehlt: pro Inserat, siehe Phase 2 |
| Lebenslauf-Vorlage, Bewerbung auf Inserat | B2 | gebaut |
| **Nachrichten pro Vorgang** (Bewerbung) mit Entwurf und Freigabe | E1, D2 | gebaut 07.10.2026 (ADR-0047, `messages-service`) |
| **Platzhalter-Schicht**, Anschreiben darauf umgestellt | E2 | gebaut 07.10.2026 (`ServiceDefaults/Ai/Placeholders.cs`) |
| **Mistral als Standard-Anbieter**, Ollama bleibt | E2 | gebaut 07.10.2026 (`Draft__Provider`, `ModelWire`) |
| Hinweis „keine Gesundheitsdaten" über jedem Nachrichtenfeld | E2 | gebaut 07.10.2026 — immer, nicht erst wenn ein Berater mitliest |

## Phase 1 — Grundlagen, ohne die kein Agent arbeiten kann

**Erledigt am 07.10.2026**, siehe oben. Offen aus Phase 1 bleibt nur, was erst
mit seiner Phase kommt: weitere Vorgangsarten für Nachrichten (Ansprache durch
den Recruiter in Phase 2, Gespräch mit dem Berater in Phase 3, Transfer in
Phase 4) — jede mit ihrer eigenen Antwort auf „wer gehört dazu“, im Dienst des
Vorgangs.

## Phase 2 — Firmenseite

**Erledigt am 07.10.2026.**

| Was | Frage | Stand |
|---|---|---|
| **Rolle „Recruiter“** | B7 | gebaut (ADR-0048): geordnet member < recruiter < admin; der erste Kontakt braucht mindestens recruiter |
| **Gehaltsspanne im Inserat** | B5 | gebaut: freiwillig, ohne Angabe „Gehalt nicht angegeben“; Pflicht oder nicht ist J13 |
| **KI-Scout pro Inserat** | B1 | gebaut: „Passende Personen“ am veröffentlichten Inserat, dieselbe Häkchen-Suche, feste Regeln |
| **KI-Recruiter** | B2, D2 | gebaut: Entwurf der Ansprache im Scout, Freigabe öffnet die Ansprache in messages-service; `/messages` für beide Seiten |

Nebenbei gefunden und behoben: Inserate kamen ohne Arbeitsort-Modus und
Anstellungsart an (falsche Feldnamen), das Anschreiben hatte im Cluster keinen
Kontext, die Suchtür schnitt still bei zehn Fähigkeiten ab.

## Phase 3 — Personenseite

| Was | Frage | Stand |
|---|---|---|
| **Eigenes Zeitfenster** im Marktstatus | H1 | gebaut 07.10.2026: außerhalb nicht ansprechbar, Daten sieht nur die Person |
| **Kündigungsfrist** → frühester Wechsel, Angebote markieren | H2 | gebaut 07.10.2026: nur für die Person; nebenbei Angebotsbeginn als Monat (war ein 500) |
| **Checklisten** (Kündigung, Aufhebung, Sperrzeit, Zeugnis, Resturlaub) | B6, F2 | gebaut 08.10.2026: `/checklists`, öffentlich, neutrale Stellen |
| **Initiativbewerbung** | B2 | gebaut 08.10.2026: dritte Gesprächsart, Senden gibt das Profil der Firma frei |
| Gehaltsvergleich: Inserate | B5 | gebaut 08.10.2026: `/salaries`, jede Spanne einzeln mit Quelle, kein Durchschnitt |
| Gehaltsvergleich: **Destatis GENESIS** | B5 | **offen** — die Schnittstelle braucht ein Konto, und die zwei Dokumentationen widersprechen sich (Gast per GET / Token per POST). Erst gegen einen echten Zugang messen, dann bauen. Bis dahin verlinkt die Seite die Veröffentlichung. |
| **KI-Berater** (Gehalt, Vertrag erklären, Wechsel begleiten) | B4, B5 | erste Fassung gebaut 08.10.2026 (ADR-0049): spricht zur Person über ein Gespräch, liest ihr Mandat und ihren frühesten Wechsel; nach außen über Entwurf und Freigabe. Offen: im Namen der Person verhandeln. |

## Phase 4 — Der alte Arbeitgeber und die Ablöse

**Erledigt am 16.10.2026** (ADR-0050). Offen bleibt, was D1 als dritten Weg
nennt: Person ↔ alter Arbeitgeber auf der Plattform (Aufhebung).

| Was | Frage | Stand |
|---|---|---|
| **Alter Arbeitgeber als Nutzer**: Einstellung „Anfragen anderer Firmen zu unserem Personal zulassen“ | C1, D1 | gebaut 16.10.2026 (ADR-0050): `admin`, nirgends öffentlich, kein „Insolvenz“; eine Liste „Anfragen zu eurem Personal“ zeigt eine Person erst, wenn eine Firma fragt |
| **Zustimmung der Person** zum Weg über den Arbeitgeber | C1 | gebaut 16.10.2026: auf der Karriereseite des Arbeitgebers, **nennen = zustimmen** (`employer.route:tenant:<X>` im Register); schließen löscht „arbeitet bei X“ |
| **Ablöse-Verhandlung** Firma ↔ Firma, Person stimmt dem Ergebnis zu | C3, D1 | gebaut 16.10.2026: Monat + Ablöse, abwechselnd, angenommen wird von der anderen Seite; beide sehen den frühesten regulären Wechsel der Person; kein Preisvorschlag |
| Feld „Ablöse“ aus dem Angebot an die Person **entfernen** | C3 | gebaut 16.10.2026; „bezahlt“ (ADR-0027 §3) heißt jetzt „vereinbarte Ablöse“ |
| **Zeugnis anfordern** über den Berater | C2 | gebaut 16.10.2026: Musterschreiben auf `/checklists`, im Browser gefüllt, nichts gespeichert oder gesendet; Zwischenzeugnis erst nach dem Haken zum Risiko |

## Phase 5 — Verträge

| Was | Frage | Größe | Bemerkung |
|---|---|---|---|
| **Vertragsvorlagen**, automatisch gefüllt | F1 | L | **blockiert**: die Vorlagen schreibt oder gibt ein Jurist frei (J7) |

## Phase 6 — Runde 2, was ins MVP gehört (`VISION-FRAGEN-2.md`, 17.10.2026)

| Was | Frage | Größe | Bemerkung |
|---|---|---|---|
| **Karriereseiten-Anbindung**: offizielle ATS-Abrufe (Greenhouse, Lever, Personio-XML, Recruitee …) und ein eigener Feed der Firma | L1 | L | jeder Anbieter erst gegen die echte Schnittstelle gemessen, dann gebaut; kein Scraping |
| **Domain-Nachweis** per DNS-TXT-Eintrag | L2 | M | trägt später auch die Subdomain der persönlichen Seite (M3) |
| Abruf täglich + „jetzt abholen“; pro Quelle Entwurf oder direkt (Vorgabe Entwurf); an der Quelle verschwunden → hier geschlossen | L3, L4 | M | Bewerbungen auf ein geschlossenes Inserat bleiben |
| Bewerbung auf geholte Inserate **über die Plattform** | L5 | S | Mappe, Anschreiben-Agent und Einwilligungen gelten wie sonst |
| **XING**: Link im Profil; Import aus dem eigenen Export nach Messung | N2 | S | |
| **Anmelden mit Google / Microsoft / Apple** | N3 | M | eine Entscheidung vorab: in identity-service selbst oder über den Identitätsanbieter aus Q4 — ADR |
| **Dokumente aus Drive / OneDrive / Dropbox** | N3 | M | nur die gewählte Datei, kein Ordnerzugriff; Ziel-Hosts in der Egress-Grenze deklarieren |
| **Anschreiben-Vorlagen** passend zu den Lebenslauf-Vorlagen | P2 | M | |

## Neue ADRs, die mit dem Bau nötig werden

- ~~Der Berater ist ein Agent der Person~~ — ADR-0049, 08.10.2026.
- ~~Der alte Arbeitgeber darf vorkommen, mit zwei Zustimmungen~~ — ADR-0050,
  16.10.2026.
- ~~Nachrichten und Agenten-Entwürfe werden gespeichert~~ — ADR-0047, 07.10.2026.
- ~~Ein KI-Zugang: Plattform-Modell und eigener Schlüssel~~ — ADR-0051,
  17.10.2026.
- **Inserate von außen**: nur offizielle Abrufe, Nachweis per DNS — ergänzt
  ADR-0004 (kein Scraping) um den erlaubten Weg.
- **Anmelden über fremde Konten**: wo die Anmeldung lebt, und dass ein
  fremdes Konto nie mehr liefert als Name und E-Mail.

## Vor dem Start (siehe `VISION-FRAGEN.md` und `VISION-FRAGEN-2.md`)

- Geschäftsmodell (G1, G2, Q1) — **bewusst zurückgestellt, letzter Punkt**;
  dazu der KI-Anbieter der Plattform im Betrieb und ob Menschen ihn per Abo
  oder Kontingent bekommen (ADR-0051) — ohne dass ein Anschreiben am leeren
  Kontingent abbricht
- Wer betreibt die Plattform (Q2) und wo läuft sie (Q3) — offen
- Control Plane im Betrieb: Keycloak, echte Lizenz (I2, Q4)
- Juristen-Liste J1–J17, **gesammelt vor dem Start** (Q7), dazu Impressum,
  Datenschutzerklärung, Nutzungsbedingungen

## Nach dem MVP

- **Persönliche Seite** für eine Person unter der Firmen-Subdomain (M):
  Richtung steht — sofort unter WorkerTransfer, Subdomain optional (M3);
  geheimer Link ohne Namen, Persönliches nach Anmeldung (M4); was darauf
  steht (M2) und ob die Firma Aufrufe sieht (M5) wird dann entschieden
- **LinkedIn** (N1): Import aus dem eigenen Export, Link, Anmelden mit LinkedIn
- Open Badges / Credly, GitLab, Videotermin-Link, Rückweg ins ATS (N3)
- Stellendaten von der Firmenwebsite (L1-B), Bewerbungsweg pro Quelle (L5-C)
- **MCP** (N4): zuerst für die Person zu ihren eigenen Daten, dann für Firmen
  nur mit Firmendaten
- **Agenten** (O1): Interview-Trainer, Lebenslauf-Optimierer, Lern-Coach,
  Dokumentenmanager, Portfolio-Helfer, Karriere-Coach; für Firmen
  Interview-Vorbereitung, Angebots-Entwurf, Personalplanung
- Agenten von Drittanbietern, mit Prüfung (O3)
- **KI-Schlüssel der Firma** für Inserat-Hilfe, Scout und Antworten der Firma;
  weitere Anbieter für eigene Schlüssel (Gemini, Ollama Cloud, Hugging Face)
  (ADR-0051)
- Bewerbungen per E-Mail aus dem **eigenen Postfach** (P1)
- Arbeitgeber gibt Personal aktiv ab (C1, Q5) — Richtung steht
- Gespräch Person ↔ alter Arbeitgeber über den geöffneten Weg (D1, Q6)
- Termine im Gespräch (E1-C): eigene kleine Lösung mit `.ics`
- Treuhand für die Ablöse (C4-B), über einen lizenzierten Zahlungsanbieter
- kununu als Gehaltsquelle, nur per Partnervertrag
- Seite „Meine Sitzungen → diese abmelden“ — Noelia 7.0.1 trägt den Sitzungswiderruf, die Seite fehlt

## Nie (O2, bestätigt 17.10.2026)

- Candidate Ranking, Match Score, Wechselwahrscheinlichkeit (ADR-0022)
- Gehaltsempfehlung über eine Person an die Firma
- Interview-Agent, der Bewerber bewertet (Hochrisiko-KI, D3)
- Team-Analyse über Personen
