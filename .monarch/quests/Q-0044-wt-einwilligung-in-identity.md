---
id: Q-0044
gate: G-0001
kind: main
rank: A
severity: high
risk_tags: [auth, pii]
state: proposed
findings: [WT-1]
scope:
  repo: workertransfer
  include: ["src/identity-service/**", "src/advisor-service/**", "src/transfer-service/**", "src/shared/**", "tests/WorkerTransfer.Identity*.Tests/**", "tests/WorkerTransfer.Advisor*.Tests/**", "tests/WorkerTransfer.Transfer*.Tests/**", "docs/adr/**"]
  exclude: ["**/Migrations/**"]
estimate: { session_pct: 25, weekly_pct: 4, gold_usd: 0 }
dod:
  - Design zuerst (Consent-Client in identity, Tenant-Parameter/Token-Weitergabe in advisor und transfer) → Warden → Code
  - identity gibt Personendaten (D5, D6) nur nach eigener Einwilligungs-/Ledger-Prüfung heraus, nicht per Konvention beim Aufrufer (InternalEndpoints.cs:131-137)
  - Negativtest – berechtigter Aufrufer, aber Konto ohne Einwilligung → 404
boss: "InternalEndpoints_IdentityDoor_RequiresConsentInIdentity grün in CI"
---
# Einwilligungsprüfung in identity statt beim Aufrufer

Aus Q-0001 abgespalten (User-Entscheidung E2, 28.10.). Grenze bis dahin: berechtigte Aufrufer können jede Subject-ID abfragen (D3–D7).
