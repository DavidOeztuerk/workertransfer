#!/usr/bin/env bash
# Test data for the local staging environment: a company, three published jobs,
# an applicant account.
#
#   make k8s-seed
#
# **Through the real API, not via SQL.** A job has a tenant_id, a status and a
# history; raw SQL easily produces a row that could never come about through
# the application — and one later looks for the defect in the code. The path
# here proves along the way that registering, confirming, founding a company,
# switching the tenant and publishing really work together.
#
# Can run several times: every run creates new accounts with a timestamp in the
# name. It cleans up nothing — that is what `make k8s-down` is for.
#
# The CONTENT stays German (company, job titles and texts): it is what the
# product shows.
set -euo pipefail

cd "$(dirname "$0")/.."

BASE=${BASE:-http://localhost:8090}
MAILPIT=${MAILPIT:-http://localhost:8025}
PW=${SEED_PASSWORD:-a-sufficiently-long-password}

red(){ printf '\033[31m%s\033[0m\n' "$*"; }
green(){ printf '\033[32m%s\033[0m\n' "$*"; }
step(){ printf '\n\033[1m==> %s\033[0m\n' "$*"; }

curl -sf -o /dev/null -m 5 "$BASE/jobs" || {
  red "Nobody answers at $BASE. Is 'make k8s-up' running?"
  exit 1
}
curl -sf -o /dev/null -m 5 "$MAILPIT/api/v1/messages" || {
  red "Mailpit at $MAILPIT does not answer — without the confirmation mail nothing works here."
  exit 1
}

# Fetches the confirmation link from Mailpit and redeems it. Without this step
# every account would stay PENDING and could not sign in.
confirm() {
  local mail="$1" id token
  for _ in $(seq 1 20); do
    id=$(curl -s "$MAILPIT/api/v1/messages" | python3 -c "
import json,sys
d=json.load(sys.stdin)
for m in d.get('messages', []):
    if any('$mail'==t['Address'] for t in m.get('To', [])):
        print(m['ID']); break
" || true)
    [ -n "${id:-}" ] && break
    sleep 1
  done
  [ -n "${id:-}" ] || { red "no confirmation mail for $mail"; return 1; }
  token=$(curl -s "$MAILPIT/api/v1/message/$id" | python3 -c "
import json,sys,re
d=json.load(sys.stdin); t=d.get('Text','')+d.get('HTML','')
m=re.search(r'token=([A-Za-z0-9_.-]+)', t); print(m.group(1) if m else '')")
  curl -s -o /dev/null -X POST "$BASE/auth/verify-email" \
    -H 'Content-Type: application/json' -d "{\"token\":\"$token\"}"
}

create_account() {  # create_account <mail> <display name> <cookie file>
  curl -s -o /dev/null -X POST "$BASE/auth/register" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$1\",\"password\":\"$PW\",\"displayName\":\"$2\"}"
  confirm "$1"
  curl -s -c "$3" -o /dev/null -X POST "$BASE/auth/login" \
    -H 'Content-Type: application/json' -d "{\"email\":\"$1\",\"password\":\"$PW\"}"
}

stamp=$(date +%s)
# NO freemail address: the company domain is derived from the confirmed
# address (ADR-0019), and gmail.com & co. are blocked for that.
COMPANY_MAIL="admin@nordwind-technik-${stamp}.de"
PERSON_MAIL="bewerberin-${stamp}@example.org"
JAR_COMPANY=$(mktemp); JAR_PERSON=$(mktemp)
trap 'rm -f "$JAR_COMPANY" "$JAR_PERSON"' EXIT

step "Creating the company account"
create_account "$COMPANY_MAIL" "Nordwind Admin" "$JAR_COMPANY"

step "Founding the company and switching into it"
company=$(curl -s -b "$JAR_COMPANY" -X POST "$BASE/companies" \
  -H 'Content-Type: application/json' -d '{"name":"Nordwind Technik"}')
tid=$(printf '%s' "$company" | python3 -c "import json,sys; print(json.load(sys.stdin).get('id',''))")
[ -n "$tid" ] || { red "Company not created: $company"; exit 1; }
# The switch is needed BEFORE jobs come about: a person's token carries no
# tenant_id, and without one there is no company an advert could belong to
# (ADR-0017/0018).
curl -s -b "$JAR_COMPANY" -c "$JAR_COMPANY" -o /dev/null -X POST "$BASE/auth/company/$tid"
echo "    tenant_id=$tid"

step "Company profile"
# Without a profile /companies/<id>/profile answers 404, and the job list shows
# no company name — an honest 404, but a bare page.
curl -s -b "$JAR_COMPANY" -o /dev/null -X PUT "$BASE/companies/me/profile" \
  -H 'Content-Type: application/json' -d '{
    "display_name": "Nordwind Technik",
    "about": "Wir bauen Abrechnungssysteme fuer Energieversorger. 40 Menschen, zwei Standorte.",
    "website": "https://nordwind-technik.example",
    "locations": ["Hamburg", "Kiel"],
    "benefits": ["Vier-Tage-Woche moeglich", "Feste Dienstplaene", "Kein Bereitschaftsdienst"]
  }'
echo "    career page: $BASE/karriere/nordwind-technik"

publish() {  # publish <title> <place> <remote> <skills-json> <description>
  local job jid
  job=$(curl -s -b "$JAR_COMPANY" -X POST "$BASE/jobs" -H 'Content-Type: application/json' \
    -d "{\"title\":\"$1\",\"description\":\"$5\",\"location\":\"$2\",\"remote\":\"$3\",\"employment\":\"full_time\",\"skills\":$4}")
  jid=$(printf '%s' "$job" | python3 -c "import json,sys; print(json.load(sys.stdin).get('id',''))")
  [ -n "$jid" ] || { red "    not created: $job"; return 1; }
  # Creating is a draft; a job becomes visible only by publishing it.
  curl -s -b "$JAR_COMPANY" -o /dev/null -X POST "$BASE/jobs/$jid/publish"
  echo "    ✓ $1"
}

step "Publishing jobs"
publish "Backend-Entwicklung Python" "Hamburg" "hybrid" '["Python","PostgreSQL","Kubernetes"]' \
  "Wir bauen die Abrechnung neu. Kleines Team, echte Verantwortung, keine Bereitschaftsdienste."
publish "Frontend-Entwicklung React" "Berlin" "full" '["TypeScript","React","CSS"]' \
  "Unsere Oberflaeche soll schneller und zugaenglicher werden. Barrierefreiheit ist kein Nachgedanke."
publish "Pflegefachkraft Intensiv" "Kiel" "none" '["Intensivpflege","Beatmung"]' \
  "Feste Dienstplaene vier Wochen im Voraus. Ausfaelle deckt ein Springerpool, nicht die Freizeit."

step "Creating the applicant account"
create_account "$PERSON_MAIL" "Testbewerberin" "$JAR_PERSON"

step "Counting — not claiming"
visible=$(curl -s "$BASE/jobs" | python3 -c "import json,sys; print(len(json.load(sys.stdin)['items']))")
[ "$visible" -ge 3 ] || { red "Only $visible jobs publicly visible, expected at least 3."; exit 1; }
green "$visible jobs are visible without signing in."

cat <<TEXT

  Jobs               $BASE/jobs

  Applicant          $PERSON_MAIL
  Company            $COMPANY_MAIL
  Password (both)    $PW

  Mails              $MAILPIT

TEXT
