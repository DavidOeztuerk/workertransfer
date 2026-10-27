---
id: Q-0007
gate: G-0002
kind: main
rank: A
severity: medium
risk_tags: [auth, pii]
state: proposed
findings: [WT-4]
scope:
  repo: workertransfer
  include: ["src/identity-service/**/Api/ErasureEndpoints.cs", "src/identity-service/**/Application/Erasure/**", "web/src/features/account/**", "tests/WorkerTransfer.Identity*.Tests/**", "tests/e2e/**"]
  exclude: []
estimate: { session_pct: 15, weekly_pct: 2, gold_usd: 0 }
dod:
  - Löschantrag verlangt frische Anmeldung (Passwort oder Provider-Re-Auth ≤ 5 min)
  - Bedenkzeit mit Bestätigungsmail und Abbruchlink, danach erst Kaskade
  - Sperre und Widerruf sofort, endgültige Löschung nach Frist (DSGVO-Frist beachten)
boss: "Erasure_RequiresFreshAuth_AndCanBeCancelledWithinGracePeriod grün in CI"
evidence: ["…/Api/ErasureEndpoints.cs:20-48", "…/Application/Erasure/RequestErasure.cs:63-88", "07-warden-workertransfer.md WT-4"]
board: { provider: github, issue: 117 }
---
# Löschung mit Step-up und Bedenkzeit

## Objective
Ein entwendetes Session-Cookie oder offenes Gerät reicht nicht mehr, um ein Konto samt aller Dienstdaten unwiderruflich zu löschen.

## Definition of Done
- [ ] Design → Review → Freigabe (auth/pii, Rank A); Juristische Frist mit Juristenliste abgleichen
- [ ] Step-up + Bedenkzeit + Abbruch
- [ ] Boss: `Erasure_RequiresFreshAuth_AndCanBeCancelledWithinGracePeriod` grün in CI
