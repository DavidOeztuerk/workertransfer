# Prompts für die nächsten Schritte

Stand 07.08.2026. Das Löschrecht ist gebaut (ADR-0027, Commit `90e372c`), die
zugehörigen Prompts sind erledigt und gelöscht.

**Reihenfolge ist nicht beliebig.** Prompt A zuerst — 17 Commits auf einem
Feature-Branch sind das größte Risiko im Projekt, und jeder weitere Schnitt
macht den Merge teurer.

---

## Prompt A — Merge nach develop (zuerst)

```
Der Branch ai-seam trägt 17 Commits gegenüber develop: Phase 7 (KI-Naht +
beide Agenten), Phase 9 (Outbox in allen drei Diensten, Suche geprüft,
Kennzahlen), Phase 10.1–10.5 (Auth-Bremse, Gateway, OTel, Dependency-Scanning,
Löschrecht). Alles grün: make check, 1059 Python-Tests, 366 Frontend, 20 E2E
ohne Wackler.

Bring das nach develop. Regeln dieses Projekts:
- feature → develop → main. NIE ein Feature-Branch direkt nach main.
- Vor dem Merge einmal make check gegen den aktuellen Stand, mit GESTOPPTEM
  Docker-Stapel (`docker compose stop`), sonst konkurrieren Testcontainers mit
  dem Stapel und du siehst Fehler, die keine sind.
- Merge-Konflikte in docs/ROADMAP.md sind wahrscheinlich: nimm beide Seiten
  auf, wirf keinen Eintrag weg. Die Statustabelle und die Abschnitts-
  überschriften müssen danach dasselbe sagen —
  tests/test_roadmap_status_is_consistent.py prüft genau das.
- Nicht mergen, wenn irgendetwas rot ist. Lieber melden als durchdrücken.

Danach: PR von develop nach main öffnen, aber NICHT mergen — das macht der
Nutzer.
```

---

## Prompt B — K8s und Helm gegen einen lokalen Cluster

```
Letzter offener Block von Phase 10. Lies docs/ULTRAPLAN.md Phase 10,
docs/ROADMAP.md Phase 10 und CLAUDE.md.

Ziel: Manifeste und ein Helm-Chart, die WIRKLICH LAUFEN — gegen kind oder
minikube, nicht nur geschrieben. Für Staging und Produktion soll danach nur
eine andere values.yaml nötig sein, nicht eine andere Struktur. Genau so ist
es beim Gateway gemacht (docker/traefik/dynamic.yml als Datei statt als
Compose-Labels) — halte dich an dasselbe Muster.

Was schon steht und wiederverwendet gehört:
- Das Gateway routet bereits, inklusive der nicht-disjunkten Pfade
  (/companies/{id}/members gehört identity, /companies/{id}/profile gehört
  companies). Die Regeln mit ihren priorities stehen in
  docker/traefik/dynamic.yml — sie sind die Landkarte, nicht bloß Konfiguration.
- Jeder Dienst hat einen /health/live-Endpunkt (readiness/liveness).
- Jeder Dienst migriert sich beim Start selbst (docker/entrypoint.sh) — in K8s
  gehört das in einen initContainer oder einen Job, nicht in den Hauptprozess:
  drei Repliken, die gleichzeitig migrieren, ist ein Rennen.
- WORKER_OTLP_ENDPOINT ist verdrahtet; leer heißt aus.

Und die Grenze, die ausdrücklich in ROADMAP 10.1 steht: die Auth-Bremse
(ThrottleMiddleware) zählt IM PROZESS. Bei einer Instanz stimmt die Rechnung,
bei drei verdreifacht sich das effektive Limit. Sobald du Repliken > 1
vorsiehst, ist das eine ENTSCHEIDUNG, die in die ADR gehört — geteilter Zähler
oder Bremse ins Gateway. Nicht stillschweigend skalieren.

Kein GitOps (ArgoCD/Flux) in diesem Schnitt: ohne Zielumgebung wäre das
Konfiguration, die niemand ausführt — genau das Muster, das in diesem Repo
sechsmal aufgeräumt werden musste (ADR-0021/0022/0024/0025/0026).

Beweis am Ende: Cluster hoch, alle Dienste ready, ein Request durchs Gateway
beantwortet. Nicht "kubectl apply lief durch".
```

---

## Prompt C — Rechtsfragen bündeln (wenn du zum Anwalt gehst)

```
Stelle die offenen Rechtsfragen dieses Projekts an EINER Stelle zusammen, damit
sie in einem Termin geklärt werden können. Lies docs/adr/0027-*.md (die eine
offene Frage), docs/ROADMAP.md Phase 8 und
docs/superpowers/specs/2026-08-02-contract-templates-design.md (die sieben
Fragen zu Verträgen und E-Signatur).

Schreib docs/rechtsfragen.md: jede Frage in einem Satz, darunter was am Code
davon abhängt und was passiert, wenn die Antwort so oder anders ausfällt.
Keine Rechtsauskunft erfinden — die Datei ist eine Vorlage für den Termin, kein
Gutachten.

Die Frage aus ADR-0027 ist die mit dem größten Hebel: trifft die Plattform als
Vermittlerin eine EIGENE Aufbewahrungspflicht, oder nur die Unternehmen für
ihre eigenen Unterlagen? Lautet die Antwort "nur die Unternehmen", verschwindet
der Aufbewahrungsschalter ersatzlos.
```

---

## Was der frische Claude wissen muss

- **Branch:** `ai-seam`, 17 Commits vor `develop`. Flow: feature → develop →
  main.
- **Gates:** `make check` grün, 1059 Python-Tests, 366 Frontend, 20 E2E — null
  Skips, null Wackler. Ist etwas rot, war es vorher grün.
- **Der Stapel:** `docker compose up -d`. Gateway auf `:8080`, Traefik-Dashboard
  `:8081`, Jaeger `:16686`, Mailpit `:8025`, Web `:5173`.
- **Offen und begründet:** eine Rechtsfrage (ADR-0027), §3.4 und ein
  Audit-Ereignis (ROADMAP 10.5), starlette-CVEs (warten auf FastAPI 1.x —
  Dependabot meldet es), Phase 8 (sieben Rechtsfragen).

### Vier Fallen, die in diesem Repo Zeit gekostet haben

1. **Neue Abhängigkeit → `docker compose build <dienst>`**, kein restart. Sonst
   `ModuleNotFoundError`, während `docker compose ps` den Dienst als laufend
   meldet.
2. **Nie testen, während der Stapel läuft oder ein Build läuft.** `docker
   compose stop` vor `uv run pytest` — sonst brechen Testcontainers mit Fehlern
   ab, die wie kaputter Code aussehen (einmal: 67 Fehler in 35 Minuten statt
   946 grün in 8).
3. **Während eines E2E-Laufs nichts nebenher gegen den Stapel fahren** — keine
   `psql`-Abfragen, keine Log-Greps. Das erzeugt Wackler, die wie echte Fehler
   aussehen.
4. **Wächtertests schlagen zu und haben recht:**
   `test_env_examples_are_real.py` (neue Einstellung in JEDER
   `apps/*/.env.example`), `test_workspace_dependencies.py` (Import ohne
   Deklaration), `test_roadmap_status_is_consistent.py` (Tabelle vs.
   Überschrift).

### Die Hausregel, die am häufigsten gebrochen wurde

**Nicht raten, messen.** Eine Erklärung, die nicht überprüft wurde, ist keine
Erklärung. Zwei Fehler dieser Woche waren nur am laufenden System sichtbar: ein
Erfolgszustand, den kein Nutzer erreichen konnte, und ein Zusteller, der
Löschungen auf einem ruhigen System minutenlang verschlief. Beide hätte man
durch Hochsetzen einer Zeitgrenze „behoben" — und damit den Fehler als normales
Verhalten festgeschrieben.
