---
id: Q-0012
gate: G-0004
kind: main
rank: A
severity: medium
risk_tags: [pii, auth, public-api]
state: proposed
findings: [WT-11]
scope:
  repo: workertransfer
  include: ["src/consent-service/**/Api/ConsentEndpoints.cs", "src/consent-service/**/Domain/**/Capability.cs", "src/shared/**/HttpConsentGate.cs", "src/gateway/**/ocelot.json", "tests/WorkerTransfer.Consent*.Tests/**"]
  exclude: []
estimate: { session_pct: 18, weekly_pct: 2, gold_usd: 0 }
dod:
  - "/consent/check(-batch) nur für: die Person selbst, die in der Capability genannte Firma (Tenant aus Token) oder Dienste per M2M"
  - Rate Limit auf /consent/* im Gateway
  - Antwort verrät keine Existenz fremder Personen
boss: "ConsentCheck_DeniesUnrelatedCaller grün in CI"
evidence: ["consent …/Api/ConsentEndpoints.cs:159-194,200-252", "ocelot.json:271", "…/Capability.cs:62", "07-warden-workertransfer.md WT-11"]
board: { provider: github, issue: 122 }
---
# Consent-Abfragen nur für berechtigte Aufrufer

## Objective
Kein angemeldeter Nutzer kann mehr abfragen, ob eine beliebige Person mit einer bestimmten Firma teilt.

## Definition of Done
- [ ] Design → Review → Freigabe (pii, Rank A) – bewusstes Design (`:159-162`) ändern, ADR anpassen
- [ ] Autorisierung + Rate Limit
- [ ] Boss: `ConsentCheck_DeniesUnrelatedCaller` grün in CI
