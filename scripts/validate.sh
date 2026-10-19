#!/usr/bin/env bash
# One command that checks everything and says at the end where we stand.
#
# Why this exists: `make check` stops at the first failure and then says
# nothing more about the rest. For the question "what is the state?" that is
# the wrong form — one wants to see all red places at once, not the first. So
# here every step runs to its end and the exit code comes from the summary.
#
# And why it counts skips: a green run in which twenty tests skipped themselves
# is no green run (ADR-0011). Without Docker the integration and E2E suites
# skip themselves; the script then says what stayed unchecked instead of
# keeping quiet about it.
#
#   scripts/validate.sh          # everything that works without a running stack
#   scripts/validate.sh --e2e    # the Playwright journeys as well

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
    # Show at once what went wrong — the run goes on anyway.
    tail -n 25 "$log" | sed 's/^/    /'
  fi
}

# --- .NET --------------------------------------------------------------------
# Build and test in SEPARATE steps. Chained, the Testcontainers suites fail and
# look exactly like real test failures.
step "dotnet build" dotnet build WorkerTransfer.slnx
step "dotnet test" ./scripts/test-dotnet.sh

# --- Frontend ----------------------------------------------------------------
# `cd web` and not `pnpm -r`: since the root is no package any more there is no
# workspace `-r` could run over — the call ended with ERR_PNPM_NO_PKG_MANIFEST,
# and with it all three frontend steps were dead, of all places in the tool
# that is meant to report every red step.
#
# `pnpm build` belongs here: tsc and Vitest both do NOT run through the build
# path, and an error that only shows up when bundling otherwise shows up only
# in the image.
step "tsc" sh -c "cd web && pnpm check"
step "vitest" sh -c "cd web && pnpm test"
step "vite build" sh -c "cd web && pnpm build"

# --- Noelia's own view of the composition ------------------------------------
# Static, needs no stack, and finds exactly the class of defect this repository
# describes itself in `ServiceFoundation.cs`: an `AddX(...)` BESIDE
# `AddNoelia(...)` instead of `noelia.UseX(...)` inside it. The built-in modules
# register their own provider during `Build()`, the earlier registration is
# overwritten — and nothing says so.
#
# Exit 0 clean, 1 findings, 2 the tool itself broken. Without the tool the step
# is NOT skipped silently but named below.
if command -v noelia >/dev/null 2>&1 || [[ -x "$HOME/.dotnet/tools/noelia" ]]; then
  NOELIA_RAN=1
  step "noelia analyze" env PATH="$PATH:$HOME/.dotnet/tools" noelia analyze .
else
  NOELIA_RAN=0
fi

# --- The evidence (needs the running stack) -----------------------------------
# "A gate one has to call in order to have it, one does not have." That is why
# it stands here and not only in the Makefile.
#
# It needs the stack because it measures EXACTLY what no test can measure: what
# this container really does tonight — which providers are registered, which
# doors stand open, where it talks to. If the stack does not run, that is
# reported BY NAME below and not kept quiet; a tick over a run that did not
# happen is the kind of green this script is built against.
EVIDENCE_RAN=0
if curl -sf --max-time 2 http://localhost:8003/health/live >/dev/null 2>&1; then
  EVIDENCE_RAN=1
  step "evidence" ./scripts/evidence-check.sh
fi

# --- E2E (only on request, needs the running stack) ---------------------------
if [[ $WITH_E2E -eq 1 ]]; then
  step "playwright" sh -c "cd web && pnpm e2e"
fi

# --- What stayed unchecked? ---------------------------------------------------
# Skips are no successes. This number is the most honest figure in the report.
# The file name comes from `step`: it replaces every non-word character with
# `_`, so "dotnet test" becomes `dotnet_test.log`. Here stood
# `dotnet-test.log`, with a HYPHEN — the file never existed, `grep` found
# nothing, and both numbers hanging on it dropped out silently: the skipped
# stayed 0 for ever and the test count stayed `?`. Exactly the defect this
# report is built against — a green tick with no number beside it. Once more a
# place where two sides spelled the same thing differently.
#
# And the same defect a second time, found in the English rename: the patterns
# below read `test-dotnet.sh`'s summary, and they were still German
# ("uebersprungen", "Tests gruen") after that script had started to answer in
# English ("skipped", "tests green"). Whoever changes the wording there changes
# it here.
dotnet_log="$log_dir/dotnet_test.log"
playwright_log="$log_dir/playwright.log"
skipped=0
if [[ -f "$dotnet_log" ]]; then
  skipped=$(grep -oE '[0-9]+ skipped' "$dotnet_log" | tail -1 | grep -oE '[0-9]+' || true)
  skipped=${skipped:-0}
fi

docker_up=0
curl -sf --max-time 2 http://localhost:8003/health/live >/dev/null 2>&1 && docker_up=1

# How much really ran? A tick with no number beside it does not answer the
# question, and that is the interesting one here: "green" over a run that did
# nothing looks identical to "green" over 900 tests. The warnings further down
# only fire when something was SKIPPED — a run that silently collects only half
# would get through. So the number now always stands there, not only in the
# exceptional case.
#
# Three subtleties that made a naive version count wrong — and a wrong number
# is worse than none, because one believes it:
#   1. The logs are coloured; the ANSI sequences stand BETWEEN word and number
#      and tear every pattern apart. So strip the colour first.
#   2. vitest reports two lines with "passed" — "Test Files 41 passed" and
#      "Tests 336 passed". Without the more precise pattern the file count
#      would come out.
#   3. Summed instead of "the last line": `pnpm -r` used to run over two
#      packages and delivered two "Tests" lines. Today it is one, but the sum
#      stays right — and stays right when another one is added.
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
dotnet_passed=$(count_from "$dotnet_log" '[0-9]+ tests green')
vitest_passed=$(count_from "$vitest_log" 'Tests +[0-9]+ passed' sum)
e2e_ran=$(count_from "$playwright_log" '[0-9]+ passed')
# The number on the screen here too: a green evidence check over fourteen
# services otherwise looks exactly like one over none. The pattern reads the
# summary line of `scripts/evidence-check.sh`.
evidence_findings=$(count_from "$log_dir/evidence.log" '[0-9]+ findings run')

printf '\n%s── State ─────────────────────────────────────────%s\n' "$BOLD" "$OFF"
for entry in "${RESULTS[@]}"; do
  IFS='|' read -r status name log <<<"$entry"
  case "$name" in
    "dotnet test") tally="${dotnet_passed:-?} passed" ;;
    evidence) tally="${evidence_findings:-?} findings" ;;
    vitest) tally="${vitest_passed:-?} passed" ;;
    playwright) tally="${e2e_ran:-?} journeys" ;;
    *) tally="" ;;
  esac
  if [[ "$status" == "ok" ]]; then
    printf '  %s✓%s %s %s%s%s\n' "$GREEN" "$OFF" "$name" "$BOLD" "$tally" "$OFF"
  else
    printf '  %s✗%s %s  %s(%s)%s\n' "$RED" "$OFF" "$name" "$YELLOW" "$log" "$OFF"
  fi
done

# Retried tests are no successes. A flaky test that turns green on the second
# attempt is an open question — and otherwise disappears without a sound.
flaky=0
if [[ -n "${playwright_log:-}" && -f "$playwright_log" ]]; then
  flaky=$(grep -oE '[0-9]+ flaky' "$playwright_log" | tail -1 | grep -oE '[0-9]+' || true)
  flaky=${flaky:-0}
fi

if [[ "$flaky" -gt 0 ]]; then
  printf '\n  %s!%s %s E2E test(s) green only on the second attempt.' "$YELLOW" "$OFF" "$flaky"
  printf '\n    That does not count as passed — look into what caused it.\n'
fi

if [[ "$skipped" -gt 0 ]]; then
  printf '\n  %s!%s %s .NET tests skipped.' "$YELLOW" "$OFF" "$skipped"
  if [[ $docker_up -eq 0 ]]; then
    printf ' The stack is not running — with "docker compose up -d"\n    the integration tests really run instead of skipping themselves.\n'
  else
    printf '\n'
  fi
fi

# Skipped E2E journeys are NO success — and until 05.08.2026 they were
# invisible here. The suite skipped itself completely (16 of 16), because two
# services no longer started after a new dependency, and this script reported
# "All green" all the same. A report that does not show a hole is worse than
# none: it creates trust that is not covered.
#
# That is why a completely skipped run is RED now and not just a warning. A
# single skipped test is a decision; all of them are an outage.
e2e_skipped=0
if [[ $WITH_E2E -eq 1 && -f "$playwright_log" ]]; then
  e2e_skipped=$(grep -oE '[0-9]+ skipped' "$playwright_log" | tail -1 | grep -oE '[0-9]+' || true)
  e2e_skipped=${e2e_skipped:-0}
fi

if [[ "$e2e_skipped" -gt 0 ]]; then
  e2e_passed=$(grep -oE '[0-9]+ passed' "$playwright_log" | tail -1 | grep -oE '[0-9]+' || true)
  e2e_passed=${e2e_passed:-0}
  printf '\n  %s!%s %s E2E journey(s) skipped, %s ran.' \
    "$YELLOW" "$OFF" "$e2e_skipped" "$e2e_passed"
  if [[ "$e2e_passed" -eq 0 ]]; then
    printf '\n    %sNOT A SINGLE journey ran.%s Usually a service does not answer:\n' "$RED" "$OFF"
    printf '    "docker compose ps" and then "docker compose logs <service>".\n'
    printf '    After a NEW dependency a restart is not enough — the image has to\n'
    printf '    be rebuilt ("docker compose up -d --build <service>").\n'
    FAILED=1
  else
    printf '\n'
  fi
fi

if [[ ${NOELIA_RAN:-0} -eq 0 ]]; then
  printf '  %s!%s `noelia analyze` did NOT run — the tool is missing.\n    "dotnet tool install -g Noelia.Cli" fetches it.\n' "$YELLOW" "$OFF"
fi

if [[ $EVIDENCE_RAN -eq 0 ]]; then
  printf '  %s!%s The evidence check did NOT run (no stack). It is the only level\n    at which it is checked what this container really does —\n    which providers are registered and which doors stand open.\n    With "docker compose up -d" it runs along.\n' "$YELLOW" "$OFF"
fi

if [[ $WITH_E2E -eq 0 ]]; then
  if [[ $docker_up -eq 1 ]]; then
    printf '  %s!%s The stack is running — "scripts/validate.sh --e2e" checks the journey\n    through the browser as well.\n' "$YELLOW" "$OFF"
  else
    printf '  %s!%s E2E did not run (no stack). That is the only level at which the\n    UI is checked against real services.\n' "$YELLOW" "$OFF"
  fi
fi

if [[ $FAILED -eq 0 ]]; then
  printf '\n%s✓ All green.%s\n' "$GREEN" "$OFF"
else
  printf '\n%s✗ At least one step is red.%s The output stands above; complete\n  in the named log files (until this terminal ends).\n' "$RED" "$OFF"
fi
exit $FAILED
