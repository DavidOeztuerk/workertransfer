---
id: Q-0013
gate: G-0004
kind: side
rank: B
severity: low
risk_tags: [public-api]
state: proposed
findings: [WT-12]
scope:
  repo: workertransfer
  include: ["src/**/Api/*Endpoints.cs", "web/src/**/api/**", "web/src/features/auth/**/externalLogin.ts", "src/gateway/**/ocelot.json", "tests/**/WireContractTests.cs"]
  exclude: []
estimate: { session_pct: 12, weekly_pct: 1.5, gold_usd: 0 }
dod:
  - Fehlerantworten tragen ein stabiles code-Feld; UI matcht nur noch Codes
  - Entscheidung zur Routen-Versionierung (Präfix oder Header) als ADR
boss: "WireContract_ErrorCodes_StableAcrossWordingChanges grün in CI"
evidence: ["…/ExternalLoginEndpoints.cs:211-214", "…/ResumeEndpoints.cs:452-453", "ocelot.json:43-582", "07-warden-workertransfer.md WT-12"]
board: { provider: github, issue: 123 }
---
# Maschinenlesbare Fehlercodes, Versionspräfix

## Objective
Eine Wortlautänderung im Backend bricht die UI nicht mehr stumm.

## Definition of Done
- [ ] Plan-Freigabe (Rank B)
- [ ] code-Feld + UI-Umstellung + Vertragstest
- [ ] Boss: `WireContract_ErrorCodes_StableAcrossWordingChanges` grün in CI
