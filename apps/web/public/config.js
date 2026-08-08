// Laufzeitkonfiguration der Oberfläche.
//
// Diese Fassung ist ABSICHTLICH leer und ändert nichts: `src/env.ts` fällt dann
// auf die VITE_-Variablen und zuletzt auf den Port-Rückfall zurück — also genau
// auf das Verhalten, das `pnpm dev` und docker compose schon immer hatten.
//
// Im Kubernetes-Cluster legt das Helm-Chart eine echte Fassung über diese Datei
// (deploy/helm/workertransfer/templates/web.yaml). Der Grund steht dort und in
// ADR-0028, kurz: `vite build` backt die VITE_-Variablen in das Bündel ein, ein
// gebautes Image trüge seine URLs also fest in sich und könnte in Staging und
// Produktion nicht dasselbe sein.
//
// Sie liegt in public/ und nicht in src/, weil sie NICHT gebündelt werden darf:
// sie muss als eigene Datei im Auslieferungsverzeichnis landen, damit man sie
// dort ersetzen kann.
window.__WT_CONFIG__ = {};
