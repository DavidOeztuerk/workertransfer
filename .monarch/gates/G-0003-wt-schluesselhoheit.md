---
id: G-0003
type: standard
title: Workertransfer – Schlüsselhoheit (Master-Key, JWT-Key, Secret-Store)
rank: S
risk_tags: [infra, auth, secrets]
repo: workertransfer
state: proposed
quests: [Q-0010]
boss: "SecretStore_RotatesMasterKey_WithoutDataLoss grün in CI + Entscheidung OpenBao/Infisical als ADR"
estimate: { session_pct: 30, weekly_pct: 4, gold_usd: 0 }
board: { provider: github, milestone: 3 }
---
# Workertransfer – Schlüsselhoheit

Master-Key = SHA-256 eines Env-Strings, ohne Mindestlänge, ohne Rotation, ein Schlüssel für Data-Protection-Ring und Nutzer-KI-Schlüssel, in jedem Dienst vorhanden; JWT-Privatschlüssel aus `.env`; OpenBao (in Noelia vorhanden) ungenutzt; Infisical geplant (`CLAUDE.md:159`), Jurisdiktion offen.

**Warden-Stand:** WT-10 · SC-5 bestätigt, Warden-Rank **S** (Secrets), Schwere medium.
**Policy (Rank S):** nie autonom; **jede mutierende Aktion einzeln freigeben** (Schlüsselwechsel, Re-Encryption, Secret-Store-Anbindung).
