---
id: Q-0002
gate: G-0001
kind: main
rank: A
severity: high
risk_tags: [infra, pii, migration]
state: in_review
findings: [N-3, WT-5, WT-10]
scope:
  repo: workertransfer
  include: ["docker-compose.yml", "deploy/helm/**", "docker/**", "src/**", "scripts/**", "Makefile", ".env.example", ".github/workflows/ci.yml", "tests/**", "docs/**", "bugs/**", "Directory.Packages.props", "CLAUDE.md", "AGENTS.md", ".claude/skills/**", ".claude/commands/**"]
  exclude: ["**/Migrations/**"]
estimate: { session_pct: 40, weekly_pct: 6, gold_usd: 0 }
dod:
  - ADR-0058 nach Warden-Runde umgesetzt
  - Je Dienst eigene DB-Rolle, Eigentümer nur der eigenen Datenbank; CONNECT für PUBLIC entzogen, auch auf postgres und template1
  - Der Superuser hat kein Netz-Login mehr, auch auf bestehenden Volumes (Entscheid des Users vom 05.11.); kein Superuser in Connection-Strings; Helm ohne festes Passwort
  - WORKERTRANSFER_SECRETS_KEY nur bei identity, gespeicherte KI-Schlüssel bleiben lesbar; der Schlüsselbund hat je Dienst einen eigenen Schlüssel
  - make db-roles stellt ein bestehendes Volume um: Eigentümer je Objekt, eine Transaktion je Datenbank, Bund einmalig geleert (Entscheid des Users vom 05.11.), Fachdaten unverändert, wiederholbar
  - Boss und Migrationstest laufen gegen das echte Skript und die echten Migrationen und überspringen nicht
  - Compose, Helm, make env, .env.example, CI, Doku angepasst; Noelia-Ticket liegt in bugs/
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
