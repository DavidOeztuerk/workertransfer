#!/usr/bin/env bash
# Sammelt den Nachweis jedes Dienstes gegen den LAUFENDEN Stapel und schreibt
# drei datierte, signierte Dokumente.
#
# BELEGE, KEINE KONFORMITAET. Kein Artefakt aus diesem Skript sagt „konform",
# „zertifiziert" oder „erfuellt Art. X". Nach Art. 42/43 DSGVO darf nur eine
# Aufsichtsbehoerde oder eine nach EN ISO/IEC 17065 akkreditierte Stelle
# zertifizieren; „zertifiziert" ohne Akkreditierung ist in der EU eine
# irrefuehrende Geschaeftspraxis (RL 2005/29/EG, RL 2006/114/EG).
#
# DREI DOKUMENTE UND NICHT EINES, weil sie von drei Menschen fuer drei Zwecke
# gelesen werden — und weil man aus einem gemischten Dokument den einen Teil
# zitieren kann, ohne den Teil zu zitieren, der ihn einschraenkt.
#
#   scripts/nachweis.sh              # gegen den laufenden Stapel
#   scripts/nachweis.sh --aus nachweis/   # wohin
#
# Braucht `make up` und das Geheimnis aus .env.

set -uo pipefail
cd "$(dirname "$0")/.."

BOLD=$'\033[1m'; RED=$'\033[31m'; GREEN=$'\033[32m'; GELB=$'\033[33m'; AUS=$'\033[0m'

rot()  { printf '%s✗%s %s\n' "$RED" "$AUS" "$1" >&2; }
gelb() { printf '%s!%s %s\n' "$GELB" "$AUS" "$1"; }
gruen(){ printf '%s✓%s %s\n' "$GREEN" "$AUS" "$1"; }

ZIEL="nachweis"
[[ "${1:-}" == "--aus" ]] && ZIEL="${2:?--aus braucht ein Verzeichnis}"

for werkzeug in curl openssl python3; do
  command -v "$werkzeug" >/dev/null || { rot "$werkzeug fehlt."; exit 1; }
done

# Das Geheimnis kommt aus .env — dieselbe Datei, aus der der Stapel es hat.
# Ohne es antwortet jede Nachweisadresse mit 404, und das ist die richtige
# Antwort: leer heisst, die Tuer ist zu.
if [[ -f .env ]]; then
  # shellcheck disable=SC1091
  GEHEIMNIS=$(grep -E '^WORKERTRANSFER_NACHWEIS_SECRET=' .env | head -1 | cut -d= -f2-)
fi
GEHEIMNIS="${WORKERTRANSFER_NACHWEIS_SECRET:-${GEHEIMNIS:-}}"

if [[ -z "$GEHEIMNIS" ]]; then
  rot "WORKERTRANSFER_NACHWEIS_SECRET ist nicht gesetzt."
  echo "   \`make env\` legt eine .env an und wuerfelt es mit." >&2
  exit 1
fi

# Die vierzehn Dienste und ihre Haefen — dieselbe Liste wie in
# docker-compose.yml. Sie steht hier ein zweites Mal, und das ist der Preis
# dafuer, dass dieses Skript OHNE Docker-Kommandos auskommt: es fragt ueber
# HTTP, so wie es auch gegen ein Staging fragen wuerde.
DIENSTE=(
  "identity:8001" "consent:8002" "profile:8003" "resume:8004"
  "portfolio:8005" "jobs:8006" "applications:8007" "companies:8008"
  "transfer:8009" "notification:8010" "github:8011" "scout:8012"
  "advisor:8013" "assessment:8014"
)

ARBEIT=$(mktemp -d)
trap 'rm -rf "$ARBEIT"' EXIT

printf '%sNachweis wird eingesammelt%s\n' "$BOLD" "$AUS"

ERREICHT=0
STUMM=()

for eintrag in "${DIENSTE[@]}"; do
  name="${eintrag%%:*}"
  hafen="${eintrag##*:}"

  if curl -fsS --max-time 10 \
       -H "X-Nachweis-Secret: ${GEHEIMNIS}" \
       "http://localhost:${hafen}/noelia/report.json" \
       -o "${ARBEIT}/${name}.json" 2>/dev/null; then
    ERREICHT=$((ERREICHT + 1))
    printf '  %s✓%s %-14s\n' "$GREEN" "$AUS" "$name"
  else
    STUMM+=("$name")
    printf '  %s✗%s %-14s antwortet nicht\n' "$RED" "$AUS" "$name"
  fi
done

if [[ $ERREICHT -eq 0 ]]; then
  rot "Kein Dienst hat geantwortet. Laeuft der Stapel? \`make up\`."
  echo "   Und traegt er das Geheimnis? Eine .env ohne" >&2
  echo "   WORKERTRANSFER_NACHWEIS_SECRET laesst compose gar nicht erst starten." >&2
  exit 1
fi

# ---------------------------------------------------------------------------
# DER SCHLUESSEL.
#
# ECDSA P-256 mit SHA-256. Er wird beim ersten Lauf angelegt und liegt unter
# .nachweis/ — git-ignoriert, denn ein privater Schluessel in git ist kein
# privater Schluessel mehr. Wer die Dokumente an jemanden gibt, gibt den
# OEFFENTLICHEN Teil dazu; er steht in jeder Signaturdatei.
# ---------------------------------------------------------------------------
SCHLUESSEL="${WORKERTRANSFER_NACHWEIS_KEY:-.nachweis/schluessel.pem}"

if [[ ! -f "$SCHLUESSEL" ]]; then
  mkdir -p "$(dirname "$SCHLUESSEL")"
  openssl ecparam -name prime256v1 -genkey -noout -out "$SCHLUESSEL" 2>/dev/null
  chmod 600 "$SCHLUESSEL"
  gelb "Neuer Signaturschluessel: $SCHLUESSEL"
  echo "   Er ist git-ignoriert. Ein neuer Schluessel heisst: alte Dokumente"
  echo "   verifizieren mit dem neuen oeffentlichen Teil NICHT mehr."
fi

mkdir -p "$ZIEL"

# ---------------------------------------------------------------------------
# DIE DREI DOKUMENTE.
#
# python3 baut sie, weil die KANONISCHE FORM hier das Ganze traegt: sortierte
# Schluessel, keine bedeutungslosen Leerzeichen. Ein Pruefer rechnet sie mit
# `json.dumps(sort_keys=True, separators=(',', ':'), ensure_ascii=False)` nach
# — eine Zeile, in jeder Sprache dieselbe Bytefolge.
# ---------------------------------------------------------------------------
python3 scripts/nachweis_dokumente.py "$ARBEIT" "$ZIEL" "${STUMM[*]:-}" || {
  rot "Die Dokumente liessen sich nicht bauen."
  exit 1
}

# ---------------------------------------------------------------------------
# SIGNIEREN.
#
# Ueber die KANONISCHE Form, nicht ueber die Datei: eine Datei traegt
# Einrueckung und einen Zeilenumbruch am Ende, und beides ist bedeutungslos.
# Wer nachrechnet, kanonisiert zuerst und prueft dann — sonst haengt die
# Signatur an der Formatierung statt am Inhalt.
# ---------------------------------------------------------------------------
OEFFENTLICH=$(openssl ec -in "$SCHLUESSEL" -pubout 2>/dev/null | \
  openssl base64 -A)

for dokument in datenschutz ki mitbestimmung; do
  datei="${ZIEL}/${dokument}.json"
  [[ -f "$datei" ]] || continue

  python3 -c "
import json, sys
with open(sys.argv[1], encoding='utf-8') as f:
    d = json.load(f)
sys.stdout.write(json.dumps(d, sort_keys=True, separators=(',', ':'), ensure_ascii=False))
" "$datei" > "${ARBEIT}/${dokument}.kanonisch"

  signatur=$(openssl dgst -sha256 -sign "$SCHLUESSEL" \
    "${ARBEIT}/${dokument}.kanonisch" | openssl base64 -A)

  pruefsumme=$(openssl dgst -sha256 -binary "${ARBEIT}/${dokument}.kanonisch" | \
    openssl base64 -A)

  python3 -c "
import json, sys
print(json.dumps({
    'algorithm': 'ecdsa-p256-sha256',
    'canonical_form': \"json.dumps(sort_keys=True, separators=(',',':'), ensure_ascii=False)\",
    'canonical_sha256': sys.argv[1],
    'public_key_pem_base64': sys.argv[2],
    'signature': sys.argv[3],
    'verify': 'scripts/nachweis-pruefen.sh --dokumente',
}, indent=2, ensure_ascii=False, sort_keys=True))
" "$pruefsumme" "$OEFFENTLICH" "$signatur" > "${ZIEL}/${dokument}.sig.json"

  gruen "${ZIEL}/${dokument}.json + .sig.json"
done

printf '\n%s%s von %s Diensten gelesen.%s\n' \
  "$BOLD" "$ERREICHT" "${#DIENSTE[@]}" "$AUS"

if [[ ${#STUMM[@]} -gt 0 ]]; then
  gelb "Nicht geantwortet: ${STUMM[*]}"
  echo "   Sie stehen NAMENTLICH in jedem Dokument — ein Dienst, der schweigt,"
  echo "   darf nicht wie einer aussehen, der nichts zu melden hat."
fi
