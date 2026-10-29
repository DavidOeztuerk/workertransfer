---
id: Q-0009
gate: G-0002
kind: main
rank: A
severity: medium
risk_tags: [auth, infra]
state: proposed
findings: [WT-6, N-1]
blocked_by: [Q-0003]
scope:
  repo: workertransfer
  include: ["src/gateway/**", "src/identity-service/**/Api/AuthEndpoints.cs", "src/gateway/**/ocelot.json", ".env.example", "Makefile", "docker-compose.yml", "deploy/helm/**"]
  exclude: []
estimate: { session_pct: 15, weekly_pct: 2, gold_usd: 0 }
dod:
  - ForwardedHeaders mit expliziter Proxy-Allowlist im Gateway
  - Zusätzliche Drosselung je Konto-Identifier (ohne Enumeration oder Fremd-Sperre zu ermöglichen)
  - Zähler verteilt (Valkey), damit Replikate denselben Eimer teilen
  - Bremsfaktor 40 nur im Dev-Profil, nicht in .env.example-Default
boss: "Login_Throttled_PerAccount_BehindProxy grün in CI"
evidence: ["Noelia …/Http/ClientAddress.cs:44-55", "WT src/gateway/…/Program.cs:38", "ocelot.json:625", ".env.example:218", "07-warden-workertransfer.md WT-6"]
board: { provider: github, issue: 119 }
---
# Brute-Force-Bremse hinter Proxy und je Konto

## Objective
Login- und Registrierungsbremse wirkt pro echtem Client und pro Konto, auch hinter Ingress/Load Balancer, und lässt sich nicht über Dienstports umgehen (mit Q-0003).

## Definition of Done
- [ ] Design → Review → Freigabe (auth/infra, Rank A)
- [ ] Forwarded-Header-Vertrauen, Konto-Drosselung, verteilter Store
- [ ] Boss: `Login_Throttled_PerAccount_BehindProxy` grün in CI
