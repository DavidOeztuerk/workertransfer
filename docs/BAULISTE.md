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

| Was | Frage | Größe | Bemerkung |
|---|---|---|---|
| **Alter Arbeitgeber als Nutzer**: Einstellung „Anfragen zu unserem Personal zulassen“ | C1, D1 | M | unauffällig, ohne „Insolvenz“ |
| **Zustimmung der Person** zum Weg über den Arbeitgeber | C1 | M | ohne sie entsteht „arbeitet bei X“ nie |
| **Ablöse-Verhandlung** Firma ↔ Firma, Person stimmt dem Ergebnis zu | C3, D1 | L | beide sehen die Restzeit, keinen Preisvorschlag |
| Feld „Ablöse“ aus dem Angebot an die Person **entfernen** | C3 | S | heute sinnlos, weil der alte Arbeitgeber nie Teil ist |
| **Zeugnis anfordern** über den Berater | C2 | M | Endzeugnis nach Kündigung, Zwischenzeugnis bewusst |

## Phase 5 — Verträge

| Was | Frage | Größe | Bemerkung |
|---|---|---|---|
| **Vertragsvorlagen**, automatisch gefüllt | F1 | L | **blockiert**: die Vorlagen schreibt oder gibt ein Jurist frei (J7) |

## Neue ADRs, die mit dem Bau nötig werden

- ~~Der Berater ist ein Agent der Person~~ — ADR-0049, 08.10.2026.
- **Der alte Arbeitgeber darf vorkommen, mit zwei Zustimmungen** — widerspricht
  bewusst dem Transfer-Entwurf vom 02.08.2026 („die Plattform kontaktiert den
  Arbeitgeber nicht“).
- ~~Nachrichten und Agenten-Entwürfe werden gespeichert~~ — ADR-0047, 07.10.2026.

## Vor dem Start (siehe `VISION-FRAGEN.md`)

- Geschäftsmodell (G1, G2)
- Control Plane im Betrieb: Keycloak, echte Lizenz (I2)
- Arbeitgeber gibt Personal aktiv ab (C1)
- Juristen-Liste J1–J12, dazu Impressum, Datenschutzerklärung, Nutzungsbedingungen

## Später

- Termine im Gespräch (E1-C): eigene kleine Lösung mit `.ics`
- Treuhand für die Ablöse (C4-B), über einen lizenzierten Zahlungsanbieter
- kununu als Gehaltsquelle, nur per Partnervertrag
- Seite „Meine Sitzungen → diese abmelden“ — Noelia 7.0.1 trägt den Sitzungswiderruf, die Seite fehlt

## Nicht im Katalog behandelt — nächste Fragerunde

Aus dem ursprünglichen Auftrag (kon.txt, Z. 896), weder gebaut noch geklärt:

- **Karriereseiten-Anbindung:** Inserate automatisch von der Firmenwebsite holen
  (nur über offizielle Schnittstellen, ADR-0004).
- **Subdomain der Firma** mit einer persönlichen Seite für jeden Bewerber.
- **LinkedIn / Xing** und weitere Verbindungen.
- **Weitere Agenten** aus dem Vorschlag damals (Interview-Trainer,
  Lebenslauf-Optimierer, Lern-Coach …) — welche davon gewollt sind.
