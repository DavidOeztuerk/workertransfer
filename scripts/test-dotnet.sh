#!/usr/bin/env bash
# The test suites, ONE AT A TIME and in order.
#
# Not `dotnet test` over the solution: fourteen suites then start fourteen
# Postgres containers at once, Testcontainers' ResourceReaper runs into a
# timeout, and ALL suites fail within a millisecond with
# `TypeInitializationException`. Measured, not assumed — and it looks like a
# broken build while it is only too much at once.
#
# Runs THROUGH instead of stopping at the first red, and names the count at the
# end: a green run with twenty skipped tests is no green run, and a run that
# never collected half the suites looks exactly the same.
set -uo pipefail

cd "$(dirname "$0")/.."

# THE LANGUAGE IS PINNED HERE, BECAUSE THIS SCRIPT READS ITS OWN OUTPUT.
#
# `dotnet test` answers in the language of the environment: German on a German
# machine ("Bestanden! … erfolgreich: 925"), English on the runner ("Passed! …
# Passed: 925"). The script used to grab the German words — green locally,
# while in CI all sixteen suites reported "did not run at all" although they
# ran and were green. Measured 09.09.2026: Identity took two minutes and still
# counted as not run.
#
# It is the same pin `dependency-audit` in CI already sets. It stands here in
# the script and not in the workflow, because the script reads the output:
# whoever reads it fixes its language — otherwise the result depends on who
# happens to call.
export DOTNET_CLI_UI_LANGUAGE=en

suites=(DocumentStore Outbox Skills CrossCutting Gateway Identity Consent Profile Resume Portfolio
        Jobs Applications Companies Transfer GitHub Notification Scout Advisor Assessment)

red=0
total=0
skipped=0

for suite in "${suites[@]}"; do
  project="tests/WorkerTransfer.${suite}.Tests/WorkerTransfer.${suite}.Tests.csproj"
  [ -f "$project" ] || continue

  output=$(dotnet test "$project" --no-build 2>&1)
  line=$(printf '%s\n' "$output" | grep -E "^(Passed!|Failed!)" | tail -1)

  if [ -z "$line" ]; then
    printf '  \033[31m%-14s no output — the suite did not run at all\033[0m\n' "$suite"
    red=$((red + 1))
    continue
  fi

  passed=$(printf '%s' "$line" | grep -oE 'Passed: *[0-9]+' | grep -oE '[0-9]+')
  skip=$(printf '%s' "$line" | grep -oE 'Skipped: *[0-9]+' | grep -oE '[0-9]+')

  # A summary no number can be read from is no green run — it is an unknown.
  # Without this branch `$((total + ))` would abort or pass silently as 0.
  if [ -z "$passed" ] || [ -z "$skip" ]; then
    printf '  \033[31m%-14s unreadable summary: %s\033[0m\n' "$suite" "$line"
    red=$((red + 1))
    continue
  fi

  total=$((total + passed))
  skipped=$((skipped + skip))

  if printf '%s' "$line" | grep -q '^Passed!'; then
    printf '  \033[32m%-14s %s green\033[0m\n' "$suite" "$passed"
  else
    printf '  \033[31m%-14s %s\033[0m\n' "$suite" "$line"
    # The NAMES of what fell, not only the count. Measured 24.09.2026: a full
    # run reported "Failed: 1" for Transfer after the machine had been
    # suspended mid-run, three reruns were green, and which test had fallen
    # could no longer be found out — the summary line was all this script kept.
    printf '%s\n' "$output" | grep -E '^\s+Failed ' | sed 's/^/                 /'
    red=$((red + 1))
  fi
done

echo
if [ "$skipped" -gt 0 ]; then
  printf '\033[33m%s skipped — that is no green run.\033[0m\n' "$skipped"
fi

if [ "$red" -gt 0 ]; then
  printf '\033[31m%s suites red.\033[0m\n' "$red"
  exit 1
fi

printf '\033[32m%s tests green, 0 red, %s skipped.\033[0m\n' "$total" "$skipped"
