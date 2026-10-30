---
id: Q-0043
gate: G-0001
kind: main
rank: A
severity: high
risk_tags: [auth, pii]
state: proposed
findings: [WT-1]
scope:
  repo: workertransfer
  include: ["src/identity-service/**/Api/InternalEndpoints.cs", "src/identity-service/**/Api/NotifyEndpoints.cs", "src/identity-service/**/Api/Internal/**", "src/identity-service/**/Application/Account/Settings.cs", "src/identity-service/**/Infrastructure/IdentityInfrastructure.cs", "src/identity-service/**/Infrastructure/Mail/NotifySettings.cs", "src/notification-service/**/Api/NotificationEndpoints.cs", "src/notification-service/**/Infrastructure/NotificationInfrastructure.cs", "src/notification-service/**/Infrastructure/Mail/**", "src/shared/**", "docker-compose.yml", "deploy/helm/**", "Makefile", ".env.example", "tests/WorkerTransfer.Identity*.Tests/**", "tests/WorkerTransfer.Notification*.Tests/**", "tests/WorkerTransfer.CrossCutting.Tests/EnvironmentFileTests.cs", "docs/adr/**"]
  exclude: ["**/Migrations/**"]
estimate: { session_pct: 25, weekly_pct: 4, gold_usd: 0 }
dod:
  - Design .monarch/knowledge/Q-0001-design.md nach Warden-Review (Phase 1 + Delta) umgesetzt; Auflagen 1–6 erfüllt
  - Jede /internal-Tür von identity prüft ein eigenes Recht je Aufrufer (eigenes Secret je Aufrufer, feste Rechtetabelle im Code); alles andere 404 nach einheitlichem Vertrag (Fallback /internal/{**rest}, Prüfung vor Body-Binding)
  - KI-Schlüssel-Türen D6/D7 nur für die heute belegten Aufrufer (User-Entscheidung E1); alle anderen 404
  - E4 (a): notification-service bekommt ein eigenes eingehendes Setting; die 7 Sender senden unverändert und erhalten weiter 202; Identity__Secret von notification wird S_notification und öffnet nur D1; der alte gemeinsame Wert bekommt an allen identity-Türen 404
  - Rotation über SecretPrevious (nur identity); Compose, Helm, Makefile (make env), .env.example und EnvironmentFileTests angepasst
  - Volle Testmatrix 7 Türen × 10 Aufrufer + unbekannt, Wächter-Test in beide Richtungen, Konsistenztest gegen compose; Tests rot → grün
boss: "InternalEndpoints_NotifySecret_CannotReadIdentityOrAiKey grün in CI"
evidence: ["workertransfer/src/identity-service/…/Api/InternalEndpoints.cs:22,141-232", "…/Application/Account/Settings.cs:193", "docker-compose.yml (12 Dienste mit dem Secret)", "07-warden-workertransfer.md WT-1", ".monarch/knowledge/Q-0001-design.md"]
board: { provider: github, issue: 111 }
---
# `/internal/*` je Tür und Aufrufer eigenes Recht (ersetzt Q-0001)

Ersetzt Q-0001 (abgebrochen: Scope aus Backlog-Import unvollständig, Design-Review B1/B5; Bezug Monarch #16). Entscheidungen: E1 Aufrufer je Tür, E2 → Q-0044, E3 in diese Quest integriert, E4 (a) eng (projects-74, 28.10.).

## Objective
Ein Dienst, der nur Mails auslösen darf, kann nicht mehr Name/E-Mail beliebiger Konten oder entschlüsselte KI-Schlüssel abrufen.
