#!/usr/bin/env bash
# Die lokale Staging-Umgebung: kind-Cluster, beide Images, das Helm-Release —
# und danach ein BELEG, dass es läuft.
#
#   make k8s-up            # alles
#   make k8s-down          # Cluster weg
#
# Der letzte Abschnitt ist der eigentliche Zweck. "kubectl apply lief durch" ist
# keine Aussage über eine laufende Anwendung; in diesem Repo hat genau diese
# Verwechslung schon dreimal Zeit gekostet (siehe ROADMAP 10.3/10.4). Deshalb
# wird am Ende wirklich gefragt — lesend UND schreibend, denn nur ein Schreibweg
# beweist, dass die Migrationen liefen.
set -euo pipefail

cd "$(dirname "$0")/.."

CLUSTER=workertransfer
RELEASE=workertransfer
CHART=deploy/helm/workertransfer
HOST_PORT=8090
BASE="http://localhost:${HOST_PORT}"

rot()  { printf '\033[31m%s\033[0m\n' "$*"; }
gruen(){ printf '\033[32m%s\033[0m\n' "$*"; }
schritt(){ printf '\n\033[1m==> %s\033[0m\n' "$*"; }

for werkzeug in docker kind helm kubectl; do
  command -v "$werkzeug" >/dev/null 2>&1 || { rot "$werkzeug fehlt. brew install kind helm"; exit 1; }
done
docker info >/dev/null 2>&1 || { rot "Docker läuft nicht."; exit 1; }

# ---------------------------------------------------------------------------
schritt "Cluster"
if kind get clusters 2>/dev/null | grep -qx "$CLUSTER"; then
  echo "kind-Cluster '$CLUSTER' existiert bereits."
else
  kind create cluster --config deploy/kind/cluster.yaml
fi
kubectl config use-context "kind-${CLUSTER}" >/dev/null

# ---------------------------------------------------------------------------
schritt "Images bauen"
# EIN Image für alle zehn Dienste: sie unterscheiden sich nur in SERVICE_DIR,
# und das setzt der Pod. Der Build-Arg bleibt deshalb hier ungesetzt.
docker build -f docker/service.Dockerfile -t workertransfer/service:dev .
docker build -f docker/web-prod.Dockerfile -t workertransfer/web:dev .

schritt "Images in den Cluster laden"
# kind hat keine Registry. Ohne diesen Schritt bleibt jeder Pod in
# ErrImagePull — und zwar mit einer Meldung, die nach einem Netzproblem aussieht.
kind load docker-image workertransfer/service:dev --name "$CLUSTER"
kind load docker-image workertransfer/web:dev --name "$CLUSTER"

# ---------------------------------------------------------------------------
schritt "Helm-Release"
# Die beiden --set-file sind der Grund, warum es keine zweite Landkarte gibt:
# Routen und Datenbankanlage kommen aus DENSELBEN Dateien, die docker compose
# benutzt.
helm upgrade --install "$RELEASE" "$CHART" \
  --set-file gateway.dynamicConfig=docker/traefik/dynamic.yml \
  --set-file postgres.initSql=scripts/initdb/01-create-service-databases.sql \
  --set anthropicApiKey="${ANTHROPIC_API_KEY:-}" \
  --wait --timeout 12m

# ---------------------------------------------------------------------------
schritt "Beweis 1 — jeder Pod bereit"
kubectl get pods -o wide
# `kubectl wait` statt einer eigenen Auswertung der READY-Spalte: "1/1" gegen
# "0/1" zu prüfen verlangt eine Rückwärtsreferenz im Muster, und die kennt awk
# nicht — der Test wäre stillschweigend immer wahr oder immer falsch gewesen.
if ! kubectl wait --for=condition=Ready pod --all --timeout=180s; then
  rot "Nicht jeder Pod ist bereit."
  kubectl get pods
  exit 1
fi
gruen "Alle Pods bereit."

schritt "Beweis 2 — lesend durch das Gateway"
# /jobs gehört jobs-service, / gehört der Oberfläche. Zwei verschiedene Ziele,
# also wird wirklich geroutet und nicht bloß irgendwas beantwortet.
jobs_status=$(curl -s -o /tmp/wt-jobs.json -w '%{http_code}' "${BASE}/jobs" || true)
web_status=$(curl -s -o /tmp/wt-web.html -w '%{http_code}' "${BASE}/" || true)
echo "GET /jobs -> ${jobs_status}"
echo "GET /     -> ${web_status}"
[ "$jobs_status" = "200" ] || { rot "GET /jobs lieferte ${jobs_status}, erwartet 200."; cat /tmp/wt-jobs.json; exit 1; }
[ "$web_status"  = "200" ] || { rot "GET / lieferte ${web_status}, erwartet 200."; exit 1; }
grep -q "<div id=\"root\">" /tmp/wt-web.html || { rot "GET / lieferte kein ausgeliefertes index.html."; exit 1; }
# Ohne diese Zeile wäre nicht belegt, dass die Laufzeitkonfiguration wirklich
# ersetzt wurde — die Voreinstellung aus dem Image ist ein leeres Objekt.
config_status=$(curl -s -o /tmp/wt-config.js -w '%{http_code}' "${BASE}/config.js" || true)
grep -q "$BASE" /tmp/wt-config.js || {
  rot "/config.js enthält ${BASE} nicht (Status ${config_status}) — die ConfigMap greift nicht."
  cat /tmp/wt-config.js; exit 1
}
gruen "Gateway routet auf zwei verschiedene Ziele, Laufzeitkonfiguration sitzt."

schritt "Beweis 3 — schreibend, und die Mail kommt an"
# Erst DAS beweist, dass die Migrationen wirklich liefen: die beiden Lesepfade
# oben antworten auch, wenn keine einzige Tabelle existiert.
mail="k8s-beweis-$(date +%s)@example.org"
reg_status=$(curl -s -o /tmp/wt-reg.json -w '%{http_code}' \
  -X POST "${BASE}/auth/register" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"${mail}\",\"password\":\"ein-ausreichend-langes-passwort\",\"display_name\":\"K8s Beweis\"}" || true)
echo "POST /auth/register -> ${reg_status}"
[ "$reg_status" = "201" ] || { rot "Registrierung lieferte ${reg_status}, erwartet 201."; cat /tmp/wt-reg.json; exit 1; }

kubectl port-forward svc/mailpit 18025:8025 >/dev/null 2>&1 &
pf=$!
trap 'kill $pf 2>/dev/null || true' EXIT
# `|| continue` und `if grep`: unter `set -e` beendet ein fehlgeschlagenes
# Kommando am Ende eines Schleifenkörpers sonst das ganze Skript — und zwar
# wortlos, mitten im Warten auf eine Mail, die eine Sekunde später da wäre.
for _ in $(seq 1 30); do
  sleep 1
  curl -sf "http://localhost:18025/api/v1/messages" -o /tmp/wt-mail.json 2>/dev/null || continue
  if grep -q "$mail" /tmp/wt-mail.json; then break; fi
done
grep -q "$mail" /tmp/wt-mail.json 2>/dev/null || {
  rot "Keine Bestätigungsmail für ${mail} in Mailpit."
  rot "Der Schreibweg hat also NICHT durchgeschlagen — Outbox oder SMTP prüfen."
  exit 1
}
gruen "Registrierung angelegt, Bestätigungsmail liegt in Mailpit."

# ---------------------------------------------------------------------------
printf '\n'
gruen "Die lokale Staging-Umgebung läuft."
cat <<TEXT

  Anwendung        ${BASE}
  Traefik-Übersicht  kubectl port-forward deploy/gateway 8081:8080  -> http://localhost:8081
  Mailpit            kubectl port-forward svc/mailpit 8025:8025     -> http://localhost:8025
  Jaeger             kubectl port-forward svc/jaeger 16686:16686    -> http://localhost:16686

  Abbauen: make k8s-down

TEXT
