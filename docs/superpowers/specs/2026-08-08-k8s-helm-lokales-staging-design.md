# K8s und Helm — eine Staging-Umgebung auf dem eigenen Rechner

**Stand 08.08.2026.** Letzter offener Block von Phase 10
([ULTRAPLAN](../../ULTRAPLAN.md) Phase 10, [ROADMAP](../../ROADMAP.md) Phase 10).

Ziel ist nicht „Manifeste existieren". Ziel ist eine Umgebung, die **wirklich
läuft** — auf `kind`, ohne Cloud-Anbieter, ohne Domain, ohne Kosten — und die
sich von Staging und Produktion nur durch eine andere `values.yaml`
unterscheidet, nicht durch eine andere Struktur. Dasselbe Muster wie beim
Gateway (ADR-0025-Zeitraum, ROADMAP 10.2): Konfiguration als Datei, die
weiterzieht, nicht als Compose-Labels, die es woanders nicht gibt.

---

## 1. Was schon steht und wiederverwendet wird

| Vorhanden | Wie es im Cluster weiterlebt |
|---|---|
| `docker/service.Dockerfile` | unverändert — **ein** Image für alle zehn Dienste |
| `docker/entrypoint.sh` | unverändert — migriert, dann `exec`; im Cluster nur noch im Migrations-Job |
| `docker/traefik/dynamic.yml` | unverändert, per `--set-file` ins Chart — **eine** Landkarte |
| `scripts/initdb/01-create-service-databases.sql` | unverändert, per `--set-file` in Postgres |
| `/health/live` + `/health/ready` | liveness- bzw. readinessProbe |
| `WORKER_OTLP_ENDPOINT` | leer heißt aus, sonst `http://jaeger:4317` |

Die Zeile mit `dynamic.yml` ist die wichtigste. Die Pfade der Dienste sind
**nicht disjunkt** (`/companies/{id}/members` gehört identity,
`/companies/{id}/profile` gehört companies), und die Auflösung hängt an
`priority`-Werten in dieser Datei. Sie ein zweites Mal als
Ingress-Annotationen auszudrücken hieße, die Landkarte zu verdoppeln — und
die Kopie wäre beim ersten neuen Pfad falsch.

---

## 2. Ein Image für zehn Dienste

`service.Dockerfile` unterscheidet die Dienste ausschließlich über den Build-Arg
`SERVICE_DIR`, und `entrypoint.sh` liest ihn aus der **Umgebung**. Also wird das
Image **einmal ohne diesen Arg** gebaut; der Pod setzt `SERVICE_DIR`.

Das spart nicht nur Platz (ein `kind load` von ~1 GB statt zehn), es beseitigt
auch eine Unwahrheit: zehn Images, die sich in einer Umgebungsvariablen
unterscheiden, sind kein Zehnfaches an Software.

Im Cluster gibt es keinen Bind-Mount und kein `--reload`. Der Code kommt aus dem
Image — das ist der Zustand, den Staging und Produktion haben, und der
Unterschied zu `docker compose` ist beabsichtigt.

---

## 3. Migrationen: ein Job, nicht der Hauptprozess

`entrypoint.sh` migriert heute beim Start jedes Containers. Bei drei Repliken
wäre das ein Rennen: drei `alembic upgrade head` gegen dieselbe Datenbank.

Deshalb wird die Verantwortung getrennt:

- **Migrations-Job** je Dienst, als `helm.sh/hook: pre-install,pre-upgrade`. Er
  benutzt den unveränderten Entrypoint und hängt ein `sh -c :` als Kommando
  daran — es wird also **genau derselbe** Migrationspfad ausgeführt wie in
  Compose, nicht eine zweite Beschreibung davon.
- **Deployment** überschreibt `command:` mit `uvicorn …` und **umgeht den
  Entrypoint damit vollständig**. Der Dienst migriert nicht mehr selbst.

Ein fehlgeschlagener Hook lässt `helm upgrade` scheitern. Das ist erwünscht: ein
Dienst, dessen Migration nicht durchlief, darf nicht „bereit" melden.

---

## 4. Repliken: eins, und das ist eine Entscheidung

`replicas: 1` für jeden Dienst. Zwei belegte Gründe, nicht einer:

1. **Die Auth-Bremse zählt im Prozess** (ROADMAP 10.1,
   `worker_platform.presentation.throttle`). Bei drei Instanzen verdreifacht
   sich das effektive Limit.
2. **Der Outbox-Zusteller hat keine Sperre.** `OutboxDispatcher.pending()` ist
   ein `SELECT … ORDER BY created_at LIMIT n` **ohne** `FOR UPDATE SKIP LOCKED`.
   Zwei Zusteller greifen dieselben Zeilen. Für die Löschkaskade ist das dank
   der zugesicherten Idempotenz (ADR-0027 §4) folgenlos; für Benachrichtigungen
   heißt es: jede Mail zweimal.

Punkt 2 ist der härtere und stand bisher nirgends. Beide gehören in die ADR,
weil `replicas: 3` in einer `values.yaml` sonst wie eine Zahl aussieht statt wie
eine Verhaltensänderung. **Wer skaliert, muss vorher eines von beiden lösen** —
geteilter Zähler (Redis) oder Bremse ins Gateway, und `SKIP LOCKED` im
Zusteller.

Die `values.yaml` trägt die Zahl mit genau dieser Begründung als Kommentar.

---

## 5. Das Gateway ist dasselbe Traefik

Traefik läuft als Deployment im Chart. Die **Service-Objekte heißen wie die
Compose-Dienste** (`identity-service` auf Port 8001 usw.), deshalb passt
`dynamic.yml` wortwörtlich — kein Suchen-und-Ersetzen, keine zweite Fassung.

Geteilt wird nach Beständigkeit:

- **`dynamic.yml` — die Landkarte.** Eine Datei, per
  `--set-file gateway.dynamicConfig=docker/traefik/dynamic.yml`. Sie ist in
  jeder Umgebung gleich.
- **`traefik.yml` — die statische Konfiguration.** Wird vom Chart *erzeugt*,
  denn sie ist umgebungsabhängig: das Dashboard ist lokal an und in Produktion
  aus, das Protokoll wird ausführlicher oder knapper, TLS kommt später dazu.

Erreichbar wird das Gateway über einen NodePort, den `kind` per
`extraPortMappings` auf einen Host-Port legt. **Port 8090, nicht 8080** — 8080
gehört dem Compose-Gateway, und zwei Umgebungen, die um denselben Port
streiten, erzeugen genau die Fehlersuche, die dieses Repo schon dreimal bezahlt
hat.

Keine Domain, kein DNS, kein `/etc/hosts`: die Regeln in `dynamic.yml` enthalten
kein einziges `Host(…)`, dem Gateway ist der Name also gleich.

**Kein GitOps** (ArgoCD/Flux) in diesem Schnitt — ohne Zielumgebung wäre das
Konfiguration, die niemand ausführt. Genau das Muster, das hier sechsmal
aufgeräumt werden musste (ADR-0021/0022/0024/0025/0026).

---

## 6. Das Web: ein gebautes Artefakt, und das Problem dahinter

Compose fährt `apps/web` als **Vite-Dev-Server**. Für eine Staging-Umgebung ist
das falsch: getestet werden soll, was ausgeliefert wird.

Also ein zweites Dockerfile, `docker/web-prod.Dockerfile`: `pnpm build` in einer
Builder-Stufe, ausgeliefert von nginx mit SPA-Rückfall auf `index.html` (der
TanStack Router braucht ihn — ein Neuladen von `/profile` liefert sonst 404).

**Der Haken, der die Struktur bestimmt:** `vite build` backt
`import.meta.env.VITE_*` in das JavaScript ein. Ein Image mit eingebackenen
URLs *kann* in Staging und Produktion nicht dasselbe sein — das Versprechen
„nur eine andere `values.yaml`" wäre für das Frontend gebrochen, und zwar
unsichtbar.

Die Auflösung ist klein und geht nicht anders:

- `apps/web/public/config.js` setzt `window.__WT_CONFIG__ = {}` — die
  Voreinstellung ändert nichts.
- `index.html` lädt sie als gewöhnliches Skript **vor** dem Modul-Bündel.
- `env.ts` löst in drei Stufen auf: Laufzeitwert → `VITE_*` → heutiger
  Port-Rückfall. Bestehendes Verhalten bleibt damit unberührt, alle
  vorhandenen Tests gelten weiter.
- Das Chart legt eine ConfigMap mit der echten `config.js` an und hängt sie
  über die Datei im Image.

Im Cluster zeigen alle zehn Basis-URLs auf **denselben** Gateway-Ursprung. Das
ist nicht bloß bequem, es beseitigt zwei Fehlerquellen: kein CORS (gleicher
Origin) und keine Cookie-Verwirrung zwischen Ports.

---

## 7. Was das Chart sonst mitbringt

| Baustein | Lokal | Staging/Produktion |
|---|---|---|
| Postgres | StatefulSet + PVC, `initdb` aus ConfigMap | `postgres.enabled: false`, Host aus `values` |
| Mailpit | Deployment, fängt jede Mail | `mailpit.enabled: false`, echter SMTP-Host |
| Jaeger | Deployment (all-in-one) | `jaeger.enabled: false` oder fremder Kollektor |
| Geheimnisse | Secret aus `values` (Entwicklungswerte) | `existingSecret` |

Die Dienste selbst sind **eine Liste in `values.yaml`** (Name, Verzeichnis,
Modul, Port, Datenbank, eigene Umgebungsvariablen), über die vier Templates
laufen: Deployment, Service, Migrations-Job, ConfigMap. Das ist dasselbe
Prinzip wie der `x-service`-Anker in `docker-compose.yml` — einen Dienst
hinzuzufügen ist ein Eintrag, keine Kopie.

Ein **Wächtertest** (`tests/test_k8s_matches_compose.py`, Bauart wie
`test_workspace_dependencies.py`) fällt rot, sobald `docker-compose.yml` einen
Dienst kennt, den die `values.yaml` nicht hat — oder umgekehrt. Ohne ihn wäre
der nächste neue Dienst im Cluster einfach abwesend, und niemand merkte es.

---

## 8. Der Beweis

Nicht „`kubectl apply` lief durch". `scripts/k8s-up.sh` macht der Reihe nach:

1. `kind create cluster` aus `deploy/kind/cluster.yaml` (wenn er fehlt)
2. beide Images bauen und `kind load docker-image`
3. `helm upgrade --install --wait` mit den beiden `--set-file`
4. **Belegen**, nicht behaupten:
   - jeder Pod `Ready`, jeder Migrations-Job `Complete`
   - `GET /jobs` über das Gateway → `200` mit JSON
   - `GET /` über das Gateway → `200`, ausgeliefertes `index.html`
   - `POST /auth/register` über das Gateway → Erfolg, und die Mail liegt in
     Mailpit. Erst das beweist, dass die Migrationen wirklich liefen; die
     beiden Lesepfade beweisen es nicht.

Ein Schritt, der nicht belegt werden kann, wird gemeldet, nicht übersprungen.

---

## 9. Was ausdrücklich nicht dazugehört

- **Kein GitOps.** Siehe Abschnitt 5.
- **Kein HPA, kein PodDisruptionBudget, keine NetworkPolicy.** Alle drei setzen
  Repliken > 1 oder eine Bedrohungslage voraus, die hier nicht entschieden ist.
- **Kein TLS.** Ohne Domain gäbe es nur ein selbstsigniertes Zertifikat, das
  jeder Browser wegklickt — eine Attrappe.
- **Kein Prometheus/Grafana.** OTel ist über Jaeger sichtbar; ein zweiter
  Beobachtungsweg ohne Konsumenten wäre das bekannte Muster.
- **Compose bleibt.** Es ist der schnellere Entwicklungsweg (Bind-Mount,
  Reload). Das Chart ersetzt es nicht, es beantwortet eine andere Frage.

---

## 10. Was danach in die Dokumentation muss

- **ADR-0028** — Chart-Aufbau, das eine Image, Migrations-Job statt
  Hauptprozess, und vor allem: `replicas: 1` mit den zwei Gründen aus
  Abschnitt 4.
- **ROADMAP 10.6** — Eintrag samt Statustabelle
  (`tests/test_roadmap_status_is_consistent.py` prüft, dass beide dasselbe
  sagen).
- **CLAUDE.md** — Abschnitt zu den neuen Kommandos.
- **`docs/prompts-naechste-schritte.md`** — Prompt B entfernen, wenn er
  eingelöst ist.
