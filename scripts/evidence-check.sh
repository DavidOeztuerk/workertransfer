#!/usr/bin/env bash
# Runs every check in every service and goes RED as soon as a promise is not
# kept.
#
# RED ONLY ON `Fail`. A `Warning` is no defect but a question to a human —
# that model calls are not recorded is ADR-0024 and no gap. A gate that goes
# red on deliberate restraint gets switched off by the next person, and then it
# is no gate at all.
#
# `NotApplicable` is green as well and expressly NO tick: it says that the
# subject does not exist here.
#
#   scripts/evidence-check.sh              # against the running stack
#   scripts/evidence-check.sh --documents  # the signatures as well
#
# Needs `make up`.

set -uo pipefail
cd "$(dirname "$0")/.."

BOLD=$'\033[1m'; RED=$'\033[31m'; GREEN=$'\033[32m'; YELLOW=$'\033[33m'; OFF=$'\033[0m'

WITH_DOCUMENTS=0
[[ "${1:-}" == "--documents" ]] && WITH_DOCUMENTS=1

for tool in curl python3; do
  command -v "$tool" >/dev/null || {
    printf '%s✗%s %s is missing.\n' "$RED" "$OFF" "$tool" >&2; exit 1; }
done

if [[ -f .env ]]; then
  SECRET=$(grep -E '^WORKERTRANSFER_EVIDENCE_SECRET=' .env | head -1 | cut -d= -f2-)
fi
SECRET="${WORKERTRANSFER_EVIDENCE_SECRET:-${SECRET:-}}"

if [[ -z "$SECRET" ]]; then
  printf '%s✗%s WORKERTRANSFER_EVIDENCE_SECRET is not set.\n' "$RED" "$OFF" >&2
  echo "   Without it every evidence address answers 404 — rightly, but then" >&2
  echo "   there is nothing to check here. \`make env\` rolls it along." >&2
  exit 1
fi

SERVICES=(
  "identity:8001" "consent:8002" "profile:8003" "resume:8004"
  "portfolio:8005" "jobs:8006" "applications:8007" "companies:8008"
  "transfer:8009" "notification:8010" "github:8011" "scout:8012"
  "advisor:8013" "assessment:8014"
)

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

printf '%sEvidence check%s\n\n' "$BOLD" "$OFF"

RED_RUN=0
READ=0
FINDINGS=0
SILENT=()

for entry in "${SERVICES[@]}"; do
  name="${entry%%:*}"
  port="${entry##*:}"

  if ! curl -fsS --max-time 15 \
       -H "X-Noelia-Operator: ${SECRET}" \
       "http://localhost:${port}/noelia/report.json" \
       -o "${WORK}/${name}.json" 2>/dev/null; then
    SILENT+=("$name")
    printf '  %s✗%s %-14s does not answer\n' "$RED" "$OFF" "$name"
    RED_RUN=1
    continue
  fi

  READ=$((READ + 1))

  # The evaluation is in python3 and not in a grep chain: a status word inside
  # a summary would otherwise be counted as a status, and the number would be
  # silently wrong. A wrong number is worse than none.
  lines=$(python3 - "${WORK}/${name}.json" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as f:
    report = json.load(f)
# Noelia's operator report, schema 3 since 7.0.0: the checks still live under
# securityChecks.results, and their status is Pass/Warning/Fail/NotApplicable.
findings = report["securityChecks"]["results"]
tally = {}
for finding in findings:
    tally[finding["status"]] = tally.get(finding["status"], 0) + 1
failed = [finding for finding in findings if finding["status"] == "Fail"]
print(len(findings))
print(tally.get("Pass", 0), tally.get("Warning", 0),
      tally.get("Fail", 0), tally.get("NotApplicable", 0))
for finding in failed:
    print(f"{finding['id']}\t{finding['summary']}")
PY
)

  total=$(sed -n '1p' <<<"$lines")
  states=$(sed -n '2p' <<<"$lines")
  read -r passed warned failed notapplicable <<<"$states"

  FINDINGS=$((FINDINGS + total))

  if [[ "$total" -eq 0 ]]; then
    # A service that answers and reports NOTHING looks from outside like one
    # where everything is fine. That is the trap against which
    # `scripts/test-dotnet.sh` writes its number on the screen.
    printf '  %s✗%s %-14s answers, but reports not a single finding\n' \
      "$RED" "$OFF" "$name"
    RED_RUN=1
    continue
  fi

  if [[ "$failed" -gt 0 ]]; then
    printf '  %s✗%s %-14s %s findings: %s pass, %s warning, %s%s NOT KEPT%s, %s n/a\n' \
      "$RED" "$OFF" "$name" "$total" "$passed" "$warned" "$RED" "$failed" "$OFF" "$notapplicable"
    sed -n '3,$p' <<<"$lines" | while IFS=$'\t' read -r id text; do
      printf '      %s%s%s  %s\n' "$BOLD" "$id" "$OFF" "$text"
    done
    RED_RUN=1
  else
    printf '  %s✓%s %-14s %s findings: %s pass, %s warning, %s n/a\n' \
      "$GREEN" "$OFF" "$name" "$total" "$passed" "$warned" "$notapplicable"
  fi
done

# ---------------------------------------------------------------------------
# The signatures, on request.
# ---------------------------------------------------------------------------
if [[ $WITH_DOCUMENTS -eq 1 ]]; then
  printf '\n%sThe documents%s\n' "$BOLD" "$OFF"
  command -v openssl >/dev/null || {
    printf '  %s✗%s openssl is missing.\n' "$RED" "$OFF"; RED_RUN=1; }

  for document in datenschutz ki mitbestimmung; do
    file="evidence/${document}.json"
    signature="evidence/${document}.sig.json"

    if [[ ! -f "$file" || ! -f "$signature" ]]; then
      printf '  %s!%s %-14s not there — `make evidence` first\n' \
        "$YELLOW" "$OFF" "$document"
      continue
    fi

    python3 -c "
import json, sys
with open(sys.argv[1], encoding='utf-8') as f:
    d = json.load(f)
sys.stdout.write(json.dumps(d, sort_keys=True, separators=(',', ':'), ensure_ascii=False))
" "$file" > "${WORK}/${document}.canonical"

    python3 -c "
import json, sys
with open(sys.argv[1], encoding='utf-8') as f:
    print(json.load(f)['public_key_pem_base64'])
" "$signature" | openssl base64 -d -A > "${WORK}/${document}.pub"

    python3 -c "
import json, sys
with open(sys.argv[1], encoding='utf-8') as f:
    print(json.load(f)['signature'])
" "$signature" | openssl base64 -d -A > "${WORK}/${document}.bin"

    if openssl dgst -sha256 -verify "${WORK}/${document}.pub" \
         -signature "${WORK}/${document}.bin" \
         "${WORK}/${document}.canonical" >/dev/null 2>&1; then
      printf '  %s✓%s %-14s signature holds\n' "$GREEN" "$OFF" "$document"
    else
      printf '  %s✗%s %-14s signature does NOT hold\n' "$RED" "$OFF" "$document"
      RED_RUN=1
    fi

    # THE WORD LIST, AT THE GENERATED DOCUMENT.
    #
    # A test over the source of the sentence is good and runs everywhere
    # (`DocumentWordTests`); this one checks what REALLY came out — including
    # the caveats that are derived from the reading and therefore stand in no
    # constant. A finding whose summary one day says "konform" would otherwise
    # get through.
    word_hits=$(python3 scripts/evidence_words.py "$file")

    if [[ -n "$word_hits" ]]; then
      printf '  %s✗%s %-14s compliance word in the scope sentence or a caveat:\n' \
        "$RED" "$OFF" "$document"
      sed 's/^/      /' <<<"$word_hits"
      RED_RUN=1
    fi
  done
fi

# ---------------------------------------------------------------------------
# The number on the screen.
#
# It stands here for the same reason `scripts/test-dotnet.sh` writes its
# number: a green run over nothing looks exactly like one over fourteen
# services. `scripts/validate.sh` reads the "findings run" line below.
# ---------------------------------------------------------------------------
printf '\n%s── State ─────────────────────────────────────────%s\n' "$BOLD" "$OFF"
printf '  %s of %s services read, %s findings run.\n' \
  "$READ" "${#SERVICES[@]}" "$FINDINGS"

if [[ ${#SILENT[@]} -gt 0 ]]; then
  printf '  %s!%s Stayed silent: %s\n' "$YELLOW" "$OFF" "${SILENT[*]}"
  echo "    Is the stack running? \`make up\`. A silent service is RED here and"
  echo "    no warning: the evidence says nothing about it, and that is exactly"
  echo "    the state nobody notices."
fi

if [[ $RED_RUN -eq 0 ]]; then
  printf '\n%s✓ No promise without evidence.%s What is open stands there as a warning.\n' \
    "$GREEN" "$OFF"
else
  printf '\n%s✗ At least one promise is not kept.%s\n' "$RED" "$OFF"
fi

exit $RED_RUN
