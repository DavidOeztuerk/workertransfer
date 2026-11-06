---
id: Q-0004
gate: G-0001
kind: side
rank: A
severity: low
risk_tags: [auth]
state: cleared
findings: [D4]
scope:
  repo: workertransfer
  include: ["docker-compose.yml", "deploy/helm/**", "scripts/**", "Makefile", ".env.example", ".github/workflows/ci.yml", "tests/**", "docs/**", "src/shared/**", "CLAUDE.md", "AGENTS.md", ".claude/skills/**"]
  exclude: ["**/Migrations/**"]
estimate: { session_pct: 10, weekly_pct: 2, gold_usd: 0 }
dod:
  - Jeder Dienst bekommt ein eigenes Evidence-Secret; die Control Plane bekommt es je Dienst aus der Umgebung, nie aus einer Datei
  - make env würfelt 15 Werte; Rotation je Dienst möglich; make evidence und make evidence-check fragen je Dienst mit dessen Secret
boss: "Leak-Probe: Secret von Dienst A öffnet /noelia von Dienst B nicht (404)"
evidence: ["docker-compose.yml:115-122", "scripts/control-plane.sh:69,129", "CP Program.cs:510, FleetCollector.cs:448", "07-warden-workertransfer.md D4"]
board: { provider: github, issue: 114, pr: 110 }
---
# Operator-Secret je Dienst

## Objective
Ein geleaktes Operator-Secret öffnet nur noch die Betriebsansicht eines Dienstes.

## Definition of Done
- [ ] Review + Freigabe (auth, Rank A)
- [ ] Secrets je Dienst in Compose/Helm, Fleet-Datei angepasst
- [ ] Boss: Leak-Probe grün
