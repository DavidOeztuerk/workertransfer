#!/usr/bin/env sh
# Welcher der elf Dienste bin ich?
#
# `SERVICE_DIR` kommt aus der UMGEBUNG, nicht aus einem Bauargument: das Bild
# wird einmal gebaut und weiss beim Bauen nicht, wer es spaeter ist (ADR-0028).
#
# Die Wanderung steht NICHT hier, anders als zur Python-Zeit: `dotnet ef`
# braucht das SDK, und ein Laufzeitbild hat keins. Jeder Dienst wandert sein
# Schema beim Start selbst (`Wanderung.WandereAsync`) — dieselbe Zusage,
# nur eine Schicht tiefer.
set -eu

if [ -z "${SERVICE_DIR:-}" ]; then
  echo "entrypoint: SERVICE_DIR ist nicht gesetzt" >&2
  exit 1
fi

VERZEICHNIS="/app/${SERVICE_DIR}"

if [ ! -d "$VERZEICHNIS" ]; then
  echo "entrypoint: '${SERVICE_DIR}' gibt es in diesem Bild nicht" >&2
  echo "vorhanden: $(ls /app | tr '\n' ' ')" >&2
  exit 1
fi

# Der Einstiegspunkt ist die einzige Assembly mit einer eigenen
# runtimeconfig.json — die Bibliotheken daneben haben keine. Das erspart eine
# Tabelle von Dienstname auf DLL-Name, die bei jedem neuen Dienst zu pflegen
# waere und beim ersten Vergessen still das Falsche startet.
KONFIG="$(ls "${VERZEICHNIS}"/*.runtimeconfig.json 2>/dev/null | head -1)"

if [ -z "$KONFIG" ]; then
  echo "entrypoint: kein Einstiegspunkt in ${VERZEICHNIS}" >&2
  exit 1
fi

EINSTIEG="$(basename "$KONFIG" .runtimeconfig.json)"

# IN das Verzeichnis wechseln, nicht nur die DLL von dort starten: die
# Inhaltswurzel von ASP.NET ist das ARBEITSVERZEICHNIS. Aus /app heraus las
# jeder Dienst ein /app/appsettings.json, das es nicht gibt — und das Gateway
# starb an seiner fehlenden ocelot.json. Gemessen beim ersten `compose up`,
# nicht vermutet.
cd "$VERZEICHNIS"

echo "==> ${SERVICE_DIR}: dotnet ${EINSTIEG}.dll"
exec dotnet "${EINSTIEG}.dll" "$@"
