#!/usr/bin/env bash
# Testdaten für die lokale Staging-Umgebung: ein Unternehmen, drei
# veröffentlichte Stellen, ein Bewerber-Konto.
#
#   make k8s-seed
#
# **Über die echte API, nicht per SQL.** Eine Stelle hat eine tenant_id, einen
# Status und einen Verlauf; rohes SQL erzeugt leicht eine Zeile, die es über die
# Anwendung nie gäbe — und man sucht den Fehler später im Code. Der Weg hier
# belegt nebenbei, dass Registrieren, Bestätigen, Unternehmensgründung,
# Tenant-Wechsel und Veröffentlichen wirklich zusammenspielen.
#
# Mehrfach ausführbar: jede Ausführung legt neue Konten mit einem Zeitstempel im
# Namen an. Aufräumen tut es nichts — dafür gibt es `make k8s-down`.
set -euo pipefail

cd "$(dirname "$0")/.."

BASE=${BASE:-http://localhost:8090}
MAILPIT=${MAILPIT:-http://localhost:8025}
PW=${SEED_PASSWORT:-ein-ausreichend-langes-passwort}

rot(){ printf '\033[31m%s\033[0m\n' "$*"; }
gruen(){ printf '\033[32m%s\033[0m\n' "$*"; }
schritt(){ printf '\n\033[1m==> %s\033[0m\n' "$*"; }

curl -sf -o /dev/null -m 5 "$BASE/jobs" || {
  rot "Unter $BASE antwortet niemand. Läuft 'make k8s-up'?"
  exit 1
}
curl -sf -o /dev/null -m 5 "$MAILPIT/api/v1/messages" || {
  rot "Mailpit unter $MAILPIT antwortet nicht — ohne die Bestätigungsmail geht hier nichts."
  exit 1
}

# Holt den Bestätigungslink aus Mailpit und löst ihn ein. Ohne diesen Schritt
# bliebe jedes Konto PENDING und könnte sich nicht anmelden.
bestaetigen() {
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
  [ -n "${id:-}" ] || { rot "keine Bestätigungsmail für $mail"; return 1; }
  token=$(curl -s "$MAILPIT/api/v1/message/$id" | python3 -c "
import json,sys,re
d=json.load(sys.stdin); t=d.get('Text','')+d.get('HTML','')
m=re.search(r'token=([A-Za-z0-9_.-]+)', t); print(m.group(1) if m else '')")
  curl -s -o /dev/null -X POST "$BASE/auth/verify-email" \
    -H 'Content-Type: application/json' -d "{\"token\":\"$token\"}"
}

konto() {  # konto <mail> <anzeigename> <cookiedatei>
  curl -s -o /dev/null -X POST "$BASE/auth/register" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$1\",\"password\":\"$PW\",\"display_name\":\"$2\"}"
  bestaetigen "$1"
  curl -s -c "$3" -o /dev/null -X POST "$BASE/auth/login" \
    -H 'Content-Type: application/json' -d "{\"email\":\"$1\",\"password\":\"$PW\"}"
}

stamp=$(date +%s)
# KEINE Freemail-Adresse: die Unternehmensdomain wird aus der bestätigten
# Adresse abgeleitet (ADR-0019), und gmail.com & Co. sind dafür gesperrt.
FIRMA_MAIL="admin@nordwind-technik-${stamp}.de"
PERSON_MAIL="bewerberin-${stamp}@example.org"
JAR_FIRMA=$(mktemp); JAR_PERSON=$(mktemp)
trap 'rm -f "$JAR_FIRMA" "$JAR_PERSON"' EXIT

schritt "Unternehmenskonto anlegen"
konto "$FIRMA_MAIL" "Nordwind Admin" "$JAR_FIRMA"

schritt "Unternehmen gründen und hineinwechseln"
firma=$(curl -s -b "$JAR_FIRMA" -X POST "$BASE/companies" \
  -H 'Content-Type: application/json' -d '{"name":"Nordwind Technik"}')
tid=$(printf '%s' "$firma" | python3 -c "import json,sys; print(json.load(sys.stdin).get('id',''))")
[ -n "$tid" ] || { rot "Unternehmen nicht angelegt: $firma"; exit 1; }
# Der Wechsel ist nötig, BEVOR Stellen entstehen: das Token einer Person trägt
# keine tenant_id, und ohne die gibt es kein Unternehmen, dem eine Anzeige
# gehören könnte (ADR-0017/0018).
curl -s -b "$JAR_FIRMA" -c "$JAR_FIRMA" -o /dev/null -X POST "$BASE/auth/company/$tid"
echo "    tenant_id=$tid"

schritt "Unternehmensprofil"
# Ohne Profil antwortet /companies/<id>/profile mit 404, und die Stellenliste
# zeigt keinen Firmennamen — ein ehrlicher 404, aber eine kahle Seite.
curl -s -b "$JAR_FIRMA" -o /dev/null -X PUT "$BASE/companies/me/profile" \
  -H 'Content-Type: application/json' -d '{
    "display_name": "Nordwind Technik",
    "about": "Wir bauen Abrechnungssysteme fuer Energieversorger. 40 Menschen, zwei Standorte.",
    "website": "https://nordwind-technik.example",
    "locations": ["Hamburg", "Kiel"],
    "benefits": ["Vier-Tage-Woche moeglich", "Feste Dienstplaene", "Kein Bereitschaftsdienst"]
  }'
echo "    Karriere-Seite: $BASE/karriere/nordwind-technik"

veroeffentliche() {  # veroeffentliche <titel> <ort> <remote> <skills-json> <beschreibung>
  local job jid
  job=$(curl -s -b "$JAR_FIRMA" -X POST "$BASE/jobs" -H 'Content-Type: application/json' \
    -d "{\"title\":\"$1\",\"description\":\"$5\",\"location\":\"$2\",\"remote\":\"$3\",\"employment\":\"full_time\",\"skills\":$4}")
  jid=$(printf '%s' "$job" | python3 -c "import json,sys; print(json.load(sys.stdin).get('id',''))")
  [ -n "$jid" ] || { rot "    nicht angelegt: $job"; return 1; }
  # Anlegen ist ein Entwurf; sichtbar wird eine Stelle erst durch das
  # Veröffentlichen.
  curl -s -b "$JAR_FIRMA" -o /dev/null -X POST "$BASE/jobs/$jid/publish"
  echo "    ✓ $1"
}

schritt "Stellen veröffentlichen"
veroeffentliche "Backend-Entwicklung Python" "Hamburg" "hybrid" '["Python","PostgreSQL","Kubernetes"]' \
  "Wir bauen die Abrechnung neu. Kleines Team, echte Verantwortung, keine Bereitschaftsdienste."
veroeffentliche "Frontend-Entwicklung React" "Berlin" "full" '["TypeScript","React","CSS"]' \
  "Unsere Oberflaeche soll schneller und zugaenglicher werden. Barrierefreiheit ist kein Nachgedanke."
veroeffentliche "Pflegefachkraft Intensiv" "Kiel" "none" '["Intensivpflege","Beatmung"]' \
  "Feste Dienstplaene vier Wochen im Voraus. Ausfaelle deckt ein Springerpool, nicht die Freizeit."

schritt "Bewerber-Konto anlegen"
konto "$PERSON_MAIL" "Testbewerberin" "$JAR_PERSON"

schritt "Nachzählen — nicht behaupten"
anzahl=$(curl -s "$BASE/jobs" | python3 -c "import json,sys; print(len(json.load(sys.stdin)['items']))")
[ "$anzahl" -ge 3 ] || { rot "Nur $anzahl Stellen öffentlich sichtbar, erwartet mindestens 3."; exit 1; }
gruen "$anzahl Stellen sind ohne Anmeldung sichtbar."

cat <<TEXT

  Stellen            $BASE/jobs

  Bewerberin         $PERSON_MAIL
  Unternehmen        $FIRMA_MAIL
  Passwort (beide)   $PW

  Mails              $MAILPIT

TEXT
