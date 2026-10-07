#!/usr/bin/env bash
# Collects every service's evidence from the RUNNING stack and writes three
# dated, signed documents.
#
# EVIDENCE, NOT COMPLIANCE. No artefact of this script says "compliant",
# "certified" or "fulfils Art. X". Under Art. 42/43 GDPR only a supervisory
# authority or a body accredited to EN ISO/IEC 17065 may certify; "certified"
# without accreditation is a misleading commercial practice in the EU
# (Directive 2005/29/EC, Directive 2006/114/EC).
#
# THREE DOCUMENTS AND NOT ONE, because three people read them for three
# purposes — and because from a mixed document one can quote the one part
# without quoting the part that limits it.
#
#   scripts/evidence.sh                  # against the running stack
#   scripts/evidence.sh --out evidence/  # where to
#
# Needs `make up` and the secret from .env.

set -uo pipefail
cd "$(dirname "$0")/.."

BOLD=$'\033[1m'; RED=$'\033[31m'; GREEN=$'\033[32m'; YELLOW=$'\033[33m'; OFF=$'\033[0m'

red()    { printf '%s✗%s %s\n' "$RED" "$OFF" "$1" >&2; }
yellow() { printf '%s!%s %s\n' "$YELLOW" "$OFF" "$1"; }
green()  { printf '%s✓%s %s\n' "$GREEN" "$OFF" "$1"; }

TARGET="evidence"
[[ "${1:-}" == "--out" ]] && TARGET="${2:?--out needs a directory}"

for tool in curl openssl python3; do
  command -v "$tool" >/dev/null || { red "$tool is missing."; exit 1; }
done

# The secret comes from .env — the same file the stack takes it from. Without
# it every evidence address answers 404, and that is the right answer: empty
# means the door is shut.
if [[ -f .env ]]; then
  # shellcheck disable=SC1091
  SECRET=$(grep -E '^WORKERTRANSFER_EVIDENCE_SECRET=' .env | head -1 | cut -d= -f2-)
fi
SECRET="${WORKERTRANSFER_EVIDENCE_SECRET:-${SECRET:-}}"

if [[ -z "$SECRET" ]]; then
  red "WORKERTRANSFER_EVIDENCE_SECRET is not set."
  echo "   \`make env\` writes a .env and rolls it along." >&2
  exit 1
fi

# The fourteen services and their ports — the same list as in
# docker-compose.yml. It stands here a second time, and that is the price for
# this script getting by WITHOUT docker commands: it asks over HTTP, the way it
# would ask a staging environment too.
SERVICES=(
  "identity:8001" "consent:8002" "profile:8003" "resume:8004"
  "portfolio:8005" "jobs:8006" "applications:8007" "companies:8008"
  "transfer:8009" "notification:8010" "github:8011" "scout:8012"
  "advisor:8013" "assessment:8014" "messages:8015"
)

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

printf '%sCollecting the evidence%s\n' "$BOLD" "$OFF"

REACHED=0
SILENT=()

for entry in "${SERVICES[@]}"; do
  name="${entry%%:*}"
  port="${entry##*:}"

  if curl -fsS --max-time 10 \
       -H "X-Noelia-Operator: ${SECRET}" \
       "http://localhost:${port}/noelia/report.json" \
       -o "${WORK}/${name}.json" 2>/dev/null; then
    REACHED=$((REACHED + 1))
    printf '  %s✓%s %-14s\n' "$GREEN" "$OFF" "$name"
  else
    SILENT+=("$name")
    printf '  %s✗%s %-14s does not answer\n' "$RED" "$OFF" "$name"
  fi
done

if [[ $REACHED -eq 0 ]]; then
  red "No service answered. Is the stack running? \`make up\`."
  echo "   And does it carry the secret? A .env without" >&2
  echo "   WORKERTRANSFER_EVIDENCE_SECRET does not let compose start at all." >&2
  exit 1
fi

# ---------------------------------------------------------------------------
# THE KEY.
#
# ECDSA P-256 with SHA-256. It is created on the first run and lives under
# .evidence/ — git-ignored, because a private key in git is no private key any
# more. Whoever hands the documents to somebody hands over the PUBLIC part with
# them; it stands in every signature file.
# ---------------------------------------------------------------------------
KEY="${WORKERTRANSFER_EVIDENCE_KEY:-.evidence/key.pem}"

if [[ ! -f "$KEY" ]]; then
  mkdir -p "$(dirname "$KEY")"
  openssl ecparam -name prime256v1 -genkey -noout -out "$KEY" 2>/dev/null
  chmod 600 "$KEY"
  yellow "New signing key: $KEY"
  echo "   It is git-ignored. A new key means: older documents do NOT verify"
  echo "   against the new public part any more."
fi

mkdir -p "$TARGET"

# ---------------------------------------------------------------------------
# THE THREE DOCUMENTS.
#
# python3 builds them, because the CANONICAL FORM carries everything here:
# sorted keys, no meaningless whitespace. A verifier recomputes it with
# `json.dumps(sort_keys=True, separators=(',', ':'), ensure_ascii=False)` —
# one line, the same byte sequence in every language.
# ---------------------------------------------------------------------------
python3 scripts/evidence_documents.py "$WORK" "$TARGET" "${SILENT[*]:-}" || {
  red "The documents could not be built."
  exit 1
}

# ---------------------------------------------------------------------------
# SIGNING.
#
# Over the CANONICAL form, not over the file: a file carries indentation and a
# trailing newline, and both are meaningless. Whoever recomputes canonicalises
# first and verifies then — otherwise the signature hangs on the formatting
# instead of on the content.
# ---------------------------------------------------------------------------
PUBLIC=$(openssl ec -in "$KEY" -pubout 2>/dev/null | \
  openssl base64 -A)

for document in datenschutz ki mitbestimmung; do
  file="${TARGET}/${document}.json"
  [[ -f "$file" ]] || continue

  python3 -c "
import json, sys
with open(sys.argv[1], encoding='utf-8') as f:
    d = json.load(f)
sys.stdout.write(json.dumps(d, sort_keys=True, separators=(',', ':'), ensure_ascii=False))
" "$file" > "${WORK}/${document}.canonical"

  signature=$(openssl dgst -sha256 -sign "$KEY" \
    "${WORK}/${document}.canonical" | openssl base64 -A)

  checksum=$(openssl dgst -sha256 -binary "${WORK}/${document}.canonical" | \
    openssl base64 -A)

  python3 -c "
import json, sys
print(json.dumps({
    'algorithm': 'ecdsa-p256-sha256',
    'canonical_form': \"json.dumps(sort_keys=True, separators=(',',':'), ensure_ascii=False)\",
    'canonical_sha256': sys.argv[1],
    'public_key_pem_base64': sys.argv[2],
    'signature': sys.argv[3],
    'verify': 'scripts/evidence-check.sh --documents',
}, indent=2, ensure_ascii=False, sort_keys=True))
" "$checksum" "$PUBLIC" "$signature" > "${TARGET}/${document}.sig.json"

  green "${TARGET}/${document}.json + .sig.json"
done

printf '\n%s%s of %s services read.%s\n' \
  "$BOLD" "$REACHED" "${#SERVICES[@]}" "$OFF"

if [[ ${#SILENT[@]} -gt 0 ]]; then
  yellow "Did not answer: ${SILENT[*]}"
  echo "   They stand BY NAME in every document — a service that stays silent"
  echo "   must not look like one that has nothing to report."
fi
