---
id: Q-0043
gate: G-0001
kind: main
rank: A
severity: high
risk_tags: [auth, pii]
state: in_progress
findings: [WT-1]
scope:
  repo: workertransfer
  include: ["src/**", "tests/**", "docker-compose.yml", "deploy/helm/**", "Makefile", ".env.example", ".github/workflows/ci.yml", "scripts/**", "docs/**", "Directory.Packages.props", "CLAUDE.md", "AGENTS.md", ".claude/skills/**", "web/e2e/**"]
  exclude: ["**/Migrations/**"]
estimate: { session_pct: 45, weekly_pct: 7, gold_usd: 0 }
dod:
  - Design-Delta (Notiz §13, ADR-0057) nach Warden-Runde umgesetzt
  - Noelia auf 7.1.1 (7.1.0 weist jeden Türaufruf hinter UseTokenRevocation ab); identity stellt Dienst-Tokens aus (11 Clients, Audiences identity, notification, profile), ohne Gateway-Route
  - Jede /internal-Tür trägt ihre feste Aufruferliste: 7 in identity, 1 in notification (E4, Entscheid des Users vom 05.11.), 2 in profile; je Gruppe ein Fallback, jede Ablehnung ist dasselbe 404
  - KI-Schlüssel-Türen D6/D7 nur für die belegten Aufrufer (E1); alle anderen 404
  - WORKERTRANSFER_NOTIFY_SECRET, X-Notify-Secret, Notify__Secret, Identity__Secret, Notifications__Secret und Profile__Secret sind entfernt
  - Ist kein Token zu bekommen, nimmt der Aufrufer denselben Weg wie bei einem schweigenden Dienst (503, nie 500); der Client company-site trägt nie ein Token
  - Compose, Helm, make env, .env.example und CI angepasst; ein Pod trägt nur sein eigenes Secret
  - Matrix Türen × Aufrufer + unbekannt, Wächter Route ↔ Tabelle, Konsistenztest gegen compose; je Dienst ein Test über die echte Client-Fabrik; Tests rot → grün
boss: "InternalEndpoints_NotifySecret_CannotReadIdentityOrAiKey grün in CI"
evidence: ["workertransfer/src/identity-service/…/Api/InternalEndpoints.cs:22,141-232", "…/Application/Account/Settings.cs:193", "docker-compose.yml (12 Dienste mit dem Secret)", "07-warden-workertransfer.md WT-1", ".monarch/knowledge/Q-0001-design.md"]
board: { provider: github, issue: 111 }
---
# `/internal/*` je Tür und Aufrufer eigenes Recht (ersetzt Q-0001)

Ersetzt Q-0001 (abgebrochen: Scope aus Backlog-Import unvollständig, Design-Review B1/B5; Bezug Monarch #16). Entscheidungen: E1 Aufrufer je Tür, E2 → Q-0044, E3 in diese Quest integriert, E4 (a) eng (projects-74, 28.10.).

## Objective
Ein Dienst, der nur Mails auslösen darf, kann nicht mehr Name/E-Mail beliebiger Konten oder entschlüsselte KI-Schlüssel abrufen.
