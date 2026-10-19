#!/usr/bin/env bash
# The local staging environment: kind cluster, both images, the Helm release —
# and after that a PROOF that it runs.
#
#   make k8s-up            # everything
#   make k8s-down          # cluster gone
#
# The last section is the actual purpose. "kubectl apply went through" is no
# statement about a running application; in this repository exactly that
# confusion has already cost time three times (see ROADMAP 10.3/10.4). So at
# the end it really asks — reading AND writing, because only a write path
# proves that the migrations ran.
set -euo pipefail

cd "$(dirname "$0")/.."

CLUSTER=workertransfer
RELEASE=workertransfer
CHART=deploy/helm/workertransfer
HOST_PORT=8090
BASE="http://localhost:${HOST_PORT}"
WEB_PORT=5173
WEB="http://localhost:${WEB_PORT}"

red()  { printf '\033[31m%s\033[0m\n' "$*"; }
green(){ printf '\033[32m%s\033[0m\n' "$*"; }
step() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }

for tool in docker kind helm kubectl; do
  command -v "$tool" >/dev/null 2>&1 || { red "$tool is missing. brew install kind helm"; exit 1; }
done
docker info >/dev/null 2>&1 || { red "Docker is not running."; exit 1; }

# kind binds the host ports listed in deploy/kind/cluster.yaml, and compose
# binds the same three. Whoever gets there first wins, and the loser fails
# somewhere far from the cause — kind reports an unready node minutes later.
# Named here, before anything is built.
# Only when the cluster does not exist yet: once it runs, its own node holds
# these ports, and a check that cannot tell the two apart blocks every re-run.
if ! kind get clusters 2>/dev/null | grep -qx "$CLUSTER"; then
  taken=""
  for port in "$HOST_PORT" "$WEB_PORT" 8025; do
    if lsof -nP -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1; then
      taken="$taken $port"
    fi
  done
  if [ -n "$taken" ]; then
    red "These host ports are already taken:$taken"
    red "The compose stack uses the same ones. First 'make down', then 'make k8s-up'."
    exit 1
  fi
fi

# ---------------------------------------------------------------------------
step "Cluster"
if kind get clusters 2>/dev/null | grep -qx "$CLUSTER"; then
  echo "kind cluster '$CLUSTER' exists already."
  # `kind get clusters` lists the cluster even when its node is STOPPED — and
  # that is the normal case, because one stops it before `make test`
  # (Testcontainers and the cluster do not get along). Without these lines the
  # script reports "exists already", builds images for ten minutes and then
  # fails on a `kind load` against a node that is not running.
  if ! docker ps --filter "name=${CLUSTER}-control-plane" --format '{{.Names}}' | grep -q .; then
    echo "... but its node is stopped. Starting it."
    docker start "${CLUSTER}-control-plane" >/dev/null
  fi
else
  kind create cluster --config deploy/kind/cluster.yaml
fi
kubectl config use-context "kind-${CLUSTER}" >/dev/null

# Waiting for the API server. After a `docker start` it answers within a few
# seconds; the pods then need one to two more minutes, which `helm --wait`
# further down absorbs.
for _ in $(seq 1 60); do
  kubectl get nodes >/dev/null 2>&1 && break
  sleep 2
done
kubectl get nodes >/dev/null 2>&1 || { red "The cluster does not answer."; exit 1; }

# ---------------------------------------------------------------------------
step "Building images"
# ONE image for all fourteen services AND the gateway: they differ only in
# SERVICE_DIR, and the pod sets that. The build arg therefore stays unset here.
#
# The secret carries the NuGet sign-in in and never becomes a layer. It has not
# been needed since 10.09.2026 — Girder lives on nuget.org — and it stands here
# so that a machine with a source mapping of its own keeps building.
docker build -f docker/dotnet-service.Dockerfile \
  --secret "id=nuget_config,src=${HOME}/.nuget/NuGet/NuGet.Config" \
  -t workertransfer/service:dev .
docker build -f docker/web-prod.Dockerfile -t workertransfer/web:dev .

step "Loading the images into the cluster"
# kind has no registry. Without this step every pod stays in ErrImagePull —
# with a message that looks like a network problem.
kind load docker-image workertransfer/service:dev --name "$CLUSTER"
kind load docker-image workertransfer/web:dev --name "$CLUSTER"

# ---------------------------------------------------------------------------
step "Helm release"
# Only ONE --set-file left: the database setup comes from the same file docker
# compose uses. The routes need none any more — the map travels in the image,
# and with it what compose runs runs here.
# The token key pair comes about here and not in the chart: `keepOrMake` rolls
# each half on its own, and two halves rolled on their own do not fit together.
# With an existing release the present pair is reused — a new one would sign
# out every open session.
key_pair=$(mktemp)
trap 'rm -f "$key_pair"' EXIT

if kubectl get secret "$RELEASE-secrets" >/dev/null 2>&1; then
  jwt_private=$(kubectl get secret "$RELEASE-secrets" \
    -o jsonpath='{.data.WORKER_JWT_PRIVATE_KEY}' | base64 -d)
  jwt_public=$(kubectl get secret "$RELEASE-secrets" \
    -o jsonpath='{.data.WORKER_JWT_PUBLIC_KEY}' | base64 -d)
  echo "  Key pair taken over from the existing release."
else
  openssl ecparam -name prime256v1 -genkey -noout -out "$key_pair" 2>/dev/null
  jwt_private=$(openssl pkcs8 -topk8 -nocrypt -in "$key_pair" -outform DER \
    2>/dev/null | base64 | tr -d '\n')
  jwt_public=$(openssl ec -in "$key_pair" -pubout -outform DER \
    2>/dev/null | base64 | tr -d '\n')
  echo "  Key pair freshly generated (P-256)."
fi

helm upgrade --install "$RELEASE" "$CHART" \
  --set-file postgres.initSql=scripts/initdb/01-create-service-databases.sql \
  --set aiKey="${WORKERTRANSFER_AI_KEY:-}" \
  --set secrets.jwtPrivateKey="$jwt_private" \
  --set secrets.jwtPublicKey="$jwt_public" \
  --wait --timeout 12m

# ---------------------------------------------------------------------------
step "Proof 1 — every pod ready"
kubectl get pods -o wide
# `kubectl wait` instead of evaluating the READY column ourselves: checking
# "1/1" against "0/1" needs a back reference in the pattern, and awk does not
# know one — the test would have been silently always true or always false.
if ! kubectl wait --for=condition=Ready pod --all --timeout=180s; then
  red "Not every pod is ready."
  kubectl get pods
  exit 1
fi
green "All pods ready."

step "Proof 2 — reading through the gateway, on TWO different services"
# Two targets, so it really is routed and not merely something answering — and
# the two answers have different SHAPES, which is the actual proof: a missing
# route would be Ocelot's empty 404, a dead service a 502, and both would look
# alike in a single status code.
#
# SOMETHING ELSE STOOD HERE UNTIL 11.09.2026, and it could no longer hold. Two
# promises were outdated, both measured at the first real run:
#
#   * `GET /jobs` was expected with 401. The job list has become public; it
#     answers 200. With that the proof hanging on it was gone too — the
#     RFC 9457 document with `correlationId`. It now hangs on `/consent/me`,
#     exactly as in CI's `images` job and for the same reason.
#   * `GET /` was expected with the shipped UI. Since ADR-0040 the gateway
#     serves NO UI any more: the `web` pod was ClusterIP and not reachable from
#     outside, the chart published only the gateway. `/` answers 404, and that
#     is right. Whoever wanted the UI back in the staging environment would
#     give `web` a door of its own and split `publicUrl` into a web and an API
#     origin — the work ADR-0040 expressly left open, and which Proof 4 below
#     now checks.
#
# With them fell "Proof 2b — direct link and reload": it checked the
# `Sec-Fetch-Dest` switch of the `Navigation` middleware, and that was deleted
# with ADR-0040. A proof for a middleware that does not exist can only go red.
jobs_status=$(curl -s -o /tmp/wt-jobs.json -w '%{http_code}' "${BASE}/jobs" || true)
consent_status=$(curl -s -o /tmp/wt-consent.json -w '%{http_code}' "${BASE}/consent/me" || true)
echo "GET /jobs       -> ${jobs_status}"
echo "GET /consent/me -> ${consent_status}"

# jobs-service, public. The proof is the PAGE SHAPE, not the success.
[ "$jobs_status" = "200" ] || { red "GET /jobs delivered ${jobs_status}, expected 200."; cat /tmp/wt-jobs.json; exit 1; }
grep -q '"items"' /tmp/wt-jobs.json || { red "GET /jobs did not come from jobs-service."; cat /tmp/wt-jobs.json; exit 1; }

# consent-service, behind the sign-in. A problem document with an id can only
# have been written by a service that really saw the request.
[ "$consent_status" = "401" ] || { red "GET /consent/me delivered ${consent_status}, expected 401."; cat /tmp/wt-consent.json; exit 1; }
grep -q '"correlationId"' /tmp/wt-consent.json || { red "GET /consent/me did not come from consent-service."; cat /tmp/wt-consent.json; exit 1; }
green "The gateway routes to two different services, both answer in their own shape."

step "Proof 3 — writing, and the mail arrives"
# Only THIS proves that the migrations really ran: the two read paths above
# answer even when not a single table exists.
# `display_name`, NOT `displayName`. The contract writes snake_case, and
# camelCase does not arrive wrongly — it does not arrive at all: the value is
# empty at the receiver, and the answer is a 422 on a field one believes one
# sent. Exactly this seam once let the notification path fall silently four
# times. Measured 11.09.2026, at the first real run of this script.
mail="k8s-proof-$(date +%s)@example.org"
reg_status=$(curl -s -o /tmp/wt-reg.json -w '%{http_code}' \
  -X POST "${BASE}/auth/register" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"${mail}\",\"password\":\"a-sufficiently-long-password\",\"display_name\":\"K8s proof\"}" || true)
echo "POST /auth/register -> ${reg_status}"
[ "$reg_status" = "201" ] || { red "Registration delivered ${reg_status}, expected 201."; cat /tmp/wt-reg.json; exit 1; }

kubectl port-forward svc/mailpit 18025:8025 >/dev/null 2>&1 &
pf=$!
trap 'kill $pf 2>/dev/null || true' EXIT
# `|| continue` and `if grep`: under `set -e` a failed command at the end of a
# loop body otherwise ends the whole script — without a word, in the middle of
# waiting for a mail that would be there a second later.
for _ in $(seq 1 30); do
  sleep 1
  curl -sf "http://localhost:18025/api/v1/messages" -o /tmp/wt-mail.json 2>/dev/null || continue
  if grep -q "$mail" /tmp/wt-mail.json; then break; fi
done
grep -q "$mail" /tmp/wt-mail.json 2>/dev/null || {
  red "No confirmation mail for ${mail} in Mailpit."
  red "So the write path did NOT get through — check the outbox or SMTP."
  exit 1
}
green "Registration created, confirmation mail lies in Mailpit."

# ---------------------------------------------------------------------------
step "Proof 4 — the UI has a door of its own, and the gateway does not"
# Two halves, and the second is the one worth having. A UI that answers proves
# the door works; a gateway that ALSO answers documents would mean ADR-0040 has
# quietly come back, and nothing else in this script would notice.
web_status=$(curl -s -o /tmp/wt-web.html -w '%{http_code}' "${WEB}/" || true)
echo "GET ${WEB}/ -> ${web_status}"
[ "$web_status" = "200" ] || { red "The UI delivered ${web_status}, expected 200."; exit 1; }
grep -qi '<div id="root"' /tmp/wt-web.html || {
  red "No application shell at ${WEB}/ — somebody else answered."
  head -c 400 /tmp/wt-web.html
  exit 1
}

# The counter-check: the gateway must answer a DOCUMENT request with its own
# 404, never with HTML. `Sec-Fetch-Dest: document` is exactly what a browser
# sends on a direct link, and it is what the deleted Navigation middleware
# used to switch on.
gateway_document=$(curl -s -o /tmp/wt-gateway.html -w '%{http_code}' \
  -H 'Sec-Fetch-Dest: document' "${BASE}/overview" || true)
echo "GET ${BASE}/overview (as a document) -> ${gateway_document}"
if grep -qi '<div id="root"' /tmp/wt-gateway.html; then
  red "The gateway serves the UI. That is ADR-0040 back."
  exit 1
fi
green "UI on ${WEB}, API on ${BASE} — two doors, no switch."

# ---------------------------------------------------------------------------
printf '\n'
green "The local staging environment is running."
cat <<TEXT

  API (gateway)      ${BASE}
  UI                 ${WEB}   (a door of its own — since ADR-0040 the gateway
                     serves only the API and never a document)
  Mailpit            http://localhost:8025   (the confirmation link from the
                     registration lies here — without it nobody gets in)
  Traefik overview   kubectl port-forward deploy/gateway 8081:8080  -> http://localhost:8081
  Jaeger             kubectl port-forward svc/jaeger 16686:16686    -> http://localhost:16686

  Tear down: make k8s-down

TEXT
