#!/usr/bin/env bash
# Fills in the service clients' secrets and hashes in a .env (ADR-0057).
#
# Only ever whole pairs, and nothing that is set is touched: a .env carries
# third-party secrets, and an existing pair is in use. A pair with one half
# set stays as it is and is named: a second half rolled now would not fit the
# first, and the service would not get a token. No value is printed.
set -euo pipefail

file="${1:-.env}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/m2m-pair.sh
. "$here/m2m-pair.sh"

value_of() {
  awk -F= -v k="$1" '$1 == k { sub(/^[^=]*=/, ""); print; exit }' "$file"
}

set_value() {
  local key="$1" value="$2" tmp
  tmp=$(mktemp)
  if grep -q "^${key}=" "$file"; then
    awk -v k="$key" -v v="$value" 'index($0, k "=") == 1 { print k "=" v; next } { print }' "$file" > "$tmp"
  else
    cat "$file" > "$tmp"
    printf '%s=%s\n' "$key" "$value" >> "$tmp"
  fi
  cat "$tmp" > "$file"
  rm -f "$tmp"
}

added=0
broken=0

for client in $M2M_CLIENTS; do
  name=$(printf %s "$client" | tr '[:lower:]' '[:upper:]')
  secret_key="WORKERTRANSFER_M2M_${name}_SECRET"
  hash_key="WORKERTRANSFER_M2M_${name}_SECRET_HASH"
  secret=$(value_of "$secret_key")
  hash=$(value_of "$hash_key")

  if [ -n "$secret" ] && [ -n "$hash" ]; then
    continue
  fi

  if [ -n "$secret" ] || [ -n "$hash" ]; then
    echo "HALF A PAIR: $secret_key and $hash_key do not both have a value." >&2
    echo "  Left as it is. Empty both and run this again." >&2
    broken=$((broken + 1))
    continue
  fi

  pair=$(m2m_pair)
  set_value "$secret_key" "$(printf '%s\n' "$pair" | sed -n 1p)"
  set_value "$hash_key" "$(printf '%s\n' "$pair" | sed -n 2p)"
  added=$((added + 1))
done

echo "$added service client pair(s) added."

if [ "$broken" -gt 0 ]; then
  echo "$broken pair(s) are incomplete, see above." >&2
  exit 1
fi
