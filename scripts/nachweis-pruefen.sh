#!/usr/bin/env bash
# Faehrt jede Pruefung in jedem Dienst und geht ROT, sobald eine Zusage nicht
# eingeloest ist.
#
# ROT NUR BEI `fehlt`. Ein `hinweis` ist kein Mangel, sondern eine Frage an
# einen Menschen — dass Modellaufrufe nicht aufgezeichnet werden, ist ADR-0024
# und keine Luecke. Ein Tor, das auf eine gewollte Zurueckhaltung rot geht,
# schaltet der naechste Mensch ab, und dann ist es gar keins mehr.
#
# `nichtanwendbar` ist ebenfalls gruen und ausdruecklich KEIN Haken: es heisst,
# dass es den Gegenstand hier nicht gibt.
#
#   scripts/nachweis-pruefen.sh              # gegen den laufenden Stapel
#   scripts/nachweis-pruefen.sh --dokumente  # zusaetzlich die Signaturen
#
# Braucht `make up`.

set -uo pipefail
cd "$(dirname "$0")/.."

BOLD=$'\033[1m'; RED=$'\033[31m'; GREEN=$'\033[32m'; GELB=$'\033[33m'; AUS=$'\033[0m'

MIT_DOKUMENTEN=0
[[ "${1:-}" == "--dokumente" ]] && MIT_DOKUMENTEN=1

for werkzeug in curl python3; do
  command -v "$werkzeug" >/dev/null || {
    printf '%s✗%s %s fehlt.\n' "$RED" "$AUS" "$werkzeug" >&2; exit 1; }
done

if [[ -f .env ]]; then
  GEHEIMNIS=$(grep -E '^WORKERTRANSFER_NACHWEIS_SECRET=' .env | head -1 | cut -d= -f2-)
fi
GEHEIMNIS="${WORKERTRANSFER_NACHWEIS_SECRET:-${GEHEIMNIS:-}}"

if [[ -z "$GEHEIMNIS" ]]; then
  printf '%s✗%s WORKERTRANSFER_NACHWEIS_SECRET ist nicht gesetzt.\n' "$RED" "$AUS" >&2
  echo "   Ohne es antwortet jede Nachweisadresse mit 404 — richtig, aber" >&2
  echo "   dann ist hier nichts zu pruefen. \`make env\` wuerfelt es mit." >&2
  exit 1
fi

DIENSTE=(
  "identity:8001" "consent:8002" "profile:8003" "resume:8004"
  "portfolio:8005" "jobs:8006" "applications:8007" "companies:8008"
  "transfer:8009" "notification:8010" "github:8011" "scout:8012"
  "advisor:8013" "assessment:8014"
)

ARBEIT=$(mktemp -d)
trap 'rm -rf "$ARBEIT"' EXIT

printf '%sNachweis-Pruefung%s\n\n' "$BOLD" "$AUS"

ROT=0
GELESEN=0
BEFUNDE=0
STUMM=()

for eintrag in "${DIENSTE[@]}"; do
  name="${eintrag%%:*}"
  hafen="${eintrag##*:}"

  if ! curl -fsS --max-time 15 \
       -H "X-Noelia-Operator: ${GEHEIMNIS}" \
       "http://localhost:${hafen}/noelia/report.json" \
       -o "${ARBEIT}/${name}.json" 2>/dev/null; then
    STUMM+=("$name")
    printf '  %s✗%s %-14s antwortet nicht\n' "$RED" "$AUS" "$name"
    ROT=1
    continue
  fi

  GELESEN=$((GELESEN + 1))

  # Die Auswertung steht in python3 und nicht in einer grep-Kette: ein
  # Zustandswort in einer Zusammenfassung wuerde sonst als Zustand gezaehlt,
  # und die Zahl waere still falsch. Eine falsche Zahl ist schlimmer als keine.
  zeile=$(python3 - "${ARBEIT}/${name}.json" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as f:
    bericht = json.load(f)
# Noelias Operator-Bericht, Schema 2: die Pruefungen liegen unter
# securityChecks.results, und ihr Zustand heisst Pass/Warning/Fail/NotApplicable.
befunde = bericht["securityChecks"]["results"]
zaehl = {}
for b in befunde:
    zaehl[b["status"]] = zaehl.get(b["status"], 0) + 1
fehlt = [b for b in befunde if b["status"] == "Fail"]
print(len(befunde))
print(zaehl.get("Pass", 0), zaehl.get("Warning", 0),
      zaehl.get("Fail", 0), zaehl.get("NotApplicable", 0))
for b in fehlt:
    print(f"{b['id']}\t{b['summary']}")
PY
)

  anzahl=$(sed -n '1p' <<<"$zeile")
  staende=$(sed -n '2p' <<<"$zeile")
  read -r gut hinweis fehlt nichtan <<<"$staende"

  BEFUNDE=$((BEFUNDE + anzahl))

  if [[ "$anzahl" -eq 0 ]]; then
    # Ein Dienst, der antwortet und NICHTS berichtet, sieht von aussen aus wie
    # einer, bei dem alles in Ordnung ist. Das ist die Falle, gegen die
    # `scripts/test-dotnet.sh` seine Zahl auf den Schirm schreibt.
    printf '  %s✗%s %-14s antwortet, berichtet aber keinen einzigen Befund\n' \
      "$RED" "$AUS" "$name"
    ROT=1
    continue
  fi

  if [[ "$fehlt" -gt 0 ]]; then
    printf '  %s✗%s %-14s %s Befunde: %s gut, %s Hinweis, %s%s NICHT EINGELOEST%s, %s n/a\n' \
      "$RED" "$AUS" "$name" "$anzahl" "$gut" "$hinweis" "$RED" "$fehlt" "$AUS" "$nichtan"
    sed -n '3,$p' <<<"$zeile" | while IFS=$'\t' read -r id text; do
      printf '      %s%s%s  %s\n' "$BOLD" "$id" "$AUS" "$text"
    done
    ROT=1
  else
    printf '  %s✓%s %-14s %s Befunde: %s gut, %s Hinweis, %s n/a\n' \
      "$GREEN" "$AUS" "$name" "$anzahl" "$gut" "$hinweis" "$nichtan"
  fi
done

# ---------------------------------------------------------------------------
# Die Signaturen, auf Wunsch.
# ---------------------------------------------------------------------------
if [[ $MIT_DOKUMENTEN -eq 1 ]]; then
  printf '\n%sDie Dokumente%s\n' "$BOLD" "$AUS"
  command -v openssl >/dev/null || {
    printf '  %s✗%s openssl fehlt.\n' "$RED" "$AUS"; ROT=1; }

  for dokument in datenschutz ki mitbestimmung; do
    datei="nachweis/${dokument}.json"
    signatur="nachweis/${dokument}.sig.json"

    if [[ ! -f "$datei" || ! -f "$signatur" ]]; then
      printf '  %s!%s %-14s nicht vorhanden — `make nachweis` zuerst\n' \
        "$GELB" "$AUS" "$dokument"
      continue
    fi

    python3 -c "
import json, sys
with open(sys.argv[1], encoding='utf-8') as f:
    d = json.load(f)
sys.stdout.write(json.dumps(d, sort_keys=True, separators=(',', ':'), ensure_ascii=False))
" "$datei" > "${ARBEIT}/${dokument}.kanonisch"

    python3 -c "
import json, sys
with open(sys.argv[1], encoding='utf-8') as f:
    print(json.load(f)['public_key_pem_base64'])
" "$signatur" | openssl base64 -d -A > "${ARBEIT}/${dokument}.pub"

    python3 -c "
import json, sys
with open(sys.argv[1], encoding='utf-8') as f:
    print(json.load(f)['signature'])
" "$signatur" | openssl base64 -d -A > "${ARBEIT}/${dokument}.bin"

    if openssl dgst -sha256 -verify "${ARBEIT}/${dokument}.pub" \
         -signature "${ARBEIT}/${dokument}.bin" \
         "${ARBEIT}/${dokument}.kanonisch" >/dev/null 2>&1; then
      printf '  %s✓%s %-14s Signatur stimmt\n' "$GREEN" "$AUS" "$dokument"
    else
      printf '  %s✗%s %-14s Signatur stimmt NICHT\n' "$RED" "$AUS" "$dokument"
      ROT=1
    fi

    # DIE WORTLISTE, AM ERZEUGTEN DOKUMENT.
    #
    # Ein Test ueber die Quelle des Satzes ist gut und laeuft ueberall
    # (`DokumentwortTests`); dieser hier prueft, was WIRKLICH herauskam — samt
    # der Vorbehalte, die aus der Lesung abgeleitet werden und deshalb in
    # keiner Konstante stehen. Ein Befund, dessen Zusammenfassung eines Tages
    # „konform" sagt, kaeme sonst durch.
    wortfund=$(python3 scripts/nachweis_worte.py "$datei")

    if [[ -n "$wortfund" ]]; then
      printf '  %s✗%s %-14s Konformitaetswort im Geltungssatz oder Vorbehalt:\n' \
        "$RED" "$AUS" "$dokument"
      sed 's/^/      /' <<<"$wortfund"
      ROT=1
    fi
  done
fi

# ---------------------------------------------------------------------------
# Die Zahl auf dem Schirm.
#
# Sie steht hier aus demselben Grund, aus dem `scripts/test-dotnet.sh` seine
# Zahl schreibt: ein gruener Lauf ueber nichts sieht genauso aus wie einer
# ueber vierzehn Dienste.
# ---------------------------------------------------------------------------
printf '\n%s── Stand ─────────────────────────────────────────%s\n' "$BOLD" "$AUS"
printf '  %s von %s Diensten gelesen, %s Befunde gefahren.\n' \
  "$GELESEN" "${#DIENSTE[@]}" "$BEFUNDE"

if [[ ${#STUMM[@]} -gt 0 ]]; then
  printf '  %s!%s Stumm geblieben: %s\n' "$GELB" "$AUS" "${STUMM[*]}"
  echo "    Laeuft der Stapel? \`make up\`. Ein stummer Dienst ist hier ROT und"
  echo "    kein Hinweis: ueber ihn sagt der Nachweis nichts, und das ist"
  echo "    genau der Zustand, den niemand bemerkt."
fi

if [[ $ROT -eq 0 ]]; then
  printf '\n%s✓ Keine Zusage unbelegt.%s Was offen ist, steht als Hinweis da.\n' \
    "$GREEN" "$AUS"
else
  printf '\n%s✗ Mindestens eine Zusage ist nicht eingeloest.%s\n' "$RED" "$AUS"
fi

exit $ROT
