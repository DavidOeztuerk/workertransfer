#!/usr/bin/env bash
# Ein Befehl, der alles prüft und am Ende sagt, wo wir stehen.
#
# Warum das existiert: `make check` fällt beim ersten Fehler aus und sagt dann
# nichts mehr über den Rest. Für die Frage "wie ist der Stand?" ist das die
# falsche Form — man will alle roten Stellen auf einmal sehen, nicht die erste.
# Deshalb läuft hier jeder Schritt zu Ende und der Exit-Code kommt aus der
# Zusammenfassung.
#
# Und warum es Skips zählt: ein grüner Lauf, in dem sich zwanzig Tests
# übersprungen haben, ist kein grüner Lauf (ADR-0011). Ohne Docker
# überspringen sich die Integrations- und E2E-Suiten selbst; das Skript sagt
# dann, was ungeprüft blieb, statt es zu verschweigen.
#
#   scripts/validate.sh          # alles, was ohne laufenden Stack geht
#   scripts/validate.sh --e2e    # zusätzlich die Playwright-Reise

set -uo pipefail
cd "$(dirname "$0")/.."

WITH_E2E=0
[[ "${1:-}" == "--e2e" ]] && WITH_E2E=1

BOLD=$'\033[1m'; RED=$'\033[31m'; GREEN=$'\033[32m'; YELLOW=$'\033[33m'; OFF=$'\033[0m'
declare -a RESULTS=()
FAILED=0

log_dir="$(mktemp -d)"
trap 'rm -rf "$log_dir"' EXIT

step() {
  local name="$1"; shift
  printf '%s▸ %s%s\n' "$BOLD" "$name" "$OFF"
  local log="$log_dir/${name//[^a-zA-Z0-9]/_}.log"
  if "$@" >"$log" 2>&1; then
    RESULTS+=("ok|$name|")
  else
    RESULTS+=("fail|$name|$log")
    FAILED=1
    # Sofort zeigen, was schiefging — der Lauf geht trotzdem weiter.
    tail -n 25 "$log" | sed 's/^/    /'
  fi
}

# --- Python ------------------------------------------------------------------
step "ruff format" uv run ruff format --check .
step "ruff lint" uv run ruff check .
step "mypy" uv run mypy packages apps
step "pytest" uv run pytest -q

# --- Frontend ----------------------------------------------------------------
step "tsc" pnpm -r run check
step "vitest" pnpm -r run test

# --- E2E (nur auf Wunsch, braucht den laufenden Stack) ------------------------
if [[ $WITH_E2E -eq 1 ]]; then
  step "playwright" pnpm --filter @workertransfer/web run e2e
fi

# --- Was blieb ungeprüft? ----------------------------------------------------
# Skips sind keine Erfolge. Diese Zahl ist die ehrlichste Kennzahl im Bericht.
pytest_log="$log_dir/pytest.log"
playwright_log="$log_dir/playwright.log"
skipped=0
if [[ -f "$pytest_log" ]]; then
  skipped=$(grep -oE '[0-9]+ skipped' "$pytest_log" | tail -1 | grep -oE '[0-9]+' || true)
  skipped=${skipped:-0}
fi

docker_up=0
curl -sf --max-time 2 http://localhost:8003/health/live >/dev/null 2>&1 && docker_up=1

# Wie viel ist wirklich gelaufen? Ein Haken ohne Zahl daneben beantwortet die
# Frage nicht, und genau die ist hier die interessante: „grün" über einem Lauf,
# der nichts getan hat, sieht identisch aus wie „grün" über 900 Tests. Die
# Warnungen weiter unten schlagen erst an, wenn etwas ÜBERSPRUNGEN wurde — ein
# Lauf, der stillschweigend nur die Hälfte sammelt, käme durch. Deshalb steht
# die Zahl jetzt immer da, nicht nur im Ausnahmefall.
#
# Drei Feinheiten, die eine naive Fassung falsch zählen ließen — und eine
# falsche Zahl ist schlimmer als keine, weil man ihr glaubt:
#   1. Die Protokolle sind eingefärbt; die ANSI-Folgen stehen ZWISCHEN Wort und
#      Zahl und zerreißen jedes Muster. Deshalb erst entfärben.
#   2. vitest meldet zwei Zeilen mit „passed" — „Test Files 41 passed" und
#      „Tests 336 passed". Ohne das genauere Muster käme die Dateizahl heraus.
#   3. `pnpm -r` läuft über zwei Pakete, also gibt es zwei „Tests"-Zeilen. Die
#      letzte zu nehmen hieße, das zuletzt fertige Paket zu melden — hier wird
#      summiert.
count_from() {
  local log="$1" pattern="$2" mode="${3:-last}" nums
  [[ -f "$log" ]] || return 0
  nums=$(sed -E $'s/\x1b\\[[0-9;]*[a-zA-Z]//g' "$log" | grep -oE "$pattern" |
    grep -oE '[0-9]+' || true)
  [[ -n "$nums" ]] || return 0
  if [[ "$mode" == "sum" ]]; then
    awk '{s+=$1} END {if (NR) print s}' <<<"$nums"
  else
    tail -1 <<<"$nums"
  fi
}

vitest_log="$log_dir/vitest.log"
pytest_passed=$(count_from "$pytest_log" '[0-9]+ passed')
vitest_passed=$(count_from "$vitest_log" 'Tests +[0-9]+ passed' sum)
e2e_ran=$(count_from "$playwright_log" '[0-9]+ passed')

printf '\n%s── Stand ─────────────────────────────────────────%s\n' "$BOLD" "$OFF"
for entry in "${RESULTS[@]}"; do
  IFS='|' read -r status name log <<<"$entry"
  case "$name" in
    pytest) tally="${pytest_passed:-?} bestanden" ;;
    vitest) tally="${vitest_passed:-?} bestanden" ;;
    playwright) tally="${e2e_ran:-?} Reisen" ;;
    *) tally="" ;;
  esac
  if [[ "$status" == "ok" ]]; then
    printf '  %s✓%s %s %s%s%s\n' "$GREEN" "$OFF" "$name" "$BOLD" "$tally" "$OFF"
  else
    printf '  %s✗%s %s  %s(%s)%s\n' "$RED" "$OFF" "$name" "$YELLOW" "$log" "$OFF"
  fi
done

# Wiederholte Tests sind keine Erfolge. Ein Wackelkandidat, der beim zweiten
# Versuch grün wird, ist eine offene Frage — und verschwindet sonst lautlos.
flaky=0
if [[ -n "${playwright_log:-}" && -f "$playwright_log" ]]; then
  flaky=$(grep -oE '[0-9]+ flaky' "$playwright_log" | tail -1 | grep -oE '[0-9]+' || true)
  flaky=${flaky:-0}
fi

if [[ "$flaky" -gt 0 ]]; then
  printf '\n  %s!%s %s E2E-Test(s) erst im zweiten Anlauf grün.' "$YELLOW" "$OFF" "$flaky"
  printf '\n    Das zählt nicht als bestanden — nachsehen, woran es lag.\n'
fi

if [[ "$skipped" -gt 0 ]]; then
  printf '\n  %s!%s %s Python-Tests übersprungen.' "$YELLOW" "$OFF" "$skipped"
  if [[ $docker_up -eq 0 ]]; then
    printf ' Der Stack läuft nicht — mit "docker compose up -d"\n    laufen die Integrationstests wirklich statt sich zu überspringen.\n'
  else
    printf '\n'
  fi
fi

# Übersprungene E2E-Reisen sind KEIN Erfolg — und sie waren hier bis zum
# 05.08.2026 unsichtbar. Die Reihe übersprang sich vollständig (16 von 16), weil
# zwei Dienste nach einer neuen Abhängigkeit nicht mehr starteten, und dieses
# Skript meldete trotzdem „Alles grün". Ein Bericht, der ein Loch nicht zeigt,
# ist schlechter als keiner: er erzeugt Vertrauen, das nicht gedeckt ist.
#
# Deshalb ist ein vollständig übersprungener Lauf jetzt ROT und nicht nur ein
# Hinweis. Ein einzelner übersprungener Test ist eine Entscheidung; alle sind
# ein Ausfall.
e2e_skipped=0
if [[ $WITH_E2E -eq 1 && -f "$playwright_log" ]]; then
  e2e_skipped=$(grep -oE '[0-9]+ skipped' "$playwright_log" | tail -1 | grep -oE '[0-9]+' || true)
  e2e_skipped=${e2e_skipped:-0}
fi

if [[ "$e2e_skipped" -gt 0 ]]; then
  e2e_passed=$(grep -oE '[0-9]+ passed' "$playwright_log" | tail -1 | grep -oE '[0-9]+' || true)
  e2e_passed=${e2e_passed:-0}
  printf '\n  %s!%s %s E2E-Reise(n) übersprungen, %s gelaufen.' \
    "$YELLOW" "$OFF" "$e2e_skipped" "$e2e_passed"
  if [[ "$e2e_passed" -eq 0 ]]; then
    printf '\n    %sKEINE einzige Reise ist gelaufen.%s Meist antwortet ein Dienst nicht:\n' "$RED" "$OFF"
    printf '    "docker compose ps" und dann "docker compose logs <dienst>".\n'
    printf '    Nach einer NEUEN Abhängigkeit reicht ein Neustart nicht — das Image\n'
    printf '    muss neu gebaut werden ("docker compose up -d --build <dienst>").\n'
    FAILED=1
  else
    printf '\n'
  fi
fi

if [[ $WITH_E2E -eq 0 ]]; then
  if [[ $docker_up -eq 1 ]]; then
    printf '  %s!%s Der Stack läuft — "scripts/validate.sh --e2e" prüft auch die Reise\n    durch den Browser.\n' "$YELLOW" "$OFF"
  else
    printf '  %s!%s E2E nicht gelaufen (kein Stack). Das ist die einzige Ebene, auf der\n    die Oberfläche gegen echte Dienste geprüft wird.\n' "$YELLOW" "$OFF"
  fi
fi

if [[ $FAILED -eq 0 ]]; then
  printf '\n%s✓ Alles grün.%s\n' "$GREEN" "$OFF"
else
  printf '\n%s✗ Mindestens ein Schritt ist rot.%s Die Ausgaben stehen oben; vollständig\n  in den genannten Logdateien (bis dieses Terminal endet).\n' "$RED" "$OFF"
fi
exit $FAILED
