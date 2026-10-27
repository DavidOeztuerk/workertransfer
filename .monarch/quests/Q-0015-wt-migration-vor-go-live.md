---
id: Q-0015
gate: G-0005
kind: main
rank: A
severity: low
risk_tags: [migration]
state: proposed
findings: [WT-9]
scope:
  repo: workertransfer
  include: ["src/**/Persistence/Migrations/**", "src/shared/**/SchemaMigration.cs", "deploy/helm/**/services.yaml", "docs/adr/0045-*.md"]
  exclude: []
estimate: { session_pct: 12, weekly_pct: 1.5, gold_usd: 0 }
dod:
  - "Entscheidung dokumentiert: Startbasis ist das neue InitialSchema; kein girder_*-Bestand existiert irgendwo (Prüfung aller Volumes/Umgebungen)"
  - Widerspruch Commit 04e614c1 („Wanderung mit RenameTable“) vs. Baum ohne RenameTable im ADR aufgelöst
  - Auto-Migration beim Start nur mit Migrationssperre (EF Core 9+ / Npgsql verifizieren) oder als separater Job; Warnung bei replicas > 1 als Test
boss: "SchemaMigration_IsSingleWriter_UnderConcurrentStart grün in CI"
evidence: ["…/Migrations/20260924172250_InitialSchema.cs", "…/SchemaMigration.cs:60-86", "deploy/helm/…/services.yaml:30,64-65", "Commit 04e614c1", "07-warden-workertransfer.md WT-9 (teilweise)"]
board: { provider: github, issue: 125 }
---
# Migrationspfad und -sperre vor Go-Live

## Objective
Vor dem ersten echten Konto ist geklärt, dass keine Altdaten existieren, und Schema-Migrationen laufen nie parallel.

## Definition of Done
- [ ] Design → Review → Freigabe (migration, Rank A)
- [ ] ADR-Ergänzung + Test für parallele Starts
- [ ] Boss: `SchemaMigration_IsSingleWriter_UnderConcurrentStart` grün in CI
