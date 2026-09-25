// Runtime configuration of the UI.
//
// This version is DELIBERATELY empty and changes nothing: `src/env.ts` then
// falls back to the VITE_ variables and finally to the port fallback — exactly
// the behaviour `pnpm dev` and docker compose always had.
//
// In the Kubernetes cluster the Helm chart lays a real version over this file
// (written by deploy/helm/workertransfer/templates/configmap-web.yaml, mounted
// by web.yaml). The reason stands there and in ADR-0028, in short: `vite build`
// bakes the VITE_ variables into the bundle, so a built image would carry its
// URLs baked in and could not be the same in staging and production.
//
// It lives in public/ and not in src/ because it must NOT be bundled: it has
// to land as a file of its own in the served directory so that it can be
// replaced there. `__WT_CONFIG__` and the keys inside it are that contract.
window.__WT_CONFIG__ = {};
