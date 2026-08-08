# Prompts für die nächsten Schritte

Stand 08.08.2026. Erledigte Prompts werden hier gelöscht, nicht abgehakt — eine
Liste mit Häkchen liest sich irgendwann wie eine Liste mit Aufgaben.

**Erledigt und deshalb entfernt:**

- *Prompt A — Merge nach develop.* Eingelöst: `ai-seam` steckt in `origin/develop`
  (0 Commits Abstand), und develop ist über PR #50 in `main`. Der Remote-Zweig
  `ai-seam` steht noch da und darf weg.
- *Prompt B — K8s und Helm gegen einen lokalen Cluster.* Eingelöst mit
  **ADR-0028** und ROADMAP 10.6: `make k8s-up` fährt einen kind-Cluster hoch und
  belegt ihn (15 Pods bereit, null Neustarts, `GET /jobs` → 200,
  `POST /auth/register` → 201 samt Mail in Mailpit).

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

## Prompt D — Mehr als eine Replik (erst wenn es gebraucht wird)

```
ADR-0028 §3 sperrt replicas > 1, mit drei Gründen. Alle drei sind Änderungen am
Code, nicht an einer values.yaml — deshalb ist das ein eigener Schnitt und kein
Feineinstellen:

1. Die Auth-Bremse (worker_platform.presentation.throttle) zählt IM PROZESS.
   Entweder ein geteilter Zähler (Redis) oder die Bremse ins Gateway. Beides ist
   eine Entscheidung mit eigener Abwägung: Redis wäre die erste Infrastruktur
   seit ADR-0025, die wieder dazukommt.
2. OutboxDispatcher.pending() hat KEIN "FOR UPDATE SKIP LOCKED". Zwei Zusteller
   greifen dieselben Zeilen — für die Löschkaskade folgenlos (Idempotenz ist
   dort zugesichert), für Benachrichtigungen heißt es: jede Mail zweimal.
3. Migriert wird im initContainer, also je Pod. Bei drei Repliken ist das das
   Rennen, das Phase 10 vermeiden wollte. Dann braucht es einen Job mit einer
   Reihenfolge, die auch mit einer Datenbank AUS demselben Chart funktioniert
   (der naheliegende pre-install-Hook tut das nicht — er läuft, bevor Postgres
   existiert).

Nicht die Zahl in values.yaml erhöhen und schauen, was passiert. Punkt 2 fällt
in keinem Test auf und in keiner Oberfläche — nur bei jemandem, der zwei Mails
bekommt.
```

---

## Was der frische Claude wissen muss

- **Zweige:** `feature → develop → main`. `main` und `develop` sind aktuell
  deckungsgleich bis auf den Merge-Commit von PR #50.
- **Gates:** `make check` (sechs Schritte, fail-fast) oder `make validate`
  (läuft durch, nennt Übersprungenes und zählt die Tests). Ein grüner Lauf mit
  zwanzig Skips ist kein grüner Lauf.
- **Zwei Stapel, zwei Fragen:** `docker compose up -d` ist der schnelle
  Entwicklungsweg (Bind-Mount, Reload) — Gateway `:8080`, Traefik-Übersicht
  `:8081`, Jaeger `:16686`, Mailpit `:8025`, Web `:5173`. `make k8s-up` ist die
  Staging-Nachbildung auf kind — Anwendung auf **`:8090`**, Mailpit ebenfalls
  auf `:8025`, gebautes Frontend-Artefakt, kein Reload. Nie beide gleichzeitig.
- **Anmelden geht nur über Mailpit:** Registrierung legt das Konto `PENDING` an,
  der Bestätigungslink liegt unter `:8025`. Ohne ihn kommt man in keine
  Umgebung hinein.
- **Offen und begründet:** eine Rechtsfrage (ADR-0027), §3.4 und ein
  Audit-Ereignis (ROADMAP 10.5), starlette-CVEs (warten auf FastAPI 1.x —
  `dependency-audit` meldet sie bei jedem PR; `dependabot.yml` ist seit
  08.08.2026 gelöscht, siehe ROADMAP 10.4), Phase 8 (sieben Rechtsfragen),
  Repliken > 1 (ADR-0028 §3, siehe Prompt D).

### Fünf Fallen, die in diesem Repo Zeit gekostet haben

1. **Neue Abhängigkeit → `docker compose build <dienst>`**, kein restart. Sonst
   `ModuleNotFoundError`, während `docker compose ps` den Dienst als laufend
   meldet.
2. **Nie testen, während ein Stapel läuft oder ein Build läuft.** `docker
   compose stop` bzw. `docker stop workertransfer-control-plane` vor
   `uv run pytest` — sonst brechen Testcontainers mit Fehlern ab, die wie
   kaputter Code aussehen (einmal: 67 Fehler in 35 Minuten statt 946 grün in 8).
3. **Während eines E2E-Laufs nichts nebenher gegen den Stapel fahren** — keine
   `psql`-Abfragen, keine Log-Greps. Das erzeugt Wackler, die wie echte Fehler
   aussehen.
4. **Wächtertests schlagen zu und haben recht:**
   `test_env_examples_are_real.py` (neue Einstellung in JEDER
   `apps/*/.env.example`), `test_workspace_dependencies.py` (Import ohne
   Deklaration), `test_roadmap_status_is_consistent.py` (Tabelle vs.
   Überschrift), `test_k8s_matches_compose.py` (Dienst in Compose, aber nicht
   im Chart — im Cluster fehlt er dann einfach, ohne roten Pod).
5. **Kubernetes-Prüfungen: `timeoutSeconds` steht voreingestellt auf EINE
   Sekunde.** Eine Bereitschaftsprüfung, die die Datenbank fragt, schafft das
   beim gleichzeitigen Hochlauf nicht — drei laufende Dienste wurden deshalb
   neu gestartet, und jede Meldung lautete `timed out after 1s`. Im Chart ist
   die Grenze jetzt überall gesetzt; wer eine Prüfung ergänzt, muss sie
   mitsetzen.
6. **Eine Oberfläche hinter demselben Ursprung wie die API prüft man mit einem
   DIREKTLINK, nicht mit einem Klick.** `/jobs` ist beides — Seite und
   API-Präfix. Ein Klick im Programm funktioniert immer (der Router schaltet im
   Browser um, ohne zu fragen); nur das Eintippen der Adresse und F5 gehen
   wirklich durchs Gateway. Genau diese Hälfte lieferte rohes JSON, und genau
   diese Hälfte macht beim Entwickeln niemand. Gelöst über
   `Sec-Fetch-Dest: document` in `docker/traefik/dynamic.yml`.

### Die Hausregel, die am häufigsten gebrochen wurde

**Nicht raten, messen.** Eine Erklärung, die nicht überprüft wurde, ist keine
Erklärung. Drei Fehler dieser Woche waren nur am laufenden System sichtbar: ein
Erfolgszustand, den kein Nutzer erreichen konnte; ein Zusteller, der Löschungen
auf einem ruhigen System minutenlang verschlief; und drei Kubernetes-Dienste,
die neu gestartet wurden, obwohl sie liefen. Die ersten beiden hätte man durch
Hochsetzen einer Zeitgrenze „behoben" — und damit den Fehler als normales
Verhalten festgeschrieben. Beim dritten war das Setzen einer Zeitgrenze die
richtige Antwort, weil überhaupt keine da war. Den Unterschied sieht man nur,
wenn man vorher misst.
