#!/usr/bin/env bash
# Drives the route map against the RUNNING stack.
#
#   make up && ./scripts/route-map.sh
#
# `docs/routenkarte.yml` records for every endpoint what it answers in the FOUR
# capacities of ADR-0017/0018. Here it is asked whether that still holds.
#
# The completeness of the map is checked by another test (RouteMapTests in
# WorkerTransfer.Gateway.Tests) — that one needs no stack and runs in every
# suite. Together they make the promise: every route has an entry, and every
# entry holds.
#
# The map is a German document and its YAML keys stay German (`gruppen`,
# `eintraege`, `pfad`, `methode`, `rumpf`, and the columns `ohne`, `person`,
# `mitglied`, `firma`); this script reads them by those names.
#
# ---------------------------------------------------------------------------
# WHY A SCRIPT AND NOT A TEST SUITE
#
# A suite that needs a running stack skips itself without one — and a skipped
# test looks like a passed one. Exactly that confusion has already cost time in
# this repository. A script one calls cannot quietly excuse itself: it runs, or
# it says why not.
set -euo pipefail

cd "$(dirname "$0")/.."

BASE="${WORKERTRANSFER_BASE:-http://localhost:8090}"
MAIL="${WORKERTRANSFER_MAIL:-http://localhost:8025}"
MAP="docs/routenkarte.yml"
PW="a-sufficiently-long-password"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

red()   { printf '\033[31m%s\033[0m\n' "$*"; }
green() { printf '\033[32m%s\033[0m\n' "$*"; }
grey()  { printf '\033[2m%s\033[0m\n' "$*"; }

command -v python3 >/dev/null || { red "python3 is missing."; exit 1; }

curl -sf -o /dev/null "${BASE}/health/live" || {
  red "The gateway does not answer on ${BASE}/health/live."
  red "The stack is not running — 'make up' and once more."
  exit 1
}

# ---------------------------------------------------------------------------
# Obtaining the four capacities.
#
# A DOMAIN OF ITS OWN PER RUN for the company: a domain can be claimed only
# once (ADR-0019). On the second run against the same one the company is
# refused at confirmation — the account is confirmed anyway, and the
# measurement then goes on with a PERSON token in the company column, without
# anything turning red. That is exactly how this script once went astray while
# it was being built.
stamp="$(date +%s)-$$"
# A DOMAIN OF ITS OWN FOR THE PERSON TOO, not only for the company.
#
# `POST /companies` answers DIFFERENTLY depending on the state: while the
# person's domain is still free, the name check falls first (400); once it is
# claimed, the domain check falls first (409). With a shared domain like
# example.org the answer therefore depends on whether some earlier run created
# a company there — and the map would flip between two values without anything
# in the code having changed.
#
# With a domain of its own per run it is unambiguous: 400.
PERSON="person@rm-p-${stamp}.example"
COMPANY="boss@rm-c-${stamp}.example"
# The FOURTH capacity: the same company, role `member`.
#
# It founds NOTHING. Whoever founds is administrator (ADR-0019) — a second
# company account would be a second admin and thus the same column once more.
# So the member comes into the boss account's company through an invitation
# with `role: "member"`, and that is exactly the way a person takes too: there
# is no "create company" button.
MEMBER="colleague@rm-m-${stamp}.example"

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

# The token from the INVITATION MAIL, recognised by its link.
#
# `token_from_mail` takes the first message and the first `token=` hit. The
# member account gets TWO mails, though: first the confirmation, then the
# invitation. Relying on the order in Mailpit would make the measurement depend
# on a sort nobody promised — and a wrong token does not stand out here, it
# only quietly turns the member into a person without a company. So the LINK is
# searched that only the invitation carries.
invitation_token_from_mail() {
  for _ in $(seq 1 40); do
    sleep 0.5
    t=$(curl -sf "${MAIL}/api/v1/search?query=to:$1" 2>/dev/null | python3 -c "
import json,sys
print(' '.join(m['ID'] for m in (json.load(sys.stdin).get('messages') or [])))" 2>/dev/null \
      | tr ' ' '\n' | while read -r id; do
          [ -n "$id" ] || continue
          curl -sf "${MAIL}/api/v1/message/${id}" | python3 -c "
import json,sys
d=json.load(sys.stdin); print((d.get('Text') or '')+(d.get('HTML') or ''))"
        done | grep -oE 'invitation\?token=[A-Za-z0-9_-]+' | head -1 | cut -d= -f2)
    [ -n "$t" ] && { echo "$t"; return 0; }
  done
  return 1
}

create_account() {
  if [ -n "${2:-}" ]; then
    body="{\"email\":\"$1\",\"password\":\"$PW\",\"display_name\":\"Route map\",\"company_name\":\"$2\"}"
  else
    body="{\"email\":\"$1\",\"password\":\"$PW\",\"display_name\":\"Route map\"}"
  fi
  curl -sf -o /dev/null -X POST "${BASE}/auth/register" \
    -H 'Content-Type: application/json' -d "$body"
  t="$(token_from_mail "$1")" || { red "No confirmation mail for $1."; exit 1; }
  curl -sf -o /dev/null -X POST "${BASE}/auth/verify-email" \
    -H 'Content-Type: application/json' -d "{\"token\":\"$t\"}"
  curl -sf -o /dev/null -c "${WORK}/$3.cookie" -X POST "${BASE}/auth/login" \
    -H 'Content-Type: application/json' -d "{\"email\":\"$1\",\"password\":\"$PW\"}"
}

grey "Creating the capacities ..."
create_account "$PERSON" "" person
create_account "$COMPANY" "Route map ${stamp}" company
create_account "$MEMBER" "" member

TENANT=$(curl -sf -b "${WORK}/company.cookie" "${BASE}/me/companies" | python3 -c "
import json,sys
d=json.load(sys.stdin)
d=d if isinstance(d,list) else (d.get('companies') or d.get('items') or [])
print(d[0]['id'] if d else '')")

[ -n "$TENANT" ] || { red "The company was not created — domain already taken?"; exit 1; }

curl -sf -o /dev/null -b "${WORK}/company.cookie" -c "${WORK}/company.cookie" \
  -X POST "${BASE}/auth/company/${TENANT}"

# The proof that the third column really is a third one. Without it one may be
# measuring the same capacity twice.
carried=$(curl -sf -b "${WORK}/company.cookie" "${BASE}/me" | python3 -c "
import json,sys; print(json.load(sys.stdin).get('tenant_id') or '')")
[ -n "$carried" ] || { red "The company token carries no tenant."; exit 1; }

# ---------------------------------------------------------------------------
# Bringing the MEMBER into the same company — through an invitation, like a
# person.
invited=$(curl -s -o /dev/null -w '%{http_code}' -b "${WORK}/company.cookie" \
  -X POST "${BASE}/companies/${TENANT}/invitations" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"${MEMBER}\",\"role\":\"member\"}")
[ "$invited" = "201" ] || { red "The invitation was not created (${invited})."; exit 1; }

invitation="$(invitation_token_from_mail "$MEMBER")" \
  || { red "No invitation mail for ${MEMBER}."; exit 1; }

curl -sf -o /dev/null -b "${WORK}/member.cookie" \
  -X POST "${BASE}/invitations/accept" \
  -H 'Content-Type: application/json' -d "{\"token\":\"${invitation}\"}"

# Joining does NOT switch the running session (on purpose, see
# CompanyJourneyTests) — the member has to choose the company itself.
curl -sf -o /dev/null -b "${WORK}/member.cookie" -c "${WORK}/member.cookie" \
  -X POST "${BASE}/auth/company/${TENANT}"

# And the proof that the FOURTH column really is a fourth one: the same tenant
# as the company column, but the role `member`. Without this check a failed
# join would quietly measure a person without a company — and every one of the
# eight protected rows would still look green, because 403 is the right answer
# for a person there too.
member_tenant=$(curl -sf -b "${WORK}/member.cookie" "${BASE}/me" | python3 -c "
import json,sys; print(json.load(sys.stdin).get('tenant_id') or '')")
[ "$member_tenant" = "$carried" ] \
  || { red "The member token does not carry the same tenant as the company."; exit 1; }

role=$(curl -sf -b "${WORK}/member.cookie" "${BASE}/me/companies" | python3 -c "
import json,sys
d=json.load(sys.stdin)
d=d if isinstance(d,list) else (d.get('companies') or d.get('items') or [])
print(d[0]['role'] if d else '')")
[ "$role" = "member" ] \
  || { red "The fourth account is no 'member' but '${role}'."; exit 1; }

grey "  person without a company, company and member at tenant ${carried:0:8}…"

# ---------------------------------------------------------------------------
# `/account/erasure` erases the caller. A run that bluntly touches every
# endpoint takes its own account away and then measures nothing but 401s that
# are no promise but a consequence. So it is skipped — VISIBLY, not silently.
SKIPPED="/account/erasure"

python3 - "$MAP" "$BASE" "$WORK" "$SKIPPED" <<'PY'
import json, subprocess, sys, urllib.request, urllib.error

route_map, base, work, skipped = sys.argv[1:5]

def read_map(path):
    """Reads YAML without relying on ONE tool.

    This script runs in CI too. Ruby is there today — but a proof that hangs
    on an unexplained tool fails some day without any recognisable connection.
    So PyYAML first, then Ruby, and when both are missing: say what is missing
    instead of ending green.
    """
    try:
        import yaml                                   # noqa: PLC0415
        with open(path) as handle:
            return yaml.safe_load(handle)
    except ImportError:
        pass

    run = subprocess.run(
        ["ruby", "-ryaml", "-rjson", "-e", f"puts YAML.load_file({path!r}).to_json"],
        capture_output=True, text=True)
    if run.returncode != 0:
        print("Neither PyYAML nor Ruby available — the map cannot be read.")
        print(run.stderr.strip())
        sys.exit(1)
    return json.loads(run.stdout)


groups = read_map(route_map)["gruppen"]

def ask(method, path, cookie, body=None):
    # `{}` as the default, because by far most endpoints do not look inside.
    # Where one does, the entry carries a `rumpf:` — otherwise the map would
    # measure the answer to an EMPTY request and record it as the endpoint's.
    request = urllib.request.Request(
        base + path, method=method,
        data=(body or "{}").encode(),
        headers={"Content-Type": "application/json", **({"Cookie": cookie} if cookie else {})})
    try:
        with urllib.request.urlopen(request) as answer:
            return answer.status
    except urllib.error.HTTPError as error:
        return error.code
    except Exception as error:                       # noqa: BLE001
        return f"<{type(error).__name__}>"

def cookie_from(name):
    # Do NOT throw `#HttpOnly_` away as a comment. Exactly these lines carry the
    # access token — the browser gets it only as an httpOnly cookie
    # (ADR-0006/0007). Whoever skips them measures every signed-in row as 401
    # and then takes half the map for wrong.
    rows = []
    for row in open(f"{work}/{name}.cookie").read().splitlines():
        if row.startswith("#HttpOnly_"):
            row = row[len("#HttpOnly_"):]
        elif row.startswith("#") or not row.strip():
            continue
        rows.append(row.split("\t"))
    return "; ".join(f"{t[5]}={t[6]}" for t in rows if len(t) >= 7)

# The column names are the map's own YAML keys.
columns = {
    "ohne": None,
    "person": cookie_from("person"),
    "mitglied": cookie_from("member"),
    "firma": cookie_from("company"),
}

deviations, checked, skipped_count = [], 0, 0

for group in groups:
    for entry in group["eintraege"]:
        if entry["pfad"] in skipped.split(","):
            skipped_count += 1
            print(f"  \033[2mskipped: {entry['methode']} {entry['pfad']} "
                  f"(erases the caller)\033[0m")
            continue
        for column, cookie in columns.items():
            checked += 1
            actual = ask(
                entry["methode"], entry["pfad"], cookie, entry.get("rumpf"))
            expected = entry[column]
            if actual != expected:
                deviations.append(
                    (group["name"], entry["methode"], entry["pfad"], column, expected, actual))

print()
if deviations:
    print(f"\033[31m{len(deviations)} deviation(s):\033[0m")
    for name, method, path, column, expected, actual in deviations:
        print(f"  \033[31m{method:7} {path:52} {column:8} expected {expected}, got {actual}\033[0m")
        print(f"          \033[2min group: {name}\033[0m")
    print()
    print("A deviation is NO reason to adjust the map. First check which of the")
    print("two sides is right — the map records the INTENT.")
    sys.exit(1)

print(f"\033[32m{checked} answers checked, all as written down.\033[0m")
if skipped_count:
    print(f"\033[2m({skipped_count} skipped, named above.)\033[0m")
PY
