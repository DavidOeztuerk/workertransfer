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

reihen=(Ablage Outbox Skills Ganzes Gateway Identity Consent Profile Resume Portfolio
        Jobs Applications Companies Transfer GitHub Notification)

rot=0
gesamt=0
uebersprungen=0

for reihe in "${reihen[@]}"; do
  projekt="tests/WorkerTransfer.${reihe}.Tests/WorkerTransfer.${reihe}.Tests.csproj"
  [ -f "$projekt" ] || continue

  zeile=$(dotnet test "$projekt" --no-build 2>&1 | grep -E "^(Bestanden!|Fehler!)" | tail -1)

  if [ -z "$zeile" ]; then
    printf '  \033[31m%-14s keine Ausgabe — die Reihe lief gar nicht\033[0m\n' "$reihe"
    rot=$((rot + 1))
    continue
  fi

  erfolg=$(printf '%s' "$zeile" | grep -oE 'erfolgreich: *[0-9]+' | grep -oE '[0-9]+')
  sprung=$(printf '%s' "$zeile" | grep -oE 'übersprungen: *[0-9]+' | grep -oE '[0-9]+')
  gesamt=$((gesamt + erfolg))
  uebersprungen=$((uebersprungen + sprung))

  if printf '%s' "$zeile" | grep -q '^Bestanden!'; then
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
