#!/usr/bin/env bash
# The Noelia Control Plane over the running stack, on http://localhost:8091.
#
#   make up && make control-plane          # start (or restart), local mode
#   make up && make control-plane-oidc     # the same, signing in at Keycloak
#   make control-plane-down                # stop
#
# The local mode makes every visitor from this machine an administrator. The
# OIDC mode is the one the product runs in operation (VISION-FRAGEN-2 Q4):
# sign-in at the local Keycloak (`scripts/keycloak.sh`), group "admins" mapped
# to the role admin, nobody else let in.
#
# ---------------------------------------------------------------------------
# WHY ON THE HOST AND NOT IN A CONTAINER
#
# Since NoeliaControlPlane PR #1 the product refuses to start without access
# control: either OIDC (Authority, ClientId) or, for local evaluation,
# `Access:AllowLocalDevelopment=true` in Development. The local mode accepts a
# request only from a LOOPBACK peer with a local Host header and no forwarding
# headers. Through Docker Desktop's port forwarding the peer is the VM's
# gateway, never loopback — so in a container the mode starts and then refuses
# every browser request. Measured 01.10.2026. The vendor's own
# `run-everything.sh` runs it from source on the host for the same reason.
#
# The compose overlay this replaces also named its container
# `workertransfer-control-plane` — the name of the kind node `make k8s-up`
# looks for by filter.
#
# The fleet (`scripts/control-plane-fleet.json`) reaches the services through
# their published ports. The licence and the operator secret come from `.env`
# and never from that file.
set -euo pipefail

cd "$(dirname "$0")/.."

red()   { printf '\033[31m%s\033[0m\n' "$*"; }
green() { printf '\033[32m%s\033[0m\n' "$*"; }
grey()  { printf '\033[2m%s\033[0m\n' "$*"; }

SOURCE="${NOELIA_CONTROL_PLANE_PFAD:-$PWD/../NoeliaControlPlane}"
PROJECT="$SOURCE/src/Noelia.ControlPlane/Noelia.ControlPlane.csproj"
STATE="$PWD/.control-plane"
PORT=8091
PID_FILE="$STATE/control-plane.pid"

stop() {
  if [[ -f "$PID_FILE" ]] && kill -0 "$(cat "$PID_FILE")" 2>/dev/null; then
    kill "$(cat "$PID_FILE")"
    grey "stopped the running control plane"
  fi
  rm -f "$PID_FILE"
}

if [[ "${1:-}" == "--down" ]]; then
  stop
  exit 0
fi

MODE=local
[[ "${1:-}" == "--oidc" ]] && MODE=oidc
KEYCLOAK="https://localhost:8443/realms/workertransfer"

[[ -f "$PROJECT" ]] || {
  red "No NoeliaControlPlane at $SOURCE — set NOELIA_CONTROL_PLANE_PFAD."; exit 1; }
command -v dotnet >/dev/null || { red "dotnet is missing."; exit 1; }
[[ -f .env ]] || { red ".env is missing — \`make env\` first."; exit 1; }

value() { grep -E "^$1=" .env | head -1 | cut -d= -f2- || true; }
SECRET="$(value WORKERTRANSFER_EVIDENCE_SECRET)"
LICENCE="$(value WORKERTRANSFER_NOELIA_LICENCE)"

# Without the paper every service answers 404 and the fleet stays empty
# without anything looking broken.
[[ -n "$SECRET" ]] || { red "WORKERTRANSFER_EVIDENCE_SECRET is empty in .env."; exit 1; }

# A fleet that answers nothing is collected as fourteen unreachable services.
# Say it here instead of on a page.
curl -fsS --max-time 5 http://localhost:8001/health/live >/dev/null 2>&1 || {
  red "The stack does not answer on :8001 — \`make up\` first."; exit 1; }

if lsof -nP -iTCP:"$PORT" -sTCP:LISTEN >/dev/null 2>&1; then
  stop
  sleep 1
  if lsof -nP -iTCP:"$PORT" -sTCP:LISTEN >/dev/null 2>&1; then
    red "Port $PORT is taken by something else:"
    lsof -nP -iTCP:"$PORT" -sTCP:LISTEN | tail -n +2
    exit 1
  fi
fi

mkdir -p "$STATE"
grey "building the control plane from $SOURCE"
dotnet build "$PROJECT" -c Release -v quiet -nologo >"$STATE/build.log" 2>&1 || {
  red "Build failed — see $STATE/build.log"; exit 1; }

DLL="$(dirname "$PROJECT")/bin/Release/net10.0/Noelia.ControlPlane.dll"
[[ -f "$DLL" ]] || { red "No $DLL after the build."; exit 1; }

# The content root is the working directory, so the application reads the
# appsettings files out of $STATE: ours, and none of the vendor's demo fleets.
cp "$(dirname "$PROJECT")/appsettings.json" "$STATE/appsettings.json"
cp scripts/control-plane-fleet.json "$STATE/appsettings.Development.json"

# Who may come in. Local: everybody from this machine. OIDC: whoever Keycloak
# signs in and puts into "admins" — an unmapped group grants nothing.
ACCESS=(ControlPlane__Access__AllowLocalDevelopment=true)
if [[ "$MODE" == oidc ]]; then
  [[ -f "$STATE/keycloak.env" ]] || { red "No Keycloak set up — \`make keycloak\` first."; exit 1; }
  curl -fsS --max-time 3 --cacert "$STATE/keycloak-tls/cert.pem" \
    "$KEYCLOAK/.well-known/openid-configuration" >/dev/null 2>&1 || {
    red "Keycloak does not answer on $KEYCLOAK — \`make keycloak\` first."; exit 1; }
  CLIENT_SECRET="$(grep -E '^CONTROL_PLANE_CLIENT_SECRET=' "$STATE/keycloak.env" | cut -d= -f2-)"
  ACCESS=(
    "ControlPlane__Access__Authority=$KEYCLOAK"
    "ControlPlane__Access__ClientId=noelia-control-plane"
    "ControlPlane__Access__ClientSecret=$CLIENT_SECRET"
    "ControlPlane__Access__RoleClaim=groups"
    "ControlPlane__Access__RoleMap__admins=admin"
  )
fi

(
  cd "$STATE"
  # `env` and not a prefix: the values travel as one array, and the secret
  # stays out of this file's text.
  env "${ACCESS[@]}" \
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="http://localhost:$PORT" \
  ControlPlane__OperatorSecret="$SECRET" \
  ControlPlane__Licence="$LICENCE" \
  ControlPlane__HistoryPath="$STATE/fleet-history.db" \
  ControlPlane__OperatorAuditPath="$STATE/operator-audit.db" \
  ControlPlane__SigningKeyPath="$STATE/attestation-key.p8" \
  ControlPlane__DataProtectionKeysPath="$STATE/data-protection-keys" \
    nohup dotnet "$DLL" >"$STATE/control-plane.log" 2>&1 &
  echo $! >"$PID_FILE"
)

for _ in $(seq 1 60); do
  curl -fsS --max-time 2 "http://localhost:$PORT/health/live" >/dev/null 2>&1 && break
  kill -0 "$(cat "$PID_FILE")" 2>/dev/null || {
    red "The control plane exited — last lines of $STATE/control-plane.log:"
    tail -15 "$STATE/control-plane.log"; rm -f "$PID_FILE"; exit 1; }
  sleep 1
done

if [[ "$MODE" == oidc ]]; then
  # Signed out, every page sends the browser to Keycloak — and building that
  # address needs the discovery document, so a redirect there proves the
  # control plane reached Keycloak and trusted its certificate. Whether it
  # COLLECTS is measured in the local mode; here the pages need a sign-in.
  location="$(curl -sS -o /dev/null -w '%{redirect_url}' --max-time 20 "http://localhost:$PORT/")"
  if [[ "$location" != "$KEYCLOAK/protocol/openid-connect/auth"* ]]; then
    red "Signed out, / should lead to Keycloak and led to: ${location:-nowhere}"
    tail -15 "$STATE/control-plane.log"
    exit 1
  fi
  green "Control plane on http://localhost:$PORT — sign-in at Keycloak ($KEYCLOAK)"
  grey  "User 'operator', password: CONTROL_PLANE_OPERATOR_PASSWORD in .control-plane/keycloak.env"
  exit 0
fi

# "It answers" is not the promise. The promise is that it collects.
coverage="$(curl -fsS --max-time 60 "http://localhost:$PORT/fleet.json" | python3 -c '
import json, sys
c = json.load(sys.stdin)["coverage"]
schemas = sorted({m.get("reportSchemaVersion") for m in c["members"]} - {None})
print(c["state"], c["collected"], c["expected"], ",".join(map(str, schemas)))
')" || { red "The control plane answers, but /fleet.json does not."; exit 1; }

read -r state collected expected schemas <<<"$coverage"

if [[ "$collected" -ne "$expected" ]]; then
  red "Collected $collected of $expected services ($state) — see http://localhost:$PORT"
  exit 1
fi

green "Control plane on http://localhost:$PORT — $collected of $expected services collected, report schema $schemas"
grey  "Local mode: every visitor from this machine is an administrator. Stop with \`make control-plane-down\`."
