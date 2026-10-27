---
id: Q-0010
gate: G-0003
kind: main
rank: S
severity: medium
risk_tags: [secrets, infra, auth]
state: proposed
findings: [WT-10, SC-5]
blocked_by: [Q-0002]
scope:
  repo: workertransfer
  include: ["src/shared/**/SecretStore.cs", "src/shared/**/KeyRing.cs", "src/shared/WorkerTransfer.ServiceDefaults/ServiceFoundation.cs", "docker-compose.yml", "deploy/helm/**", "Makefile", "docs/adr/**"]
  exclude: []
estimate: { session_pct: 30, weekly_pct: 4, gold_usd: 0 }
dod:
  - KDF (HKDF) statt nacktem SHA-256, Mindestlänge/Entropie-Prüfung beim Start
  - Schlüssel-Versionierung mit zwei aktiven Schlüsseln (lesen alt+neu, schreiben neu) → Rotation ohne Datenverlust
  - Getrennte Schlüssel je Zweck (Data-Protection vs. Nutzer-KI-Schlüssel)
  - ADR: OpenBao (selbst betrieben) oder Infisical (Jurisdiktion belegen) für Master- und JWT-Key
boss: "SecretStore_RotatesMasterKey_WithoutDataLoss grün in CI"
evidence: ["src/shared/…/SecretStore.cs:30-35,52-62", "…/KeyRing.cs:58,200-205", "deploy/helm/…/values.yaml:112-115", "07-warden-workertransfer.md WT-10"]
board: { provider: github, issue: 120 }
---
# Master-Key mit KDF, Versionierung und Rotation

## Objective
Ein Schlüssel-Leck ist ohne Datenverlust behebbar, und kein Dienst hält mehr Schlüssel als nötig.

## Definition of Done
- [ ] Design → Review → Freigabe; **jede mutierende Aktion (Re-Encryption, Schlüsselwechsel) einzeln freigeben (Rank S)**
- [ ] Implementierung + Migrations-Runbook
- [ ] Boss: `SecretStore_RotatesMasterKey_WithoutDataLoss` grün in CI
