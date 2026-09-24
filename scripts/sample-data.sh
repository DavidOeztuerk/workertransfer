#!/usr/bin/env bash
#
# Creates two completely filled accounts: a person and a company.
#
# WHAT FOR. After `make up` the installation runs with what the E2E journeys
# left behind — jobs without a place, companies without a profile, people
# without a CV. That is right for checking promises and useless for LOOKING:
# every card shows gaps, and one takes them for defects. That is exactly how
# the defect `stelle.remoteundefined` was overlooked for months — in a card
# without a place and without a work mode one more gap does not stand out.
#
# This script fills EVERY field the UI draws.
#
# The CONTENT stays German (names, profile texts, job adverts): it is what the
# product shows, and the product speaks German first.
#
# FOR DEVELOPMENT ONLY. It creates accounts with known passwords and goes
# through the public path — registration, confirmation via Mailpit, sign-in.
# There is no back door, and that is on purpose: what works here works for a
# person too.
set -euo pipefail

GATEWAY="${WT_GATEWAY:-http://localhost:8090}"
MAILPIT="${WT_MAILPIT:-http://localhost:8025}"
PASSWORD="sample-password-2026"

# A timestamp in the name, so a second run does not fail on the taken address
# — the endpoint then answers the same, but creates nothing, and one would look
# for the defect in the wrong place.
STAMP="$(date +%s)"
PERSON="anna.beispiel+${STAMP}@example.org"
COMPANY_DOMAIN="nordlicht-technik-${STAMP}.de"
RECRUITER="max.werber@${COMPANY_DOMAIN}"
COMPANY_NAME="Nordlicht Technik GmbH"

red()  { printf '\033[31m%s\033[0m\n' "$*"; }
grey() { printf '\033[2m%s\033[0m\n' "$*"; }
green(){ printf '\033[32m%s\033[0m\n' "$*"; }

reachable() {
  curl -fsS -o /dev/null "$GATEWAY/health/live" 2>/dev/null || {
    red "The gateway does not answer at $GATEWAY."
    red "First 'make up', then this script."
    exit 1
  }
  curl -fsS -o /dev/null "$MAILPIT/api/v1/messages?limit=1" 2>/dev/null || {
    red "Mailpit does not answer at $MAILPIT — no mailbox, no confirmation link."
    exit 1
  }
}

# The confirmation token from the mail last delivered to an address.
token_from_mail() {
  local address="$1" attempt=0
  while [ "$attempt" -lt 20 ]; do
    local id
    id=$(curl -fsS -G "$MAILPIT/api/v1/search" --data-urlencode "query=to:$address" \
         | python3 -c 'import json,sys; d=json.load(sys.stdin); print(d["messages"][0]["ID"] if d.get("messages") else "")')
    if [ -n "$id" ]; then
      curl -fsS "$MAILPIT/api/v1/message/$id" \
        | python3 -c 'import json,re,sys; d=json.load(sys.stdin); m=re.search(r"token=([A-Za-z0-9_-]+)", d.get("Text","")); print(m.group(1) if m else "")'
      return 0
    fi
    attempt=$((attempt + 1)); sleep 0.5
  done
  red "No mail to $address in Mailpit."; exit 1
}

# Registers, confirms and signs in. Leaves the cookie in $2.
create_account() {
  local address="$1" cookie="$2" name="$3" company="${4:-}"
  local body
  if [ -n "$company" ]; then
    body=$(printf '{"email":"%s","password":"%s","display_name":"%s","company_name":"%s"}' \
            "$address" "$PASSWORD" "$name" "$company")
  else
    body=$(printf '{"email":"%s","password":"%s","display_name":"%s"}' \
            "$address" "$PASSWORD" "$name")
  fi

  curl -fsS -o /dev/null -X POST "$GATEWAY/auth/register" \
    -H 'Content-Type: application/json' -d "$body"

  local token; token=$(token_from_mail "$address")
  curl -fsS -o /dev/null -X POST "$GATEWAY/auth/verify-email" \
    -H 'Content-Type: application/json' -d "{\"token\":\"$token\"}"

  curl -fsS -o /dev/null -c "$cookie" -X POST "$GATEWAY/auth/login" \
    -H 'Content-Type: application/json' \
    -d "{\"email\":\"$address\",\"password\":\"$PASSWORD\"}"
}

# A call with a cookie. `as_user <cookie> <method> <path> [body]`
as_user() {
  local cookie="$1" method="$2" path="$3" body="${4:-}"
  if [ -n "$body" ]; then
    curl -fsS -b "$cookie" -X "$method" "$GATEWAY$path" \
      -H 'Content-Type: application/json' -d "$body"
  else
    curl -fsS -b "$cookie" -X "$method" "$GATEWAY$path"
  fi
}

reachable

COOKIE_PERSON="$(mktemp)"
COOKIE_COMPANY="$(mktemp)"
trap 'rm -f "$COOKIE_PERSON" "$COOKIE_COMPANY"' EXIT

grey "Creating the person …"
create_account "$PERSON" "$COOKIE_PERSON" "Anna Beispiel"

grey "Profile, CV, work samples, market status …"

as_user "$COOKIE_PERSON" PUT /profiles/me '{
  "headline": "Backend-Entwicklerin mit Faible fuer verteilte Systeme",
  "bio": "Seit acht Jahren baue ich Dienste, die auch dann noch antworten, wenn die Haelfte drumherum nicht mehr antwortet. Am liebsten dort, wo eine Entscheidung im Code nachvollziehbar bleibt: klare Grenzen zwischen Diensten, Vertraege, die man lesen kann, und Tests, die eine Zusage halten statt eine Zeile abzudecken.\n\nZuletzt viel mit Event-Streams und Datenwanderungen zu tun gehabt. Ich mag Systeme, die man abschalten kann, ohne dass jemand anruft.",
  "location": "Hamburg",
  "remote_ok": true,
  "skills": ["C#", ".NET", "PostgreSQL", "Kubernetes", "Docker", "Python", "Go", "Terraform"]
}' > /dev/null

as_user "$COOKIE_PERSON" PUT /resumes/me '{
  "positions": [
    {"employer": "Hansewerk Digital GmbH", "title": "Senior Backend Engineer",
     "started_on": "2022-04", "ended_on": null,
     "description": "Verantwortlich fuer die Abrechnungsdienste: sechs Dienste, eine Datenbank je Dienst, Nulldowntime-Wanderungen."},
    {"employer": "Elbstrom AG", "title": "Backend Engineer",
     "started_on": "2019-01", "ended_on": "2022-03",
     "description": "Messdatenverarbeitung, von zwei Millionen auf vierzig Millionen Punkte am Tag."},
    {"employer": "Kontorhaus Software", "title": "Werkstudentin",
     "started_on": "2017-09", "ended_on": "2018-12",
     "description": "Erste Dienste in .NET, viel gelernt ueber Fehlerbehandlung."}
  ],
  "education": []
}' > /dev/null

as_user "$COOKIE_PERSON" PUT /portfolios/me '{
  "items": [
    {"title": "Wanderung ohne Stillstand", "summary": "Ein Verfahren, mit dem ein Schema in Schritten wandert, waehrend beide Fassungen gleichzeitig laufen. Vortrag und Vorlage.",
     "url": "https://example.org/wanderung", "role": "Konzept und Umsetzung", "year": 2025, "attachment": null},
    {"title": "Lastprobe fuer Messdatenstroeme", "summary": "Ein kleines Werkzeug, das einen realistischen Strom erzeugt statt einer Schleife — sonst misst man den Zwischenspeicher.",
     "url": "https://example.org/lastprobe", "role": "Autorin", "year": 2023, "attachment": null}
  ]
}' > /dev/null

as_user "$COOKIE_PERSON" PUT /market/me '{
  "availability": "listening",
  "employed": true,
  "note": "Ich suche nicht aktiv, hoere aber zu. Wichtig waeren mir: Verantwortung fuer einen Dienst von Anfang bis Betrieb, ein Team, das Entscheidungen aufschreibt, und Hamburg oder remote."
}' > /dev/null

grey "Granting releases …"
SUBJECT=$(as_user "$COOKIE_PERSON" GET /auth/session | python3 -c 'import json,sys; print(json.load(sys.stdin)["user"]["user_id"])')
for capability in "profile.visibility:public" "portfolio.visibility:public"; do
  as_user "$COOKIE_PERSON" POST /consent/grant \
    "{\"subject_id\":\"$SUBJECT\",\"capability\":\"$capability\"}" > /dev/null
done

grey "Creating the company …"
create_account "$RECRUITER" "$COOKIE_COMPANY" "Max Werber" "$COMPANY_NAME"

TENANT=$(as_user "$COOKIE_COMPANY" GET /me/companies \
  | python3 -c 'import json,sys; d=json.load(sys.stdin); print((d if isinstance(d,list) else d.get("companies",d.get("items",[])))[0]["id"])')
# `-c` AND `-b`: the switch mints a NEW token with the tenant, and without
# writing it back the file would keep the old one — every company call after
# that would get 403, and one would look for the defect in the permissions.
curl -fsS -o /dev/null -b "$COOKIE_COMPANY" -c "$COOKIE_COMPANY" \
  -X POST "$GATEWAY/auth/company/$TENANT"

grey "Company profile …"
as_user "$COOKIE_COMPANY" PUT /companies/me/profile '{
  "display_name": "Nordlicht Technik GmbH",
  "about": "Wir bauen Steuerungssoftware fuer Windparks an der Nordsee. Zweiundvierzig Leute, davon achtzehn in der Entwicklung, seit 2014. Was wir liefern, laeuft im Zweifel ohne Netzverbindung weiter — das praegt, wie wir Software schreiben.\n\nWir schreiben Entscheidungen auf, wir deployen freitags, und wir haben keine Bereitschaft ausserhalb der Arbeitszeit.",
  "website": "https://example.org/nordlicht",
  "locations": ["Hamburg", "Husum", "Remote"],
  "benefits": ["Vier-Tage-Woche", "Weiterbildungsbudget", "Deutschlandticket", "Kein Bereitschaftsdienst"]
}' > /dev/null

grey "Publishing jobs …"
publish_job() {
  local id
  id=$(as_user "$COOKIE_COMPANY" POST /jobs "$1" | python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])')
  as_user "$COOKIE_COMPANY" POST "/jobs/$id/publish" > /dev/null
}

publish_job '{
  "title": "Senior Backend Engineer (.NET)",
  "description": "Du uebernimmst einen unserer Steuerungsdienste von der ersten Zeile bis in den Betrieb. Das heisst: du entscheidest ueber die Grenzen, du schreibst die Tests, und du bekommst den Anruf, wenn er nicht antwortet — deshalb hast du auch die Freiheit, ihn so zu bauen, dass er antwortet.\n\nWir arbeiten in .NET, mit PostgreSQL und Kubernetes. Kein Microservice-Zoo: sieben Dienste, klare Schnitte, jeder mit eigener Datenbank.",
  "location": "Hamburg",
  "postal_code": "20095",
  "remote_mode": "hybrid",
  "employment_type": "full_time",
  "skills": ["C#", ".NET", "PostgreSQL", "Kubernetes"]
}'

publish_job '{
  "title": "Site Reliability Engineer",
  "description": "Unsere Anlagen stehen auf See, und die Leitung dorthin ist nicht immer da. Du sorgst dafuer, dass die Systeme das aushalten: Beobachtbarkeit, die etwas sagt, Alarme, die jemand liest, und Wiederanlaufverfahren, die jemand schon einmal geuebt hat.",
  "location": "Husum",
  "postal_code": "25813",
  "remote_mode": "none",
  "employment_type": "full_time",
  "skills": ["Kubernetes", "Terraform", "Prometheus", "Linux"]
}'

publish_job '{
  "title": "Frontend-Entwicklung (React, Teilzeit)",
  "description": "Die Leitwarte ist eine Oberflaeche, an der Menschen acht Stunden sitzen. Sie muss lesbar sein, wenn draussen die Sonne blendet, und bedienbar mit Handschuhen. Du arbeitest eng mit den Leuten, die sie benutzen.",
  "location": "Hamburg",
  "postal_code": "20095",
  "remote_mode": "full",
  "employment_type": "part_time",
  "skills": ["React", "TypeScript", "Accessibility"]
}'

publish_job '{
  "title": "Werkstudent:in Datenanalyse",
  "description": "Aus Betriebsdaten Fragen beantworten, die vorher niemand gestellt hat. Kein fertiges Aufgabenpaket — wir zeigen dir die Daten und du sagst uns, was darin steckt.",
  "location": "Hamburg",
  "postal_code": "20095",
  "remote_mode": "hybrid",
  "employment_type": "internship",
  "skills": ["Python", "SQL"]
}'

grey "The company asks the person …"
as_user "$COOKIE_COMPANY" POST "/market/$SUBJECT/requests" > /dev/null || true
as_user "$COOKIE_COMPANY" POST "/resumes/$SUBJECT/requests" > /dev/null || true

echo
green "Done. Two accounts, both completely filled."
echo
printf '  \033[1mPerson\033[0m       %s\n' "$PERSON"
printf '  Password     %s\n' "$PASSWORD"
printf '               profile, CV (3 positions), 2 work samples, market status\n'
printf '               "listening", profile and work samples released publicly.\n'
echo
printf '  \033[1mCompany\033[0m      %s\n' "$RECRUITER"
printf '  Password     %s\n' "$PASSWORD"
printf '               %s, complete profile, 4 published jobs.\n' "$COMPANY_NAME"
printf '               Switch to the company at the top of the header.\n'
echo
printf '  Open for the person: a market status request and a CV request.\n'
printf '  Installation %s\n' "$GATEWAY"
