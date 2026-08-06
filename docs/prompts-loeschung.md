# Prompts für die Umsetzung des Löschrechts (Art. 17)

Drei Prompts, nacheinander in **getrennten frischen Sessions**. Der erste
entscheidet, der zweite baut, der dritte rollt aus. Nicht zusammenlegen — die
Entscheidungen aus Prompt 1 müssen stehen, bevor Code entsteht, sonst wird das
Falsche gelöscht und ist weg.

---

## Prompt 1 — Entscheiden und ADR schreiben (kein Code)

```
Lies docs/ROADMAP.md Abschnitt "10.5 Das Recht auf Löschung ist NICHT
eingelöst" und CLAUDE.md.

Befund: POST /consent/delete nimmt ein Löschverlangen entgegen und schreibt ein
Ereignis ins Ledger (deleted=True), aber KEIN Dienst reagiert darauf. Ein Konto
lässt sich gar nicht löschen. Die Plattform verspricht damit etwas, das nicht
passiert.

Schreibe eine ADR (docs/adr/0027-...), die die Löschung ENTSCHEIDET, aber baue
noch nichts. Beantworte darin belegt, nicht geraten:

1. Was genau wird gelöscht, was anonymisiert, was bleibt? Geh jeden Dienst
   einzeln durch (identity, consent, profile, resume, portfolio, jobs,
   applications, companies, transfer, github) und sag für jeden, welche Zeilen
   fallen und welche bleiben.

2. Aufbewahrungspflichten: Ein Unternehmen mit angenommener Bewerbung oder
   laufendem Transfer muss Unterlagen behalten. Löschen und Aufbewahren-Müssen
   widersprechen sich. Welcher gewinnt, und woran erkennt der Code den Fall?
   Das ist eine RECHTLICHE Frage — wenn du sie nicht aus den vorhandenen
   Dokumenten beantworten kannst, schreib sie als offene Frage an den Nutzer
   auf, statt eine Antwort zu erfinden.

3. Kaskade über zehn Dienste ohne gemeinsame Datenbank (ADR-0004). Die Outbox
   aus ADR-0025 wäre das Werkzeug, aber sie ist "mindestens einmal": doppeltes
   Löschen ist harmlos, eine verpasste Löschung nicht. Wie wird nachgewiesen,
   dass sie überall ankam? Was passiert, wenn ein Dienst dauerhaft nicht
   antwortet?

4. Das Ledger ist der BELEG, dass gelöscht wurde. Es mitzulöschen vernichtet
   den Nachweis. Was bleibt darin stehen, und wie ist das mit Art. 17
   vereinbar?

5. Wie erfährt die Person, dass es fertig ist? Löschung ist nicht sofort.

Halte dich an die Hausregeln: keine Zahl, die einen Menschen zusammenfasst
(ADR-0022); nichts Personenbezogenes in dauerhaften Speichern, die es nicht
braucht (ADR-0025 §5). Schreib auf, was du NICHT baust und warum.
```

---

## Prompt 2 — Bauen (erst nach freigegebener ADR)

```
Setze ADR-0027 (Löschrecht) um. Lies zuerst die ADR und CLAUDE.md.

Vorgehen, nicht verhandelbar:
- Test zuerst, und sieh ihn ROT gegen den ungefixten Code. Ein Test, der nicht
  fehlschlagen kann, bewacht nichts — das ist in dieser Codebasis mehrfach
  passiert.
- Für jede Löschzusage ein Integrationstest, der beweist, dass die Zeilen
  wirklich weg sind — nicht, dass ein Endpunkt 200 sagt.
- Ein Test, der beweist, dass die aufbewahrungspflichtigen Daten NICHT gelöscht
  wurden. Der ist wichtiger als der Löschtest: hier ist der Fehler
  unwiderruflich.
- Die Kaskade läuft über die Outbox (worker-outbox, ADR-0025): record() VOR dem
  Commit in derselben Transaktion.

Achtung, bekannte Fallen dieser Codebasis:
- Ein neues Workspace-Paket oder eine neue Abhängigkeit braucht
  `docker compose build <dienst>`, KEIN restart — sonst ModuleNotFoundError bei
  laufend gemeldetem Container.
- Testreihe und Docker-Build/laufender Stack konkurrieren: `docker compose stop`
  vor `uv run pytest`, sonst brechen Testcontainers mit Fehlern ab, die wie
  kaputter Code aussehen.
- `tests/test_env_examples_are_real.py` schlägt zu, wenn du eine neue Einstellung
  einführst und sie nicht in JEDER apps/*/.env.example dokumentierst.

Am Ende: make check grün, dann Stack hoch und `pnpm --filter
@workertransfer/web run e2e` — und beim E2E-Lauf NICHTS nebenher gegen den
Stack laufen lassen, das erzeugt Wackler, die wie echte Fehler aussehen.
```

---

## Prompt 3 — Oberfläche und Ausrollen

```
Die Löschung ist im Backend umgesetzt (ADR-0027). Baue die Oberfläche und
schließe den Kreis. Lies ADR-0027, CLAUDE.md und docs/frontend.md.

- Eine Seite, auf der eine Person die Löschung ihres Kontos verlangt. Deutsch,
  hartkodiert wie der Rest, `@workertransfer/ui`.
- Der Text muss VOR dem Klick sagen, was gelöscht wird, was bleibt und warum
  (Aufbewahrungspflichten), und dass es nicht sofort geht. Nicht in einer
  Datenschutzerklärung — an der Stelle, wo gedrückt wird. Dieselbe Regel wie
  beim Entwurfsdienst (ADR-0024).
- Löschen ist unwiderruflich: eine bewusste Bestätigung, kein einzelner Klick.
  Aber auch kein Hürdenlauf — wer löschen will, darf das.
- Eine E2E-Reise, die den ganzen Weg geht: verlangen → Kaskade läuft → die
  Daten sind weg → der Nachweis im Ledger steht noch.
- ROADMAP 10.5 von ⛔ auf ✅ und den Eintrag ehrlich schreiben: was gelöscht
  wird, was bleibt, und was noch offen ist.
```

---

## Was der frische Claude wissen muss

- **Branch:** `ai-seam`. Feature → develop → main, nie ein Feature-Branch
  direkt in main.
- **Stand:** Phasen 1–7 und 9 fertig, Phase 10 zu vier Fünfteln, Phase 8
  zurückgestellt (sieben Rechtsfragen).
- **Gates:** 944 Python-Tests, 359 Frontend, 18 E2E — alle grün, null Skips.
  Wenn etwas rot ist, war es vorher grün.
- **Die Hausregel, die am häufigsten gebrochen wurde:** nicht raten, messen.
  Eine Erklärung, die nicht überprüft wurde, ist keine Erklärung.
