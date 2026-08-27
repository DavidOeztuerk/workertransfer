#!/usr/bin/env bash
# Die Routenkarte gegen den LAUFENDEN Stapel fahren.
#
#   make up && ./scripts/routenkarte.sh
#
# `docs/routenkarte.yml` haelt fuer jeden Endpunkt fest, was er in den drei
# Handlungsformen aus ADR-0017 antwortet. Hier wird gefragt, ob das noch stimmt.
#
# Die Vollstaendigkeit der Karte prueft ein anderer Test (RoutenkarteTests in
# WorkerTransfer.Gateway.Tests) — der braucht keinen Stapel und laeuft in jeder
# Reihe mit. Beide zusammen ergeben die Zusage: jede Route hat einen Eintrag,
# und jeder Eintrag stimmt.
#
# ---------------------------------------------------------------------------
# WARUM EIN SKRIPT UND KEINE TESTREIHE
#
# Eine Reihe, die einen laufenden Stapel braucht, ueberspringt sich ohne ihn —
# und ein uebersprungener Test sieht aus wie ein bestandener. Genau diese
# Verwechslung hat in diesem Repo schon Zeit gekostet. Ein Skript, das man
# ruft, kann sich nicht selbst wegdruecken: es laeuft oder es sagt, warum
# nicht.
set -euo pipefail

cd "$(dirname "$0")/.."

BASE="${WORKERTRANSFER_BASE:-http://localhost:8090}"
MAIL="${WORKERTRANSFER_MAIL:-http://localhost:8025}"
KARTE="docs/routenkarte.yml"
PW="ein-ausreichend-langes-passwort"
ARBEIT="$(mktemp -d)"
trap 'rm -rf "$ARBEIT"' EXIT

rot()   { printf '\033[31m%s\033[0m\n' "$*"; }
gruen() { printf '\033[32m%s\033[0m\n' "$*"; }
grau()  { printf '\033[2m%s\033[0m\n' "$*"; }

command -v python3 >/dev/null || { rot "python3 fehlt."; exit 1; }

curl -sf -o /dev/null "${BASE}/health/live" || {
  rot "Das Gateway antwortet nicht auf ${BASE}/health/live."
  rot "Der Stapel laeuft nicht — 'make up' und noch einmal."
  exit 1
}

# ---------------------------------------------------------------------------
# Die drei Handlungsformen besorgen.
#
# EIGENE DOMAENE JE LAUF fuer die Firma: eine Domaene laesst sich nur einmal
# beanspruchen (ADR-0019). Beim zweiten Lauf gegen dieselbe wird die Firma bei
# der Bestaetigung abgelehnt — das Konto ist trotzdem bestaetigt, und dann
# laeuft die Messung mit einem PERSONEN-Token in der Firmenspalte weiter, ohne
# dass irgendetwas rot wird. Genau so ist dieses Skript beim Bauen einmal in
# die Irre gelaufen.
stempel="$(date +%s)-$$"
PERSON="rk-person-${stempel}@example.org"
FIRMA="chef@rk-${stempel}.example"

token_aus_mail() {
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

anlegen() {
  if [ -n "${2:-}" ]; then
    rumpf="{\"email\":\"$1\",\"password\":\"$PW\",\"displayName\":\"Routenkarte\",\"companyName\":\"$2\"}"
  else
    rumpf="{\"email\":\"$1\",\"password\":\"$PW\",\"displayName\":\"Routenkarte\"}"
  fi
  curl -sf -o /dev/null -X POST "${BASE}/auth/register" \
    -H 'Content-Type: application/json' -d "$rumpf"
  t="$(token_aus_mail "$1")" || { rot "Keine Bestaetigungsmail fuer $1."; exit 1; }
  curl -sf -o /dev/null -X POST "${BASE}/auth/verify-email" \
    -H 'Content-Type: application/json' -d "{\"token\":\"$t\"}"
  curl -sf -o /dev/null -c "${ARBEIT}/$3.cookie" -X POST "${BASE}/auth/login" \
    -H 'Content-Type: application/json' -d "{\"email\":\"$1\",\"password\":\"$PW\"}"
}

grau "Handlungsformen anlegen ..."
anlegen "$PERSON" "" person
anlegen "$FIRMA" "Routenkarte ${stempel}" firma

TENANT=$(curl -sf -b "${ARBEIT}/firma.cookie" "${BASE}/me/companies" | python3 -c "
import json,sys
d=json.load(sys.stdin)
d=d if isinstance(d,list) else (d.get('companies') or d.get('items') or [])
print(d[0]['id'] if d else '')")

[ -n "$TENANT" ] || { rot "Die Firma wurde nicht angelegt — Domaene schon vergeben?"; exit 1; }

curl -sf -o /dev/null -b "${ARBEIT}/firma.cookie" -c "${ARBEIT}/firma.cookie" \
  -X POST "${BASE}/auth/company/${TENANT}"

# Der Beleg, dass die dritte Spalte wirklich eine dritte ist. Ohne ihn misst
# man womoeglich zweimal dieselbe Handlungsform.
traegt=$(curl -sf -b "${ARBEIT}/firma.cookie" "${BASE}/me" | python3 -c "
import json,sys; print(json.load(sys.stdin).get('tenant_id') or '')")
[ -n "$traegt" ] || { rot "Das Firmentoken traegt keinen Mandanten."; exit 1; }
grau "  Person ohne Firma, Firma mit Mandant ${traegt:0:8}…"

# ---------------------------------------------------------------------------
# `/account/erasure` loescht den Aufrufer. Ein Durchlauf, der stumpf jeden
# Endpunkt anfasst, nimmt sich dabei selbst das Konto weg und misst danach
# lauter 401, die keine Zusage sind, sondern eine Folge. Also uebersprungen —
# und zwar SICHTBAR, nicht stillschweigend.
UEBERSPRUNGEN="/account/erasure"

python3 - "$KARTE" "$BASE" "$ARBEIT" "$UEBERSPRUNGEN" <<'PY'
import json, subprocess, sys, urllib.request, urllib.error

karte, base, arbeit, uebersprungen = sys.argv[1:5]

def karte_lesen(pfad):
    """YAML lesen, ohne sich auf EIN Werkzeug zu verlassen.

    Dieses Skript laeuft auch in der CI. Dort ist Ruby heute da — aber ein
    Beleg, der an einem nicht erklaerten Werkzeug haengt, faellt irgendwann
    ohne erkennbaren Zusammenhang aus. Also erst PyYAML, dann Ruby, und wenn
    beides fehlt: sagen, was fehlt, statt gruen zu enden.
    """
    try:
        import yaml                                   # noqa: PLC0415
        with open(pfad) as datei:
            return yaml.safe_load(datei)
    except ImportError:
        pass

    lauf = subprocess.run(
        ["ruby", "-ryaml", "-rjson", "-e", f"puts YAML.load_file({pfad!r}).to_json"],
        capture_output=True, text=True)
    if lauf.returncode != 0:
        print("Weder PyYAML noch Ruby vorhanden — die Karte ist nicht lesbar.")
        print(lauf.stderr.strip())
        sys.exit(1)
    return json.loads(lauf.stdout)


gruppen = karte_lesen(karte)["gruppen"]

def frag(methode, pfad, keks):
    anfrage = urllib.request.Request(
        base + pfad, method=methode, data=b"{}",
        headers={"Content-Type": "application/json", **({"Cookie": keks} if keks else {})})
    try:
        with urllib.request.urlopen(anfrage) as antwort:
            return antwort.status
    except urllib.error.HTTPError as fehler:
        return fehler.code
    except Exception as fehler:                      # noqa: BLE001
        return f"<{type(fehler).__name__}>"

def keks_aus(datei):
    # `#HttpOnly_` NICHT als Kommentar wegwerfen. Genau diese Zeilen tragen den
    # Zugriffstoken — der Browser bekommt ihn ausschliesslich als httpOnly-Keks
    # (ADR-0006/0007). Wer sie ueberspringt, misst jede angemeldete Zeile als
    # 401 und haelt danach die halbe Karte fuer falsch.
    zeilen = []
    for zeile in open(f"{arbeit}/{datei}.cookie").read().splitlines():
        if zeile.startswith("#HttpOnly_"):
            zeile = zeile[len("#HttpOnly_"):]
        elif zeile.startswith("#") or not zeile.strip():
            continue
        zeilen.append(zeile.split("\t"))
    return "; ".join(f"{t[5]}={t[6]}" for t in zeilen if len(t) >= 7)

spalten = {"ohne": None, "person": keks_aus("person"), "firma": keks_aus("firma")}

abweichungen, geprueft, uebersprungen_n = [], 0, 0

for gruppe in gruppen:
    for eintrag in gruppe["eintraege"]:
        if eintrag["pfad"] in uebersprungen.split(","):
            uebersprungen_n += 1
            print(f"  \033[2muebersprungen: {eintrag['methode']} {eintrag['pfad']} "
                  f"(loescht den Aufrufer)\033[0m")
            continue
        for spalte, keks in spalten.items():
            geprueft += 1
            ist = frag(eintrag["methode"], eintrag["pfad"], keks)
            soll = eintrag[spalte]
            if ist != soll:
                abweichungen.append(
                    (gruppe["name"], eintrag["methode"], eintrag["pfad"], spalte, soll, ist))

print()
if abweichungen:
    print(f"\033[31m{len(abweichungen)} Abweichung(en):\033[0m")
    for name, methode, pfad, spalte, soll, ist in abweichungen:
        print(f"  \033[31m{methode:7} {pfad:52} {spalte:6} erwartet {soll}, bekam {ist}\033[0m")
        print(f"          \033[2min Gruppe: {name}\033[0m")
    print()
    print("Eine Abweichung ist KEIN Grund, die Karte anzupassen. Erst pruefen,")
    print("welche der beiden Seiten recht hat — die Karte haelt die ABSICHT fest.")
    sys.exit(1)

print(f"\033[32m{geprueft} Antworten geprueft, alle wie aufgeschrieben.\033[0m")
if uebersprungen_n:
    print(f"\033[2m({uebersprungen_n} uebersprungen, oben benannt.)\033[0m")
PY
