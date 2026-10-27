---
id: Q-0005
gate: G-0002
kind: main
rank: A
severity: medium
risk_tags: [auth, pii]
state: proposed
findings: [WT-3, N-2, SC-6]
scope:
  repo: workertransfer
  include: ["src/identity-service/**", "src/notifications-service/**/Mail/**", "web/src/features/auth/**", "tests/WorkerTransfer.Identity*.Tests/**", "tests/e2e/**"]
  exclude: ["**/Migrations/*.Designer.cs"]
estimate: { session_pct: 25, weekly_pct: 3, gold_usd: 0 }
dod:
  - Reset per E-Mail-Token (Zweck PasswordReset, einmalig, kurze Laufzeit, Entropie ≥ 128 bit)
  - Passwort ändern (mit altem Passwort) und setzen (für Provider-Konten)
  - Jeder Wechsel beendet alle Sitzungen der Person (gleicher Mechanismus wie Q-0008)
  - Keine Konto-Enumeration über Reset-Antworten
boss: "PasswordReset_ConsumesTokenOnce_AndEndsAllSessions grün in CI"
evidence: ["…/Api/AuthEndpoints.cs:84-526", "…/Domain/Verification/VerificationToken.cs:6-10", "…/ExternalSignIn.cs:187-188", "07-warden-workertransfer.md WT-3, N-2"]
board: { provider: github, issue: 115 }
---
# Passwort zurücksetzen, ändern und setzen

## Objective
Konten sind wiederherstellbar, kompromittierte Passwörter rotierbar, und der lokale Login ist ein vollständiger souveräner Weg neben Google/Microsoft/Apple (SC-6).

## Definition of Done
- [ ] Design → Review → Freigabe (auth, Rank A)
- [ ] Endpunkte + UI + Mailtexte; Rate Limit auf Reset-Anfragen
- [ ] Negativtests: abgelaufenes/benutztes Token, fremdes Konto
- [ ] Boss: `PasswordReset_ConsumesTokenOnce_AndEndsAllSessions` grün in CI
