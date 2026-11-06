#!/usr/bin/env bash
# Reads a rendered chart on stdin and checks who holds which secret: every calling
# service exactly its own client secret, identity alone the hashes (ADR-0057), and
# every service its own database password, ring key and evidence secret, the master
# key at identity alone (ADR-0058). No client value in the shared `-env` ConfigMap,
# which every pod loads.
#
# awk over the rendering and not a test in a suite: helm runs in CI only in the
# images job, and `make k8s-lint` is its twin.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/m2m-pair.sh
. "$here/m2m-pair.sh"
# shellcheck source=scripts/service-secrets.sh
. "$here/service-secrets.sh"

rendering=$(mktemp)
trap 'rm -f "$rendering"' EXIT
cat > "$rendering"

# Every document on one line per fact: `<kind>/<name> <fact>`.
facts=$(awk '
  /^---/ { kind = ""; name = ""; next }
  /^kind: / { kind = $2 }
  /^  name: / && name == "" { name = $2 }
  /key: (WORKER_M2M_|WORKER_DB_PASSWORD_|WORKER_KEYRING_KEY_|WORKER_EVIDENCE_SECRET_|WORKER_SECRETS_KEY|POSTGRES_PASSWORD)/ { print kind "/" name " " $NF }
  /name: NOELIA_M2M_CLIENT_[A-Z_]*_SECRET_HASH/ { print kind "/" name " hash-variable" }
  kind == "ConfigMap" && /NOELIA_M2M_/ { print kind "/" name " shared-m2m" }
' "$rendering")

failed=0

complain() {
  echo "k8s-render-check: $1" >&2
  failed=1
}

holders() {
  printf '%s\n' "$facts" | awk -v k="$1" '$2 == k { print $1 }' | sort -u | tr '\n' ' ' | sed 's/ $//'
}

expect_holders() {
  local key="$1" wanted="$2" held
  held=$(holders "$key")
  if [ "$held" != "$wanted" ]; then
    complain "$key is held by [$held], not by [$wanted]."
  fi
}

for client in $M2M_CLIENTS; do
  own="WORKER_M2M_$(upper "$client")_SECRET"
  expect_holders "$own" "Deployment/${client}-service"
  expect_holders "${own}_HASH" "Deployment/identity-service"
done

for service in $SERVICE_DATABASES; do
  name=$(upper "$service")
  expect_holders "WORKER_DB_PASSWORD_$name" "Deployment/${service}-service StatefulSet/postgres"
  expect_holders "WORKER_KEYRING_KEY_$name" "Deployment/${service}-service"
  expect_holders "WORKER_EVIDENCE_SECRET_$name" "Deployment/${service}-service"

  if ! grep -q "Database=${service};Username=svc_${service};" "$rendering"; then
    complain "${service}-service does not log in as svc_${service}."
  fi
done

expect_holders WORKER_SECRETS_KEY "Deployment/identity-service"
expect_holders POSTGRES_PASSWORD "StatefulSet/postgres"

if printf '%s\n' "$facts" | grep -q ' shared-m2m$'; then
  complain "a NOELIA_M2M_ value stands in a ConfigMap, and every pod loads it."
fi

# The superuser enters no database a role owns (ADR-0058), not even for a probe.
if ! grep -q 'pg_isready' "$rendering"; then
  complain "no Postgres probe found, so this check reads nothing."
elif grep 'pg_isready' "$rendering" | grep -qv '"-d", "postgres"'; then
  complain "a Postgres probe does not ask the database postgres."
fi

# Without it the entrypoint sources the script instead of running it.
if ! grep -q 'defaultMode: 0555' "$rendering"; then
  complain "the initdb ConfigMap is not mounted executable."
fi

exit "$failed"
