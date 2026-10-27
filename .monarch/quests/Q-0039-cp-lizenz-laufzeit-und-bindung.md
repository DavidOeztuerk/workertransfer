---
id: Q-0039
gate: G-0012
kind: main
rank: D
severity: low
risk_tags: []
state: proposed
findings: [CP-2, D3]
scope:
  repo: [NoeliaControlPlane, workertransfer]
  include: ["NoeliaControlPlane/src/Noelia.ControlPlane/Program.cs", "NoeliaControlPlane/src/Noelia.ControlPlane/Licence.cs", "NoeliaControlPlane/src/Noelia.ControlPlane/Alerts.cs", "NoeliaControlPlane/src/Noelia.ControlPlane/LicenceCommand.cs", "workertransfer/docs/**"]
  exclude: []
estimate: { session_pct: 8, weekly_pct: 1, gold_usd: 0 }
dod:
  - LicenceState wird pro Anfrage/Poll gegen TimeProvider ausgewertet; Ablauf degradiert ohne Neustart, Verlängerung ohne Neustart
  - "WT: vor dem Start eine an die Keycloak-Authority gebundene Lizenz (Session-Entscheidung I2); User prüft, ob die heutige ungebunden ist"
boss: "Licence_ExpiresAtRuntime_WithoutRestart grün in CI"
evidence: ["Program.cs:27-30", "Licence.cs:94,292-302", "Alerts.cs:157,212", "LicenceCommand.cs:58", "07-warden-controlplane.md CP-2, D3"]
board: { provider: github, issue: 128 }
---
# Lizenzstatus zur Laufzeit; gebundene Lizenz vor dem Start

## Objective
Ablauf und Verlängerung einer Lizenz wirken ohne Neustart; WT startet mit einer gebundenen Lizenz.

## Definition of Done
- [ ] Standard-Shadow + 1 Verifier (Rank D)
- [ ] Laufzeitprüfung + WT-Notiz
- [ ] Boss: `Licence_ExpiresAtRuntime_WithoutRestart` grün in CI
