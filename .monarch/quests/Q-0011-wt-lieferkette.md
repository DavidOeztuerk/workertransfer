---
id: Q-0011
gate: G-0010
kind: daily
rank: A
severity: low
risk_tags: [infra]
state: proposed
findings: [WT-8, SC-3]
scope:
  repo: workertransfer
  include: [".github/workflows/**", ".github/renovate.json", "docker-compose.yml", "docker/*.Dockerfile", "deploy/helm/**/values.yaml"]
  exclude: []
estimate: { session_pct: 10, weekly_pct: 1.5, gold_usd: 0 }
dod:
  - Alle uses:-Zeilen per Commit-SHA (mit Versionskommentar)
  - Basis- und Dienst-Images per Digest, kein latest (mailpit)
  - Renovate (selbst gehostet oder GitHub-App – Jurisdiktion dokumentieren) für Actions, NuGet, npm, Images
boss: "CI-Job pin-check (keine Tag-Referenzen, keine latest-Images) grün"
evidence: [".github/workflows/ci.yml:40,42,81,124,128,393", "docker-compose.yml:126,155,172", "docker/dotnet-service.Dockerfile:13,64", "07-warden-workertransfer.md WT-8"]
board: { provider: github, issue: 121 }
---
# WT: Actions per SHA, Images per Digest, Renovate

## Objective
Ein kompromittierter Tag einer Drittaktion oder eines Images läuft nicht mehr ungeprüft im Build.

## Definition of Done
- [ ] Review + Freigabe (infra, Rank A)
- [ ] Pins + Renovate-Konfiguration + pin-check-Job
- [ ] Boss: `pin-check` grün
