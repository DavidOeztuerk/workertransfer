#!/usr/bin/env bash
# The cluster is deleted, data included. That is right here: a local staging
# environment exists to be thrown away, and a half-dismantled cluster with old
# PVCs produces errors at the next start that look like code errors.
#
# Remove only the release and keep the cluster: `helm uninstall workertransfer`.
#
# Only PAUSE instead of deleting: `docker stop workertransfer-control-plane`, back
# with `docker start`. That is the right way before `make test` (Testcontainers
# and the cluster do not get along). But careful with the expectation: after
# the start `kubectl` answers within seconds, yet until all fifteen pods are
# READY one to two minutes pass. Meanwhile the gateway answers 502 — that
# recovers on its own and is no reason to roll out again.
set -euo pipefail

CLUSTER=workertransfer

if ! kind get clusters 2>/dev/null | grep -qx "$CLUSTER"; then
  echo "kind cluster '$CLUSTER' does not exist — nothing to do."
  exit 0
fi

if kind delete cluster --name "$CLUSTER"; then
  exit 0
fi

# A second attempt, and for a reason.
#
# When clearing away a busy node Docker occasionally reports "tried to kill
# container, but did not receive an exit event" and returns an error — the
# container is `Exited` afterwards all the same. Only the acknowledgement was
# missing. A `make k8s-down` that goes red over that leaves a corpse behind, and
# the next `make k8s-up` fails on a name that is already taken.
echo "Deleting failed — the node has usually ended anyway. Second attempt." >&2
docker rm -f -v "${CLUSTER}-control-plane" 2>/dev/null || true
kind delete cluster --name "$CLUSTER"
