---
name: wt-k8s
description: Use when working on WorkerTransfer's Kubernetes staging environment — `make k8s-up`, `make k8s-down`, `make k8s-lint`, the Helm chart under `deploy/`, kind, port clashes with compose, `replicaCount`, the route map travelling in the image, or the web image's runtime config (`window.__WT_CONFIG__`).
---

# Kubernetes — staging on your own machine

Moved verbatim from `CLAUDE.md` (30.10.2026). The binding one-liner stays there.

### Kubernetes — a staging environment on your own machine

```bash
make k8s-up      # kind cluster + both images + helm release + PROOF
make k8s-down    # delete the cluster and its data
make k8s-lint    # helm lint + render, no cluster needed
```

**The two environments cannot run at the same time, and that was measured rather than reasoned.** The gateway answers on **`http://localhost:8090`** here *and* in compose, the UI on `:5173` in both, Mailpit on `:8025` in both — the comment in `docker-compose.yml` claiming 8090 avoids a port fight was describing 8080, and the fight it warns about is the one it creates. `scripts/k8s-up.sh` checks all three before it builds anything and names them; without that, kind reports an unready node minutes later and far from the cause. **Run `make down` first.** Sharing the ports on purpose is the better trade: one address for the UI and one for the API, whichever environment is up.

Load-bearing, and easy to undo by tidying up (ADR-0028):

- **The route map stays one file.** The k8s `Service` objects are named exactly like the compose services, so `ocelot.json` travels in the image and the same map serves both. Re-expressing those non-disjoint paths and their priorities as Ingress rules would duplicate the map, and the copy would be wrong at the first new path.
- **`replicaCount: 1` is a decision, not a starting value.** For the fourteen services one reason remains: **every service migrates its schema at startup**, so two pods are two pilgrims on one schema. (The outbox dispatcher's missing `SKIP LOCKED` is fixed.) The **gateway** is pinned separately, and for its own reason: the auth brake counts in-process — see below.
- **The web image is a built artifact, and that forces a runtime config.** `vite build` bakes `import.meta.env.VITE_*` into the bundle, so an image with baked URLs cannot be the same in two environments. `web/src/env.ts` resolves in three steps — `window.__WT_CONFIG__` → `VITE_*` → port fallback — and `web/public/config.js` is an empty object that changes nothing locally. Only the chart lays a real one over it. Do not "simplify" that back to a build arg.

**`make k8s-up` has never been run on this machine.** The chart lints and renders; only a run proves it works.

The auth brake mentioned above ("see below") is in the `wt-gateway` skill.
