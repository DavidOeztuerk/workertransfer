#!/usr/bin/env bash
# Der Cluster wird gelöscht, samt Daten. Das ist hier richtig: eine lokale
# Staging-Umgebung ist zum Wegwerfen da, und ein halb abgebauter Cluster mit
# alten PVCs erzeugt beim nächsten Hochfahren Fehler, die wie Codefehler
# aussehen.
#
# Nur das Release entfernen und den Cluster behalten: `helm uninstall workertransfer`.
set -euo pipefail

CLUSTER=workertransfer

if kind get clusters 2>/dev/null | grep -qx "$CLUSTER"; then
  kind delete cluster --name "$CLUSTER"
else
  echo "kind-Cluster '$CLUSTER' existiert nicht — nichts zu tun."
fi
