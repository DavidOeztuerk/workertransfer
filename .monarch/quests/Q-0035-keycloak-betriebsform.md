---
id: Q-0035
gate: G-0011
kind: main
rank: A
severity: low
risk_tags: [auth, infra]
state: proposed
findings: [D2, I2-Session-Entscheidung]
scope:
  repo: workertransfer
  include: ["scripts/keycloak.sh", "scripts/control-plane.sh", "docs/**", "deploy/**"]
  exclude: []
estimate: { session_pct: 18, weekly_pct: 2.5, gold_usd: 0 }
dod:
  - cert.key sofort 0600 (heute 0644, git-ignoriert)
  - Betriebsprofil Keycloak - start (nicht start-dev), Hostname, TLS-Kette, Reverse-Proxy, Realm-Export und Backup, Admin-Härtung
  - Redirect-URIs ausschließlich https in Nicht-Dev
  - Entscheidung zum Hosting (EU, selbst betrieben) als ADR – verknüpft mit G-0005
boss: "keycloak.sh --profile prod startet ohne start-dev, Discovery per https mit gültiger Kette, CP-Login E2E grün"
evidence: ["WT scripts/keycloak.sh:75-76,114,118,129,162-163", "07-warden-controlplane.md D2", "02a / cp-session-hits.tsv: 2026-10-06T21:33:01Z (I2), 2026-10-20T13:04:15Z (PR #102)"]
board: { provider: github, issue: 127 }
---
# Keycloak-Betriebsform

## Objective
Die Betreiber-Anmeldung der CP läuft im Betrieb auf einem gehärteten, selbst betriebenen Keycloak – so wie in der Session als „vor dem Start“ festgehalten.

## Definition of Done
- [ ] Sofortmaßnahme cert.key 0600 (einzeln freigeben)
- [ ] Design → Review → Freigabe (auth/infra, Rank A)
- [ ] Boss: siehe Frontmatter
