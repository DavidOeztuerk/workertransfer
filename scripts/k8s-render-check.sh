#!/usr/bin/env bash
# Reads a rendered chart on stdin and checks who holds which client secret
# (ADR-0057): every calling service exactly its own, identity alone the hashes,
# and no client value in the shared `-env` ConfigMap, which every pod loads.
#
# awk over the rendering and not a test in a suite: helm runs in CI only in the
# images job, and `make k8s-lint` is its twin.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/m2m-pair.sh
. "$here/m2m-pair.sh"

rendering=$(mktemp)
trap 'rm -f "$rendering"' EXIT
cat > "$rendering"

# Every document on one line per fact: `<kind>/<name> <fact>`.
facts=$(awk '
  /^---/ { kind = ""; name = ""; next }
  /^kind: / { kind = $2 }
  /^  name: / && name == "" { name = $2 }
  /key: WORKER_M2M_/ { print kind "/" name " " $NF }
  /name: NOELIA_M2M_CLIENT_[A-Z_]*_SECRET_HASH/ { print kind "/" name " hash-variable" }
  kind == "ConfigMap" && /NOELIA_M2M_/ { print kind "/" name " shared-m2m" }
' "$rendering")

failed=0

complain() {
  echo "k8s-render-check: $1" >&2
  failed=1
}

for client in $M2M_CLIENTS; do
  own="WORKER_M2M_$(printf %s "$client" | tr '[:lower:]' '[:upper:]')_SECRET"

  held=$(printf '%s\n' "$facts" | awk -v k="$own" '$2 == k { print $1 }' | sort -u)
  if [ "$held" != "Deployment/${client}-service" ]; then
    complain "$own is held by [${held//$'\n'/ }], not by ${client}-service alone."
  fi

  hash_holders=$(printf '%s\n' "$facts" | awk -v k="${own}_HASH" '$2 == k { print $1 }' | sort -u)
  if [ "$hash_holders" != "Deployment/identity-service" ]; then
    complain "${own}_HASH is held by [${hash_holders//$'\n'/ }], not by identity-service alone."
  fi
done

if printf '%s\n' "$facts" | grep -q ' shared-m2m$'; then
  complain "a NOELIA_M2M_ value stands in a ConfigMap, and every pod loads it."
fi

exit "$failed"
