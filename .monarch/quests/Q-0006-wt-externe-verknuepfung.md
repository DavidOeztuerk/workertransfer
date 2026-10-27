---
id: Q-0006
gate: G-0002
kind: main
rank: A
severity: medium
risk_tags: [auth]
state: proposed
findings: [WT-2, N-2]
blocked_by: []
scope:
  repo: workertransfer
  include: ["src/identity-service/**/Application/SignIn/**", "src/identity-service/**/Infrastructure/Security/ExternalLogin.cs", "src/identity-service/**/Application/Registration/**", "tests/WorkerTransfer.Identity*.Tests/**"]
  exclude: []
estimate: { session_pct: 20, weekly_pct: 3, gold_usd: 0 }
dod:
  - Provider-Verknüpfung wird erst geschrieben, wenn die Adresse bewiesen ist (Mail-Link), nie vorher
  - Bestätigt das Opfer eine Adresse, die ein fremder Provider-sub beansprucht hat, wird dieser Link verworfen
  - Lokale Registrierung auf eine „gesquattete“ Adresse führt zu Reset/Übernahme durch den Adressinhaber (mit Q-0005)
boss: "PreAccountHijack_MicrosoftSquatter_CannotSignInAfterVictimConfirms grün in CI"
evidence: ["…/SignIn/ExternalSignIn.cs:189-200,285-292", "…/Security/ExternalLogin.cs:220,240", "…/Registration/Register.cs:116-123", "07-warden-workertransfer.md WT-2, N-2"]
board: { provider: github, issue: 116 }
---
# Externe Verknüpfung erst nach bewiesener Adresse

## Objective
Die Kette N-2 (Microsoft-Konto mit fremder Adresse → inaktives Konto → Opfer bestätigt → Angreifer meldet sich an) ist unterbrochen.

## Definition of Done
- [ ] Design → Review → Freigabe (auth, Rank A)
- [ ] Link-Erstellung hinter Adressbeweis; Pending-Links mit Ablauf
- [ ] Boss: `PreAccountHijack_MicrosoftSquatter_CannotSignInAfterVictimConfirms` grün in CI
