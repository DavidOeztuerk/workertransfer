#!/usr/bin/env bash
# Fills in what each service holds alone in a .env (ADR-0058): its database
# password, the key of its key ring and its evidence secret, plus the password
# the Postgres image demands on an empty volume.
#
# Nothing that is set is touched: a .env carries third-party secrets, and a value
# in use must stay. No value is printed.
set -euo pipefail

file="${1:-.env}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/env-file.sh
. "$here/env-file.sh"
# shellcheck source=scripts/service-secrets.sh
. "$here/service-secrets.sh"

added=0

add() {
  if [ -z "$(value_of "$1")" ]; then
    set_value "$1" "$(service_secret)"
    added=$((added + 1))
  fi
}

add WORKERTRANSFER_POSTGRES_PASSWORD

for service in $SERVICE_DATABASES; do
  name=$(upper "$service")
  add "WORKERTRANSFER_DB_PASSWORD_$name"
  add "WORKERTRANSFER_KEYRING_KEY_$name"
  add "WORKERTRANSFER_EVIDENCE_SECRET_$name"
done

echo "$added service value(s) added."
