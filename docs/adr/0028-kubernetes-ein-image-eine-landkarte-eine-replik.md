# ADR-0028 — Kubernetes: ein Image, eine Landkarte, eine Replik

**Datum:** 08.08.2026
**Status:** angenommen
**Betrifft:** `deploy/`, `docker/web-prod.Dockerfile`, `apps/web/src/env.ts`,
`scripts/k8s-up.sh`, ULTRAPLAN Phase 10 (letzter offener Block)

## Zusammenhang

Phase 10 verlangt „Docker, K8s/Helm". Das Gateway steht (ROADMAP 10.2), OTel ist
sichtbar (10.3), der Auth-Rand ist gebremst (10.1) — es fehlte eine Umgebung,
die dem Ziel gleicht, ohne einen Cloud-Anbieter zu kosten.

Die Anforderung war nicht „Manifeste schreiben". Sie war: eine Umgebung, die
**wirklich läuft**, und die sich von Staging und Produktion nur durch eine
andere `values.yaml` unterscheidet, nicht durch eine andere Struktur.

## Entscheidungen

### 1. Ein Image für zehn Dienste

`docker/service.Dockerfile` unterscheidet die Dienste allein über den Build-Arg
`SERVICE_DIR`, und `docker/entrypoint.sh` liest ihn aus der **Umgebung**. Im
Cluster wird das Image deshalb **einmal ohne diesen Arg** gebaut; der Pod setzt
`SERVICE_DIR` und das uvicorn-Modul.

Zehn Images, die sich in einer Umgebungsvariablen unterscheiden, wären kein
Zehnfaches an Software — nur zehnfacher Platz und zehnfache Ladezeit. Kein
Dockerfile musste dafür geändert werden.

### 2. Die Landkarte bleibt eine Datei

Die K8s-`Service`-Objekte heissen **wie die Compose-Dienste** (`identity-service`
auf 8001 usw.). Dadurch gilt `docker/traefik/dynamic.yml` wortwörtlich weiter,
und das Chart bekommt sie per
`--set-file gateway.dynamicConfig=docker/traefik/dynamic.yml`.

Das ist die wichtigste Entscheidung hier. Die Pfade der Dienste sind **nicht
disjunkt** — `/companies/{id}/members` gehört identity, `/companies/{id}/profile`
gehört companies —, und die Auflösung hängt an `priority`-Werten in dieser
Datei. Sie ein zweites Mal als Ingress-Regeln auszudrücken hiesse, die Landkarte
zu verdoppeln; die Kopie wäre beim ersten neuen Pfad falsch, und zwar still.

Getrennt wird nach Beständigkeit, nicht nach Dateiformat: `dynamic.yml` ist
überall gleich und kommt aus der einen Datei; `traefik.yml` ist
umgebungsabhängig (Dashboard lokal an, in Produktion aus) und wird vom Chart
erzeugt.

Dasselbe gilt für `scripts/initdb/01-create-service-databases.sql` — dieselbe
Datei legt in Compose und im Cluster dieselben zehn Datenbanken an.

`required` statt eines stillen Rückfalls: fehlt eine der beiden Dateien, bricht
das Rendern mit einem Satz ab, der sagt, welche Option fehlt. Ein Chart, das
ohne Routen installiert, wäre schlimmer als eines, das sich weigert.

### 3. Repliken: eins, und das ist eine Entscheidung

`replicaCount: 1`. Nicht „noch nicht skaliert", sondern **darf noch nicht
skaliert werden**. Drei belegte Gründe:

1. **Die Auth-Bremse zählt im Prozess.**
   `worker_platform.presentation.throttle` ist ein gleitendes Fenster ohne
   gemeinsamen Speicher (ROADMAP 10.1). Bei drei Instanzen verdreifacht sich das
   effektive Limit — und niemand sieht es, weil jede einzelne Instanz korrekt
   rechnet.
2. **Der Outbox-Zusteller hat keine Sperre.** `OutboxDispatcher.pending()` ist
   ein `SELECT … ORDER BY created_at LIMIT n` **ohne** `FOR UPDATE SKIP LOCKED`
   (`packages/worker-outbox`). Zwei Zusteller greifen dieselben Zeilen. Für die
   Löschkaskade ist das folgenlos — ADR-0027 §4 verlangt dort ausdrücklich
   Idempotenz —, für Benachrichtigungen heisst es: jede Mail zweimal.
3. **Migriert wird im initContainer**, also je Pod. Bei drei Repliken ist das
   das Rennen, das Phase 10 ausdrücklich vermeiden wollte.

Punkt 2 stand bisher nirgends und ist der härteste: er lässt sich nicht durch
eine Einstellung abstellen, sondern nur durch eine Änderung am Zusteller.

**Wer diese Zahl erhöht, muss vorher alle drei lösen.** Die Begründung steht als
Kommentar an der Zahl selbst, nicht nur hier — eine `3` in einer `values.yaml`
sieht sonst wie eine Zahl aus statt wie eine Verhaltensänderung.

### 4. Migriert wird im initContainer, nicht im Hauptprozess

Das `command:` der Deployments überschreibt den ENTRYPOINT und umgeht ihn damit
vollständig; der Dienst migriert nicht mehr selbst. Ein initContainer ruft
`docker/entrypoint.sh` ausdrücklich auf — es läuft also **derselbe**
Migrationspfad wie in Compose, keine zweite Beschreibung davon.

**Kein Helm-Hook-Job**, obwohl das strukturell sauberer wäre: ein
`pre-install`-Hook läuft, *bevor* das Postgres-StatefulSet aus diesem Chart
existiert. Er hätte keine Datenbank, auf die er warten könnte. Postgres selbst
zum Hook zu machen, nähme ein StatefulSet mit PVC aus Helms Bestandsverwaltung —
ein hoher Preis für eine Reihenfolge, die mit einer Replik ohnehin eindeutig ist.

Vor der Migration wartet eine Schleife auf den Datenbankport. Ohne sie bricht
Alembic mit einem Stapelabzug ab, der nach kaputtem Code aussieht und nur eine
Reihenfolge ist.

### 5. Die Oberfläche ist ein gebautes Artefakt — und braucht dafür eine Laufzeitkonfiguration

Compose fährt `apps/web` als Vite-**Dev**-Server. Eine Staging-Umgebung, die den
Dev-Server prüft, prüft das Falsche. Also `docker/web-prod.Dockerfile`:
`pnpm build`, ausgeliefert von nginx mit SPA-Rückfall auf `index.html`.

Der Rückfall ist kein Detail: ohne ihn beantwortet ein **Neuladen** von
`/profile` ein 404. Der erste Aufruf über einen Link funktioniert, das Neuladen
nicht — und genau diese Hälfte fällt beim Prüfen zuerst durch.

**Der Haken, der die Struktur bestimmt:** `vite build` setzt
`import.meta.env.VITE_*` beim Bauen in das Bündel ein. Ein Image mit
eingebackenen URLs *kann* in zwei Umgebungen nicht dasselbe sein — das
Versprechen „nur eine andere `values.yaml`" wäre für das Frontend gebrochen, und
zwar unsichtbar.

Deshalb löst `apps/web/src/env.ts` jetzt in **drei** Stufen auf:
`window.__WT_CONFIG__` → `VITE_*` → Port-Rückfall. Die Voreinstellung im
Repository (`apps/web/public/config.js`) ist ein leeres Objekt und ändert
nichts; erst das Chart legt eine echte Fassung darüber. `pnpm dev` und
`docker compose` verhalten sich unverändert.

Im Cluster zeigen alle zehn Basis-URLs auf **denselben** Ursprung, den des
Gateways. Das beseitigt zwei Fehlerquellen mit: kein CORS und keine
Cookie-Verwirrung zwischen Ports.

### 6. Das Geheimnis wird gewürfelt und behalten

`WORKER_ENVIRONMENT` ist `staging`, und `assert_deployable_jwt_secret`
(`worker-auth`) verweigert dort genau das eingecheckte Entwicklungs-Geheimnis
aus `docker-compose.yml` — zu Recht, es steht öffentlich in git. Ein Chart, das
diesen Wert setzt, brächte alle zehn Dienste in `CrashLoopBackOff`.

Das Chart würfelt deshalb eines. Entscheidend ist die zweite Hälfte: es liest
per `lookup` das bereits im Cluster liegende Secret zurück, bevor es ein neues
erzeugt. Ohne das würfelte **jedes** `helm upgrade` neu und meldete damit jede
offene Sitzung ab.

In Produktion gehört dort `secrets.existingSecret` hin.

### 7. Kein GitOps, kein TLS, kein HPA

- **Kein ArgoCD/Flux.** Ohne Zielumgebung wäre das Konfiguration, die niemand
  ausführt — genau das Muster, das in diesem Repo sechsmal aufgeräumt werden
  musste (ADR-0021/0022/0024/0025/0026).
- **Kein TLS.** Ohne Domain gäbe es nur ein selbstsigniertes Zertifikat, das
  jeder Browser wegklickt: eine Attrappe, die aussieht wie Sicherheit.
- **Kein HPA, kein PodDisruptionBudget, keine NetworkPolicy.** Die ersten beiden
  setzen Repliken > 1 voraus, und die sind nach §3 gesperrt.
- **Kein Prometheus/Grafana.** OTel ist über Jaeger sichtbar; ein zweiter
  Beobachtungsweg ohne Konsumenten wäre wieder dasselbe Muster.

## Folgen

**Gut:**

- Eine Umgebung, die dem Ziel gleicht, auf einem Rechner und ohne laufende
  Kosten. Der Beweis ist ein Schreibweg — `POST /auth/register` durch das
  Gateway samt Mail in Mailpit —, nicht „`kubectl apply` lief durch".
- Routen und Datenbankanlage haben je **eine** Quelle, die Compose und Cluster
  teilen.
- Ein Wächtertest (`tests/test_k8s_matches_compose.py`) fällt rot, sobald
  `docker-compose.yml` und `values.yaml` auseinanderlaufen. Ohne ihn fehlte ein
  neuer Dienst im Cluster einfach — ohne Fehler, ohne roten Pod.

**Teuer oder unbequem:**

- **Zwei Dockerfiles für dasselbe Frontend.** Sie beantworten verschiedene
  Fragen (Reload beim Entwickeln, Artefakt beim Ausliefern), aber es sind zwei
  Stellen, an denen die Node-Version stimmen muss.
- **Der Cluster kennt keinen Hot-Reload.** Eine Codeänderung heisst: bauen,
  `kind load`, `helm upgrade`. Compose bleibt der schnellere Weg und wird nicht
  ersetzt.
- **`strategy: Recreate`** bedeutet kurze Nichterreichbarkeit beim Upgrade. Mit
  einer Replik ist das ehrlicher als ein rollendes Update, das für einen
  Augenblick zwei Instanzen hinstellt — genau das, was §3 verbietet.
- **CI baut das Chart nicht.** Wie bei den Docker-Images (siehe CLAUDE.md) gilt:
  eine grüne CI beweist über dieses Verzeichnis nichts. `make k8s-lint` prüft
  wenigstens, dass das Chart rendert; ob es *läuft*, sagt nur `make k8s-up`.

## Offen

- **Ein geteilter Zähler für die Auth-Bremse** und **`SKIP LOCKED` im
  Outbox-Zusteller**. Beide sind Voraussetzung für mehr als eine Replik und
  bewusst nicht Teil dieses Schnittes: sie ändern Verhalten im Kern, nicht die
  Auslieferung.
- **Kein `values-production.yaml`.** Es gibt keine Produktionsumgebung, und eine
  Datei mit erfundenen Hosts wäre wieder Konfiguration, die niemand ausführt.
  Die Schalter dafür (`postgres.enabled`, `mailpit.enabled`, `jaeger.enabled`,
  `secrets.existingSecret`, `gateway.dashboard`) sind vorhanden und in
  `values.yaml` benannt.
