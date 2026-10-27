---
id: Q-0004
gate: G-0001
kind: side
rank: A
severity: low
risk_tags: [auth]
state: proposed
findings: [D4]
scope:
  repo: workertransfer
  include: ["docker-compose.yml", "deploy/helm/**/_helpers.tpl", "scripts/control-plane.sh", "scripts/control-plane-fleet.json", "Makefile"]
  exclude: []
estimate: { session_pct: 8, weekly_pct: 1, gold_usd: 0 }
dod:
  - Jeder Dienst bekommt ein eigenes Evidence-Secret; Fleet-Datei nutzt OperatorSecret je Dienst (CP unterstützt das bereits)
  - make env würfelt 15 Werte; Rotation je Dienst möglich
boss: "Leak-Probe: Secret von Dienst A öffnet /noelia von Dienst B nicht (404)"
evidence: ["docker-compose.yml:115-122", "scripts/control-plane.sh:69,129", "CP Program.cs:510, FleetCollector.cs:448", "07-warden-workertransfer.md D4"]
board: { provider: github, issue: 114 }
---
# Operator-Secret je Dienst

## Objective
Ein geleaktes Operator-Secret öffnet nur noch die Betriebsansicht eines Dienstes.

## Definition of Done
- [ ] Review + Freigabe (auth, Rank A)
- [ ] Secrets je Dienst in Compose/Helm, Fleet-Datei angepasst
- [ ] Boss: Leak-Probe grün
