#!/usr/bin/env bash
# Der Cluster wird gelöscht, samt Daten. Das ist hier richtig: eine lokale
# Staging-Umgebung ist zum Wegwerfen da, und ein halb abgebauter Cluster mit
# alten PVCs erzeugt beim nächsten Hochfahren Fehler, die wie Codefehler
# aussehen.
#
# Nur das Release entfernen und den Cluster behalten: `helm uninstall workertransfer`.
#
# Nur PAUSIEREN statt löschen: `docker stop workertransfer-control-plane`, zurück
# mit `docker start`. Das ist der richtige Weg vor `uv run pytest` (Testcontainers
# und der Cluster vertragen sich nicht). Aber Vorsicht mit der Erwartung: nach dem
# Start antwortet `kubectl` schon nach Sekunden, bis alle fünfzehn Pods BEREIT
# sind vergehen jedoch ein bis zwei Minuten. Solange gibt das Gateway 502 —
# das erholt sich von allein und ist kein Grund, neu auszurollen.
set -euo pipefail

CLUSTER=workertransfer

if ! kind get clusters 2>/dev/null | grep -qx "$CLUSTER"; then
  echo "kind-Cluster '$CLUSTER' existiert nicht — nichts zu tun."
  exit 0
fi

if kind delete cluster --name "$CLUSTER"; then
  exit 0
fi

# Zweiter Versuch, und zwar mit Grund.
#
# Docker meldet beim Abräumen eines beschäftigten Knotens gelegentlich
# "tried to kill container, but did not receive an exit event" und gibt einen
# Fehler zurück — der Container ist danach trotzdem `Exited`. Nur die
# Rückmeldung fehlte. Ein `make k8s-down`, das darüber rot wird, lässt eine
# Leiche stehen, und der nächste `make k8s-up` scheitert an einem Namen, der
# schon vergeben ist.
echo "Löschen fehlgeschlagen — der Knoten ist meist trotzdem beendet. Zweiter Versuch." >&2
docker rm -f -v "${CLUSTER}-control-plane" 2>/dev/null || true
kind delete cluster --name "$CLUSTER"
