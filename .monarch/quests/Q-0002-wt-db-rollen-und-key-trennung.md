---
id: Q-0002
gate: G-0001
kind: main
rank: A
severity: high
risk_tags: [infra, pii, migration]
state: proposed
findings: [N-3, WT-5, WT-10]
scope:
  repo: workertransfer
  include: ["docker-compose.yml", "deploy/helm/**", "docker/**", "src/shared/**/SecretStore.cs", "src/shared/**/KeyRing.cs", "scripts/**"]
  exclude: []
estimate: { session_pct: 25, weekly_pct: 3, gold_usd: 0 }
dod:
  - Je Dienst eigene DB-Rolle mit Rechten nur auf die eigene Datenbank
  - Master-Key nur in Diensten, die verschlüsselte Daten wirklich lesen; Schlüssel je Zweck getrennt
  - Postgres-Superuser nicht mehr in Connection-Strings; Helm ohne festes Passwort
  - Migrationsweg für bestehende lokale Volumes beschrieben
boss: "CrossServiceDbAccess_Denied (Dienst A kann identity-DB nicht lesen) grün in CI"
evidence: ["docker-compose.yml:130-136 + 15× Username=worker", "deploy/helm/…/values.yaml:112-120", "src/shared/…/KeyRing.cs:58", "07-warden-workertransfer.md N-3"]
board: { provider: github, issue: 112 }
---
# DB-Rolle je Dienst, Master-Key nur wo nötig

## Objective
Ein kompromittierter Dienst erreicht nur seine eigene Datenbank und kann fremde verschlüsselte Daten nicht entschlüsseln (bricht die Kette N-3 auch ohne Q-0001).

## Definition of Done
- [ ] Design → Review → Freigabe (Infra + Daten, Rank A)
- [ ] Init-Skript legt Rollen je Dienst an; Connection-Strings je Dienst
- [ ] Key-Trennung: Data-Protection-Ring und Nutzer-KI-Schlüssel nicht mehr mit demselben Master-Key
- [ ] Boss: `CrossServiceDbAccess_Denied` grün in CI
