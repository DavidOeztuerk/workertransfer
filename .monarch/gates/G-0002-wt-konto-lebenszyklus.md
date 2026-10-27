---
id: G-0002
type: standard
title: Workertransfer – Konto-Lebenszyklus und Anmeldung
rank: A
risk_tags: [auth, pii]
repo: workertransfer
state: proposed
quests: [Q-0005, Q-0006, Q-0007, Q-0008, Q-0009]
boss: "Journey-Suite AccountLifecycle (Reset, externe Verknüpfung, Löschung mit Step-up, Abmelden überall, Brute-Force-Bremse) grün in CI"
estimate: { session_pct: 100, weekly_pct: 13, gold_usd: 0 }
board: { provider: github, milestone: 2 }
---
# Workertransfer – Konto-Lebenszyklus und Anmeldung

Kein Passwort-Reset/-Wechsel (WT-3), externe Verknüpfung vor bewiesener Adresse (WT-2) – zusammen Pre-Account-Hijacking (N-2) –, Löschung ohne Step-up (WT-4), Abmelden widerruft nur das vorgelegte Token (02-N5), entfernte Firmenmitglieder behalten ≤ 15 min Lesezugriff (WT-7), Rate Limit nur pro Peer-IP und im Prozess (WT-6).

**User-Entscheidung 2026-10-27:** Abmelden soll **alle Sitzungen der Person beenden** („am liebsten alle abmelden“) – ersetzt die Claude-Entscheidung „mehrere Tabs erlaubt, Cross-Tab-Logout“ (compact-summary 2026-10-07T02:42:31Z). Umgesetzt in Q-0008.
**Warden-Stand:** WT-2, WT-3, WT-4, WT-6, WT-7 bestätigt; N-2 neu (`07-warden-workertransfer.md`).
**Policy (Rank A):** Design → Review → Freigabe → Implementierung; Security-Warden Pflicht.

| Quest | Titel | Rank | Schwere |
|---|---|---|---|
| Q-0005 | Passwort zurücksetzen, ändern und setzen | A | medium |
| Q-0006 | Externe Verknüpfung erst nach bewiesener Adresse | A | medium |
| Q-0008 | Abmelden beendet alle Sitzungen der Person | A | medium |
| Q-0007 | Löschung mit Step-up und Bedenkzeit | A | medium |
| Q-0009 | Brute-Force-Bremse hinter Proxy und je Konto | A | medium |
