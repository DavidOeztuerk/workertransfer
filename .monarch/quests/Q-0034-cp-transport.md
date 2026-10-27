---
id: Q-0034
gate: G-0011
kind: main
rank: A
severity: medium
risk_tags: [auth, infra]
state: proposed
findings: [CP-5, D6, D7, Warden-B-Neu-1]
scope:
  repo: [NoeliaControlPlane, workertransfer]
  include: ["NoeliaControlPlane/src/Noelia.ControlPlane/FleetCollector.cs", "NoeliaControlPlane/src/Noelia.ControlPlane/Program.cs", "NoeliaControlPlane/src/Noelia.ControlPlane/Access.cs", "NoeliaControlPlane/README.md", "workertransfer/scripts/control-plane.sh", "workertransfer/scripts/control-plane-fleet.json"]
  exclude: []
estimate: { session_pct: 15, weekly_pct: 2, gold_usd: 0 }
dod:
  - Startvalidierung - http-Adressen mit OperatorSecret nur in Development oder für Loopback, sonst Start-Fehler
  - UseForwardedHeaders mit expliziter Proxy-Allowlist; README dokumentiert ASPNETCORE_FORWARDEDHEADERS_ENABLED bzw. die Option
  - Lokaler OIDC-Start über https (Dev-Zertifikat) oder dokumentierte Browser-Einschränkung; Secure-Cookie bleibt Always
boss: "Startup_RejectsHttpFleetWithSecretInProduction + Oidc_BehindProxy_BuildsHttpsRedirectUri grün in CI"
evidence: ["FleetCollector.cs:213-216,268-281", "Program.cs:51-53,99-109", "Access.cs:168", "WT control-plane.sh:128", "07-warden-controlplane.md CP-5 · D6, D7, Neu-1"]
board: { provider: github, issue: 126 }
---
# Transport der CP absichern

## Objective
Das Operator-Secret geht nie im Klartext durch ein echtes Netz, und die CP funktioniert hinter dem empfohlenen TLS-Proxy.

## Definition of Done
- [ ] Design → Review → Freigabe (auth/infra, Rank A)
- [ ] Validierung, ForwardedHeaders, Doku
- [ ] Boss: beide Tests grün in CI
