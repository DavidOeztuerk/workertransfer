---
id: G-0005
type: standard
title: Workertransfer – Go-Live-Gate (vor dem ersten echten Konto)
rank: A
risk_tags: [migration, infra]
repo: workertransfer
state: proposed
quests: [Q-0015]
boss: "Go-Live-Checkliste vollständig grün: G-0001, G-0002, G-0003 gecleart + Q-0015"
estimate: { session_pct: 12, weekly_pct: 1.5, gold_usd: 0 }
board: { provider: github, milestone: 5 }
---
# Workertransfer – Go-Live-Gate

Viele Befunde haben heute niedrige Schwere nur, weil die Plattform laut ADR-0045 nicht live ist. Dieses Gate macht diese Bedingung explizit: Erst wenn es gecleart ist, darf ein echtes Konto entstehen.

Voraussetzungen: G-0001 (Vertrauensgrenze), G-0002 (Konto-Lebenszyklus), G-0003 (Schlüssel) gecleart; Entscheidungen „vor dem Start“ aus `docs/ENTSCHEIDUNG-VOR-DEM-START.md` (Hosting, Mail, Plattform-KI, Keycloak im Betrieb mit echter Lizenz – siehe G-0011).
