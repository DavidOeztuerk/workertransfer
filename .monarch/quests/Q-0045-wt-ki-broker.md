---
id: Q-0045
gate: G-0001
kind: side
rank: A
severity: medium
risk_tags: [auth, pii]
state: proposed
findings: [WT-1]
scope:
  repo: workertransfer
  include: ["src/identity-service/**", "src/profile-service/**", "src/applications-service/**", "src/messages-service/**", "src/jobs-service/**", "src/scout-service/**", "src/shared/**", "tests/**", "docs/adr/**", "docker-compose.yml", "deploy/helm/**"]
  exclude: ["**/Migrations/**"]
estimate: { session_pct: 35, weekly_pct: 6, gold_usd: 0 }
dod:
  - Design zuerst (ADR) → Warden → Code
  - Entschlüsselte KI-Schlüssel verlassen genau einen Dienst (KI-Broker) und werden nie an Aufrufer herausgegeben; D6/D7 entfallen oder liefern keine Schlüssel mehr
  - Aufrufer (profile, applications, messages, jobs, scout) nutzen den Broker; Negativtests
boss: "Kein Dienst außer dem Broker erhält einen entschlüsselten KI-Schlüssel – Test über alle Aufrufer grün in CI"
---
# KI-Broker: Schlüssel verlassen nur einen Dienst

Folge-Quest aus Q-0001 (User-Entscheidung E1, 28.10.): heute zwei KI-Schlüssel-Türen mit je drei Aufrufern.
