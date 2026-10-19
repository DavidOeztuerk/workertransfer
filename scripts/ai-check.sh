#!/usr/bin/env bash
# Does the platform's model answer — through the real path, not around it?
#
# A configured key proves nothing: the egress boundary refuses an undeclared
# host without a word in the UI, a model that is not pulled answers 404, and a
# cover letter is written by a background worker nobody watches. So this
# script goes the way a person goes: register, write a profile, ask for the
# profile help, apply to an advert and let the cover letter be written.
#
# It prints lengths and states, never a text the model wrote.
set -euo pipefail

cd "$(dirname "$0")/.."

BASE="${WORKERTRANSFER_BASE:-http://localhost:8090}"
MAIL="${WORKERTRANSFER_MAIL:-http://localhost:8025}"
PW="a-sufficiently-long-password"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

red()   { printf '\033[31m%s\033[0m\n' "$*"; }
green() { printf '\033[32m%s\033[0m\n' "$*"; }
grey()  { printf '\033[2m%s\033[0m\n' "$*"; }

command -v python3 >/dev/null || { red "python3 is missing."; exit 1; }
curl -sf -o /dev/null "${BASE}/health/live" || { red "The gateway does not answer — 'make up' first."; exit 1; }

token_from_mail() {
  for _ in $(seq 1 40); do
    sleep 0.5
    id=$(curl -sf "${MAIL}/api/v1/search?query=to:$1" 2>/dev/null | python3 -c "
import json,sys
m=(json.load(sys.stdin).get('messages') or [])
print(m[0]['ID'] if m else '')" 2>/dev/null || echo "")
    [ -n "$id" ] || continue
    t=$(curl -sf "${MAIL}/api/v1/message/${id}" | python3 -c "
import json,sys
d=json.load(sys.stdin); print((d.get('Text') or '')+(d.get('HTML') or ''))" \
      | grep -oE 'token=[A-Za-z0-9_-]+' | head -1 | cut -d= -f2)
    [ -n "$t" ] && { echo "$t"; return 0; }
  done
  return 1
}

account() {
  local body
  if [ -n "${2:-}" ]; then
    body="{\"email\":\"$1\",\"password\":\"$PW\",\"display_name\":\"AI check\",\"company_name\":\"$2\"}"
  else
    body="{\"email\":\"$1\",\"password\":\"$PW\",\"display_name\":\"AI check\"}"
  fi
  curl -sf -o /dev/null -X POST "${BASE}/auth/register" -H 'Content-Type: application/json' -d "$body"
  t="$(token_from_mail "$1")" || { red "No confirmation mail for $1."; exit 1; }
  curl -sf -o /dev/null -X POST "${BASE}/auth/verify-email" -H 'Content-Type: application/json' -d "{\"token\":\"$t\"}"
  curl -sf -o /dev/null -c "${WORK}/$3.cookie" -X POST "${BASE}/auth/login" \
    -H 'Content-Type: application/json' -d "{\"email\":\"$1\",\"password\":\"$PW\"}"
}

# ask METHOD PATH COOKIE [BODY] — sets CODE, and leaves the answer in $WORK/body.
ask() {
  CODE="$(curl -s -o "${WORK}/body" -w '%{http_code}' -X "$1" "${BASE}$2" \
    -b "${WORK}/$3.cookie" -c "${WORK}/$3.cookie" \
    -H 'Content-Type: application/json' ${4:+-d "$4"})"
}

# last EXPRESSION — evaluates it on the last answer, as `d`.
last() { python3 -c "import json,sys; d=json.loads(open(sys.argv[2]).read() or 'null'); print(eval(sys.argv[1]))" "$1" "${WORK}/body"; }

stamp="$(date +%s)-$$"
grey "Creating a person and a company ..."
account "person@ai-p-${stamp}.example" "" person
account "boss@ai-c-${stamp}.example" "AI check ${stamp}" company

ask GET /me/companies company
tenant=$(last "(d if isinstance(d,list) else (d.get('companies') or d.get('items') or []))[0]['id']")
curl -sf -o /dev/null -b "${WORK}/company.cookie" -c "${WORK}/company.cookie" -X POST "${BASE}/auth/company/${tenant}"

failed=0

# 1 — the profile help: one call, synchronous.
ask PUT /profiles/me person '{"headline":"Schweisserin","bio":"Zehn Jahre MIG und WIG im Stahlbau.","location":"Hamburg","remote_ok":false,"skills":["MIG","WIG"],"commute_km":null,"relocation":null}'
[ "$CODE" = 200 ] || { red "Profile not saved (${CODE})."; exit 1; }
started=$(date +%s)
ask POST /profiles/me/draft person '{"wish":"kurz"}'
seconds=$(( $(date +%s) - started ))
if [ "$CODE" = 200 ]; then
  green "Profile help: the model answered ($(last 'len(d.get("draft") or "")') characters, ${seconds} s)."
else
  red "Profile help: ${CODE} after ${seconds} s — $(last 'd.get("detail") if isinstance(d,dict) else d')"
  failed=1
fi

# 2 — the cover letter: written in the background, so it is polled.
ask POST /jobs company '{"title":"Schweisser/in MIG","description":"Stahlbau, Hamburg.","location":"Hamburg","postal_code":"20095","remote_mode":"none","employment_type":"full_time","skills":["MIG"],"salary":null}'
[ "$CODE" = 201 ] || [ "$CODE" = 200 ] || { red "Advert not created (${CODE})."; exit 1; }
job=$(last 'd["id"]')
ask POST "/jobs/${job}/publish" company
[ "$CODE" = 200 ] || { red "Advert not published (${CODE})."; exit 1; }
ask POST /applications/drafts person "{\"job_ids\":[\"${job}\"]}"
draft=$(last '(d if isinstance(d,list) else d.get("items") or d.get("drafts") or [d])[0]["id"]')
ask POST "/applications/drafts/${draft}/write" person
if [ "$CODE" != 200 ]; then
  red "Cover letter: writing refused with ${CODE} — $(last 'd.get("detail") if isinstance(d,dict) else d')"
  failed=1
else
  started=$(date +%s)
  state=""
  for _ in $(seq 1 90); do
    sleep 2
    ask GET "/applications/drafts/${draft}" person
    state=$(last 'd.get("state") or d.get("status")')
    [ "$state" = generating ] || break
  done
  seconds=$(( $(date +%s) - started ))
  length=$(last 'len(d.get("body") or d.get("text") or "")')
  if [ "$state" != generating ] && [ "$length" -gt 0 ]; then
    green "Cover letter: written (${length} characters, state ${state}, ${seconds} s)."
  else
    red "Cover letter: state ${state} after ${seconds} s — $(last 'd.get("error") or ""')"
    failed=1
  fi
fi

[ "$failed" = 0 ] || exit 1
