---
id: G-0011
type: standard
title: Control Plane – Betreiberzugang und Betrieb (inkl. Keycloak im Betrieb)
rank: A
risk_tags: [auth, infra]
repo: [NoeliaControlPlane, workertransfer]
state: proposed
quests: [Q-0032, Q-0033, Q-0034, Q-0036, Q-0035, Q-0037]
boss: "CP-Betriebsprofil hinter TLS-Proxy mit Keycloak (nicht start-dev): Login, Rollenentzug wirkt ≤ absolute Grenze, keine GET-Mutation, Daten 0600 + Retention – als Integrationstest grün"
estimate: { session_pct: 90, weekly_pct: 12, gold_usd: 0 }
board: { provider: github, milestone: 7 }
---
# Control Plane – Betreiberzugang und Betrieb

Dieses Gate deckt die Session-Entscheidung „Control Plane im Betrieb mit Keycloak und echter Lizenz (I2)“ vor dem Start ab (WT-b245 2026-10-06T21:33Z), soweit es Zugang und Betrieb betrifft; die Lizenz steht in G-0012.

**Warden-Stand (`07-warden-controlplane.md`):** CP-4, CP-5 · D6, CP-7 (87 MB, 0644 real gemessen), CP-9, D2, D7, CP-14 bestätigt; CP-6, CP-11 teilweise; CP-8 bestätigt (D/low); Neu-1 (ForwardedHeaders fehlt hinter TLS-Proxy). CP-10 verworfen (nur toter Parameter).

| Quest | Titel | Rank | Schwere |
|---|---|---|---|
| Q-0032 | Absolute Sitzungsgrenze und Rollen-Auffrischung | A | medium |
| Q-0033 | Keine Mutation per GET; Policy-Ablehnungen ins Audit | A | medium |
| Q-0034 | Transport: http nur Dev/Loopback, ForwardedHeaders, Secure-Cookie lokal | A | medium |
| Q-0036 | Datenhaltung: Retention, 0600, absolute Pfade, Lese-Cache | A | medium |
| Q-0035 | Keycloak-Betriebsform | A | low |
| Q-0037 | Webhooks signieren, https in Prod, Markup escapen | B | low |
