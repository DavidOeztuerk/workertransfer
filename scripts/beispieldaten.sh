#!/usr/bin/env bash
#
# Legt zwei vollstaendig gefuellte Konten an: eine Person und ein Unternehmen.
#
# WOZU. Die Anlage laeuft nach `make up` mit dem, was die E2E-Reisen
# hinterlassen haben — Stellen ohne Ort, Firmen ohne Profil, Menschen ohne
# Lebenslauf. Das ist zum Pruefen von Zusagen richtig und zum ANSEHEN nutzlos:
# jede Karte zeigt Leerstellen, und man haelt sie fuer Fehler. Genau so ist der
# Fehler `stelle.remoteundefined` monatelang uebersehen worden — in einer Karte
# ohne Ort und ohne Arbeitsform faellt eine weitere Luecke nicht auf.
#
# Dieses Skript fuellt JEDES Feld, das die Oberflaeche zeichnet.
#
# NUR FUER DIE ENTWICKLUNG. Es legt Konten mit bekannten Passwoertern an und
# geht durch den oeffentlichen Weg — Registrierung, Bestaetigung ueber Mailpit,
# Anmeldung. Es gibt keine Hintertuer, und das ist Absicht: was hier funktioniert,
# funktioniert auch fuer einen Menschen.
set -euo pipefail

TOR="${WT_GATEWAY:-http://localhost:8090}"
MAILPIT="${WT_MAILPIT:-http://localhost:8025}"
PASSWORT="beispiel-passwort-2026"

# Ein Zeitstempel im Namen, damit ein zweiter Lauf nicht an der vergebenen
# Adresse scheitert — der Endpunkt antwortet dann zwar gleich, legt aber nichts
# an, und man suchte den Fehler an der falschen Stelle.
STEMPEL="$(date +%s)"
PERSON="anna.beispiel+${STEMPEL}@example.org"
FIRMENDOMAIN="nordlicht-technik-${STEMPEL}.de"
WERBER="max.werber@${FIRMENDOMAIN}"
FIRMENNAME="Nordlicht Technik GmbH"

rot()  { printf '\033[31m%s\033[0m\n' "$*"; }
grau() { printf '\033[2m%s\033[0m\n' "$*"; }
gruen(){ printf '\033[32m%s\033[0m\n' "$*"; }

erreichbar() {
  curl -fsS -o /dev/null "$TOR/health/live" 2>/dev/null || {
    rot "Das Gateway antwortet nicht unter $TOR."
    rot "Erst 'make up', dann dieses Skript."
    exit 1
  }
  curl -fsS -o /dev/null "$MAILPIT/api/v1/messages?limit=1" 2>/dev/null || {
    rot "Mailpit antwortet nicht unter $MAILPIT — ohne Postfach kein Bestaetigungslink."
    exit 1
  }
}

# Der Bestaetigungstoken aus der zuletzt zugestellten Mail an eine Adresse.
token_aus_mail() {
  local adresse="$1" versuch=0
  while [ "$versuch" -lt 20 ]; do
    local id
    id=$(curl -fsS -G "$MAILPIT/api/v1/search" --data-urlencode "query=to:$adresse" \
         | python3 -c 'import json,sys; d=json.load(sys.stdin); print(d["messages"][0]["ID"] if d.get("messages") else "")')
    if [ -n "$id" ]; then
      curl -fsS "$MAILPIT/api/v1/message/$id" \
        | python3 -c 'import json,re,sys; d=json.load(sys.stdin); m=re.search(r"token=([A-Za-z0-9_-]+)", d.get("Text","")); print(m.group(1) if m else "")'
      return 0
    fi
    versuch=$((versuch + 1)); sleep 0.5
  done
  rot "Keine Mail an $adresse in Mailpit."; exit 1
}

# Registriert, bestaetigt und meldet an. Legt den Keks in $2 ab.
konto() {
  local adresse="$1" keks="$2" name="$3" firma="${4:-}"
  local rumpf
  if [ -n "$firma" ]; then
    rumpf=$(printf '{"email":"%s","password":"%s","display_name":"%s","company_name":"%s"}' \
            "$adresse" "$PASSWORT" "$name" "$firma")
  else
    rumpf=$(printf '{"email":"%s","password":"%s","display_name":"%s"}' \
            "$adresse" "$PASSWORT" "$name")
  fi

  curl -fsS -o /dev/null -X POST "$TOR/auth/register" \
    -H 'Content-Type: application/json' -d "$rumpf"

  local token; token=$(token_aus_mail "$adresse")
  curl -fsS -o /dev/null -X POST "$TOR/auth/verify-email" \
    -H 'Content-Type: application/json' -d "{\"token\":\"$token\"}"

  curl -fsS -o /dev/null -c "$keks" -X POST "$TOR/auth/login" \
    -H 'Content-Type: application/json' \
    -d "{\"email\":\"$adresse\",\"password\":\"$PASSWORT\"}"
}

# Ein Aufruf mit Keks. `als <keks> <methode> <pfad> [rumpf]`
als() {
  local keks="$1" methode="$2" pfad="$3" rumpf="${4:-}"
  if [ -n "$rumpf" ]; then
    curl -fsS -b "$keks" -X "$methode" "$TOR$pfad" \
      -H 'Content-Type: application/json' -d "$rumpf"
  else
    curl -fsS -b "$keks" -X "$methode" "$TOR$pfad"
  fi
}

erreichbar

KEKS_PERSON="$(mktemp)"
KEKS_FIRMA="$(mktemp)"
trap 'rm -f "$KEKS_PERSON" "$KEKS_FIRMA"' EXIT

grau "Person anlegen …"
konto "$PERSON" "$KEKS_PERSON" "Anna Beispiel"

grau "Profil, Lebenslauf, Arbeiten, Marktstatus …"

als "$KEKS_PERSON" PUT /profiles/me '{
  "headline": "Backend-Entwicklerin mit Faible fuer verteilte Systeme",
  "bio": "Seit acht Jahren baue ich Dienste, die auch dann noch antworten, wenn die Haelfte drumherum nicht mehr antwortet. Am liebsten dort, wo eine Entscheidung im Code nachvollziehbar bleibt: klare Grenzen zwischen Diensten, Vertraege, die man lesen kann, und Tests, die eine Zusage halten statt eine Zeile abzudecken.\n\nZuletzt viel mit Event-Streams und Datenwanderungen zu tun gehabt. Ich mag Systeme, die man abschalten kann, ohne dass jemand anruft.",
  "location": "Hamburg",
  "remote_ok": true,
  "skills": ["C#", ".NET", "PostgreSQL", "Kubernetes", "Docker", "Python", "Go", "Terraform"]
}' > /dev/null

als "$KEKS_PERSON" PUT /resumes/me '{
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

als "$KEKS_PERSON" PUT /portfolios/me '{
  "items": [
    {"title": "Wanderung ohne Stillstand", "summary": "Ein Verfahren, mit dem ein Schema in Schritten wandert, waehrend beide Fassungen gleichzeitig laufen. Vortrag und Vorlage.",
     "url": "https://example.org/wanderung", "role": "Konzept und Umsetzung", "year": 2025, "attachment": null},
    {"title": "Lastprobe fuer Messdatenstroeme", "summary": "Ein kleines Werkzeug, das einen realistischen Strom erzeugt statt einer Schleife — sonst misst man den Zwischenspeicher.",
     "url": "https://example.org/lastprobe", "role": "Autorin", "year": 2023, "attachment": null}
  ]
}' > /dev/null

als "$KEKS_PERSON" PUT /market/me '{
  "availability": "listening",
  "employed": true,
  "note": "Ich suche nicht aktiv, hoere aber zu. Wichtig waeren mir: Verantwortung fuer einen Dienst von Anfang bis Betrieb, ein Team, das Entscheidungen aufschreibt, und Hamburg oder remote."
}' > /dev/null

grau "Freigaben erteilen …"
WER=$(als "$KEKS_PERSON" GET /auth/session | python3 -c 'import json,sys; print(json.load(sys.stdin)["user"]["user_id"])')
for faehigkeit in "profile.visibility:public" "portfolio.visibility:public"; do
  als "$KEKS_PERSON" POST /consent/grant \
    "{\"subject_id\":\"$WER\",\"capability\":\"$faehigkeit\"}" > /dev/null
done

grau "Unternehmen anlegen …"
konto "$WERBER" "$KEKS_FIRMA" "Max Werber" "$FIRMENNAME"

MANDANT=$(als "$KEKS_FIRMA" GET /me/companies \
  | python3 -c 'import json,sys; d=json.load(sys.stdin); print((d if isinstance(d,list) else d.get("companies",d.get("items",[])))[0]["id"])')
# `-c` UND `-b`: der Wechsel praegt ein NEUES Token mit dem Mandanten, und ohne
# das Zurueckschreiben behielte die Datei das alte — jeder Firmenaufruf danach
# bekaeme 403, und man suchte den Fehler bei der Berechtigung.
curl -fsS -o /dev/null -b "$KEKS_FIRMA" -c "$KEKS_FIRMA" \
  -X POST "$TOR/auth/company/$MANDANT"

grau "Unternehmensprofil …"
als "$KEKS_FIRMA" PUT /companies/me/profile '{
  "display_name": "Nordlicht Technik GmbH",
  "about": "Wir bauen Steuerungssoftware fuer Windparks an der Nordsee. Zweiundvierzig Leute, davon achtzehn in der Entwicklung, seit 2014. Was wir liefern, laeuft im Zweifel ohne Netzverbindung weiter — das praegt, wie wir Software schreiben.\n\nWir schreiben Entscheidungen auf, wir deployen freitags, und wir haben keine Bereitschaft ausserhalb der Arbeitszeit.",
  "website": "https://example.org/nordlicht",
  "locations": ["Hamburg", "Husum", "Remote"],
  "benefits": ["Vier-Tage-Woche", "Weiterbildungsbudget", "Deutschlandticket", "Kein Bereitschaftsdienst"]
}' > /dev/null

grau "Stellen ausschreiben …"
ausschreiben() {
  local id
  id=$(als "$KEKS_FIRMA" POST /jobs "$1" | python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])')
  als "$KEKS_FIRMA" POST "/jobs/$id/publish" > /dev/null
}

ausschreiben '{
  "title": "Senior Backend Engineer (.NET)",
  "description": "Du uebernimmst einen unserer Steuerungsdienste von der ersten Zeile bis in den Betrieb. Das heisst: du entscheidest ueber die Grenzen, du schreibst die Tests, und du bekommst den Anruf, wenn er nicht antwortet — deshalb hast du auch die Freiheit, ihn so zu bauen, dass er antwortet.\n\nWir arbeiten in .NET, mit PostgreSQL und Kubernetes. Kein Microservice-Zoo: sieben Dienste, klare Schnitte, jeder mit eigener Datenbank.",
  "location": "Hamburg",
  "postal_code": "20095",
  "remote_mode": "hybrid",
  "employment_type": "full_time",
  "skills": ["C#", ".NET", "PostgreSQL", "Kubernetes"]
}'

ausschreiben '{
  "title": "Site Reliability Engineer",
  "description": "Unsere Anlagen stehen auf See, und die Leitung dorthin ist nicht immer da. Du sorgst dafuer, dass die Systeme das aushalten: Beobachtbarkeit, die etwas sagt, Alarme, die jemand liest, und Wiederanlaufverfahren, die jemand schon einmal geuebt hat.",
  "location": "Husum",
  "postal_code": "25813",
  "remote_mode": "none",
  "employment_type": "full_time",
  "skills": ["Kubernetes", "Terraform", "Prometheus", "Linux"]
}'

ausschreiben '{
  "title": "Frontend-Entwicklung (React, Teilzeit)",
  "description": "Die Leitwarte ist eine Oberflaeche, an der Menschen acht Stunden sitzen. Sie muss lesbar sein, wenn draussen die Sonne blendet, und bedienbar mit Handschuhen. Du arbeitest eng mit den Leuten, die sie benutzen.",
  "location": "Hamburg",
  "postal_code": "20095",
  "remote_mode": "full",
  "employment_type": "part_time",
  "skills": ["React", "TypeScript", "Accessibility"]
}'

ausschreiben '{
  "title": "Werkstudent:in Datenanalyse",
  "description": "Aus Betriebsdaten Fragen beantworten, die vorher niemand gestellt hat. Kein fertiges Aufgabenpaket — wir zeigen dir die Daten und du sagst uns, was darin steckt.",
  "location": "Hamburg",
  "postal_code": "20095",
  "remote_mode": "hybrid",
  "employment_type": "internship",
  "skills": ["Python", "SQL"]
}'

grau "Das Unternehmen fragt die Person an …"
als "$KEKS_FIRMA" POST "/market/$WER/requests" > /dev/null || true
als "$KEKS_FIRMA" POST "/resumes/$WER/requests" > /dev/null || true

echo
gruen "Fertig. Zwei Konten, beide vollstaendig gefuellt."
echo
printf '  \033[1mPerson\033[0m       %s\n' "$PERSON"
printf '  Passwort     %s\n' "$PASSWORT"
printf '               Profil, Lebenslauf (3 Stationen), 2 Arbeiten, Marktstatus\n'
printf '               „hoere zu", Profil und Arbeiten oeffentlich freigegeben.\n'
echo
printf '  \033[1mUnternehmen\033[0m  %s\n' "$WERBER"
printf '  Passwort     %s\n' "$PASSWORT"
printf '               %s, vollstaendiges Profil, 4 veroeffentlichte Stellen.\n' "$FIRMENNAME"
printf '               Oben im Kopf auf das Unternehmen wechseln.\n'
echo
printf '  Offen fuer die Person: eine Marktstatus-Anfrage und eine Lebenslauf-Anfrage.\n'
printf '  Anlage:      %s\n' "$TOR"
