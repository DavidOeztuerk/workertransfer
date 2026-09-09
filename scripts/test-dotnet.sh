#!/usr/bin/env bash
# Die Testreihen, EINZELN und der Reihe nach.
#
# Nicht `dotnet test` ueber die Projektmappe: vierzehn Reihen starten dann
# vierzehn Postgres-Behaelter gleichzeitig, der ResourceReaper von
# Testcontainers laeuft in eine Zeitueberschreitung, und ALLE Reihen fallen
# binnen einer Millisekunde mit `TypeInitializationException`. Gemessen, nicht
# vermutet — und es sieht aus wie ein kaputter Bau, ist aber nur zu viel auf
# einmal.
#
# Laeuft DURCH statt beim ersten Rot zu enden, und nennt am Schluss die Zahl:
# ein gruener Lauf mit zwanzig uebersprungenen Tests ist kein gruener Lauf, und
# ein Lauf, der die Haelfte gar nicht eingesammelt hat, sieht genauso aus.
set -uo pipefail

cd "$(dirname "$0")/.."

# DIE SPRACHE WIRD HIER FESTGENAGELT, WEIL DIESES SKRIPT SEINE EIGENE AUSGABE
# LIEST.
#
# `dotnet test` antwortet in der Sprache der Umgebung: auf diesem Rechner
# deutsch („Bestanden! … erfolgreich: 925"), auf dem Laeufer englisch
# („Passed! … Passed: 925"). Das Skript griff nach den deutschen Woertern —
# lokal gruen, in der CI meldeten alle sechzehn Reihen „lief gar nicht",
# waehrend sie in Wahrheit liefen und gruen waren. Gemessen am 09.09.2026:
# Identity brauchte zwei Minuten und galt trotzdem als nicht gelaufen.
#
# Es ist derselbe Griff, den `dependency-audit` in der CI schon tut. Er steht
# hier im Skript und nicht im Workflow, weil das Skript die Ausgabe liest:
# wer sie liest, legt ihre Sprache fest — sonst haengt das Ergebnis daran, wer
# gerade aufruft.
export DOTNET_CLI_UI_LANGUAGE=en

reihen=(Ablage Outbox Skills Ganzes Gateway Identity Consent Profile Resume Portfolio
        Jobs Applications Companies Transfer GitHub Notification)

rot=0
gesamt=0
uebersprungen=0

for reihe in "${reihen[@]}"; do
  projekt="tests/WorkerTransfer.${reihe}.Tests/WorkerTransfer.${reihe}.Tests.csproj"
  [ -f "$projekt" ] || continue

  zeile=$(dotnet test "$projekt" --no-build 2>&1 | grep -E "^(Passed!|Failed!)" | tail -1)

  if [ -z "$zeile" ]; then
    printf '  \033[31m%-14s keine Ausgabe — die Reihe lief gar nicht\033[0m\n' "$reihe"
    rot=$((rot + 1))
    continue
  fi

  erfolg=$(printf '%s' "$zeile" | grep -oE 'Passed: *[0-9]+' | grep -oE '[0-9]+')
  sprung=$(printf '%s' "$zeile" | grep -oE 'Skipped: *[0-9]+' | grep -oE '[0-9]+')

  # Eine Zusammenfassung, aus der keine Zahl zu lesen ist, ist kein gruener
  # Lauf — sie ist eine unbekannte. Ohne diesen Zweig wuerde `$((gesamt + ))`
  # abbrechen oder still als 0 durchgehen.
  if [ -z "$erfolg" ] || [ -z "$sprung" ]; then
    printf '  \033[31m%-14s Zusammenfassung unlesbar: %s\033[0m\n' "$reihe" "$zeile"
    rot=$((rot + 1))
    continue
  fi

  gesamt=$((gesamt + erfolg))
  uebersprungen=$((uebersprungen + sprung))

  if printf '%s' "$zeile" | grep -q '^Passed!'; then
    printf '  \033[32m%-14s %s gruen\033[0m\n' "$reihe" "$erfolg"
  else
    printf '  \033[31m%-14s %s\033[0m\n' "$reihe" "$zeile"
    rot=$((rot + 1))
  fi
done

echo
if [ "$uebersprungen" -gt 0 ]; then
  printf '\033[33m%s uebersprungen — das ist kein gruener Lauf.\033[0m\n' "$uebersprungen"
fi

if [ "$rot" -gt 0 ]; then
  printf '\033[31m%s Reihen rot.\033[0m\n' "$rot"
  exit 1
fi

printf '\033[32m%s Tests gruen, 0 rot, %s uebersprungen.\033[0m\n' "$gesamt" "$uebersprungen"
