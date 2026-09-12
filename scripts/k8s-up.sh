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
  # `kind get clusters` listet den Cluster auch, wenn sein Knoten ANGEHALTEN
  # ist — und das ist der Normalfall, weil man ihn vor `make test` anhält
  # (Testcontainers und der Cluster vertragen sich nicht). Ohne diese Zeilen
  # meldet das Skript "existiert bereits", baut zehn Minuten lang Images und
  # scheitert dann an einem `kind load` gegen einen Knoten, der nicht läuft.
  if ! docker ps --filter "name=${CLUSTER}-control-plane" --format '{{.Names}}' | grep -q .; then
    echo "... aber sein Knoten steht. Wird gestartet."
    docker start "${CLUSTER}-control-plane" >/dev/null
  fi
else
  kind create cluster --config deploy/kind/cluster.yaml
fi
kubectl config use-context "kind-${CLUSTER}" >/dev/null

# Auf den API-Server warten. Nach einem `docker start` antwortet er nach
# wenigen Sekunden; die Pods brauchen danach noch ein bis zwei Minuten, was
# `helm --wait` weiter unten abfängt.
for _ in $(seq 1 60); do
  kubectl get nodes >/dev/null 2>&1 && break
  sleep 2
done
kubectl get nodes >/dev/null 2>&1 || { rot "Der Cluster antwortet nicht."; exit 1; }

# ---------------------------------------------------------------------------
schritt "Images bauen"
# EIN Image für alle vierzehn Dienste UND das Gateway: sie unterscheiden sich
# nur in SERVICE_DIR, und das setzt der Pod. Der Build-Arg bleibt deshalb hier
# ungesetzt.
#
# Das Geheimnis traegt die NuGet-Anmeldung herein und wird nie eine Schicht.
# Noetig ist es seit dem 10.09.2026 nicht mehr — Girder liegt auf nuget.org —,
# und es steht hier, damit eine Maschine mit einer eigenen Quellenzuordnung
# weiterhin baut.
docker build -f docker/dotnet-service.Dockerfile \
  --secret "id=nuget_config,src=${HOME}/.nuget/NuGet/NuGet.Config" \
  -t workertransfer/service:dev .
docker build -f docker/web-prod.Dockerfile -t workertransfer/web:dev .

schritt "Images in den Cluster laden"
# kind hat keine Registry. Ohne diesen Schritt bleibt jeder Pod in
# ErrImagePull — und zwar mit einer Meldung, die nach einem Netzproblem aussieht.
kind load docker-image workertransfer/service:dev --name "$CLUSTER"
kind load docker-image workertransfer/web:dev --name "$CLUSTER"

# ---------------------------------------------------------------------------
schritt "Helm-Release"
# Nur noch EIN --set-file: die Datenbankanlage kommt aus derselben Datei, die
# docker compose benutzt. Die Routen brauchen keins mehr — die Landkarte reist
# im Bild, und damit faehrt hier, was Compose faehrt.
helm upgrade --install "$RELEASE" "$CHART" \
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

schritt "Beweis 2 — lesend durch das Gateway, auf ZWEI verschiedene Dienste"
# Zwei Ziele, damit wirklich geroutet wird und nicht bloß irgendwas antwortet —
# und die zwei Antworten haben verschiedene GESTALTEN, was der eigentliche
# Beleg ist: eine fehlende Route wäre Ocelots leeres 404, ein toter Dienst ein
# 502, und beides sähe an einem einzelnen Statuscode gleich aus.
#
# HIER STAND BIS ZUM 11.09.2026 ETWAS ANDERES, und es konnte nicht mehr
# stimmen. Zwei Zusagen waren überholt, beide beim ersten echten Lauf gemessen:
#
#   * `GET /jobs` wurde mit 401 erwartet. Die Stellenliste ist öffentlich
#     geworden; sie antwortet 200. Damit war auch der Beleg weg, der an ihr
#     hing — das RFC-9457-Dokument mit `correlationId`. Er hängt jetzt an
#     `/consent/me`, genau wie im `images`-Auftrag der CI und aus demselben
#     Grund.
#   * `GET /` wurde mit der ausgelieferten Oberfläche erwartet. Seit ADR-0040
#     liefert das Gateway KEINE Oberfläche mehr: der `web`-Pod ist ClusterIP
#     und von außen nicht erreichbar, das Chart veröffentlicht allein das
#     Gateway. `/` antwortet 404, und das ist richtig. Wer die Oberfläche in
#     der Staging-Umgebung zurückwill, gibt `web` einen eigenen Eingang und
#     teilt `publicUrl` in einen Web- und einen API-Ursprung — das ist die
#     Arbeit, die ADR-0040 ausdrücklich offengelassen hat.
#
# Mit ihnen fiel „Beweis 2b — Direktlink und Neuladen": er prüfte die
# `Sec-Fetch-Dest`-Weiche der `Navigation`-Zwischenschicht, und die ist mit
# ADR-0040 gelöscht. Ein Beweis für eine Zwischenschicht, die es nicht gibt,
# kann nur rot werden.
jobs_status=$(curl -s -o /tmp/wt-jobs.json -w '%{http_code}' "${BASE}/jobs" || true)
einwilligung_status=$(curl -s -o /tmp/wt-consent.json -w '%{http_code}' "${BASE}/consent/me" || true)
echo "GET /jobs       -> ${jobs_status}"
echo "GET /consent/me -> ${einwilligung_status}"

# jobs-service, öffentlich. Der Beleg ist die SEITENGESTALT, nicht der Erfolg.
[ "$jobs_status" = "200" ] || { rot "GET /jobs lieferte ${jobs_status}, erwartet 200."; cat /tmp/wt-jobs.json; exit 1; }
grep -q '"items"' /tmp/wt-jobs.json || { rot "GET /jobs kam nicht von jobs-service."; cat /tmp/wt-jobs.json; exit 1; }

# consent-service, hinter der Anmeldung. Ein Problemdokument mit Kennung kann
# nur ein Dienst geschrieben haben, der die Anfrage wirklich gesehen hat.
[ "$einwilligung_status" = "401" ] || { rot "GET /consent/me lieferte ${einwilligung_status}, erwartet 401."; cat /tmp/wt-consent.json; exit 1; }
grep -q '"correlationId"' /tmp/wt-consent.json || { rot "GET /consent/me kam nicht von consent-service."; cat /tmp/wt-consent.json; exit 1; }
gruen "Gateway routet auf zwei verschiedene Dienste, beide antworten in ihrer eigenen Gestalt."

schritt "Beweis 3 — schreibend, und die Mail kommt an"
# Erst DAS beweist, dass die Migrationen wirklich liefen: die beiden Lesepfade
# oben antworten auch, wenn keine einzige Tabelle existiert.
# `display_name`, NICHT `displayName`. Der Vertrag schreibt snake_case, und
# camelCase kommt nicht etwa falsch an — es kommt gar nicht an: der Wert ist
# beim Empfaenger leer, und die Antwort ist ein 422 auf ein Feld, das man
# geschickt zu haben glaubt. Genau diese Naht hat den Benachrichtigungsweg
# einmal viermal still fallen lassen. Gemessen am 11.09.2026, beim ersten
# echten Lauf dieses Skripts.
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

  API (Gateway)      ${BASE}
  Oberfläche         KEINE — das Gateway liefert seit ADR-0040 nur die API,
                     und das Chart veröffentlicht nur das Gateway. Wer sie
                     hier braucht, gibt dem web-Pod einen eigenen Eingang.
  Mailpit            http://localhost:8025   (hier liegt der Bestätigungslink
                     aus der Registrierung — ohne ihn kommt man nicht hinein)
  Traefik-Übersicht  kubectl port-forward deploy/gateway 8081:8080  -> http://localhost:8081
  Jaeger             kubectl port-forward svc/jaeger 16686:16686    -> http://localhost:16686

  Abbauen: make k8s-down

TEXT
