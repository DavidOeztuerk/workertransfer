# RECAP

## Stand
- Zeitpunkt: 2026-11-05T14:16:49Z
- Plan: Pro
- Mana 5h: 77% (Limit 90%, Reset 2026-11-05T17:20:00Z)
- Mana 7d: 55% (Limit 96%, Reset 2026-11-11T11:00:00Z)
- Limits: 5h 90%, 7d 96% (adaptive)

## Aktiv
- Gate G-0001 · Quest Q-0043 [A] · Phase in_progress

## Erledigt
- Q-0003 [A] verified: Compose: Dienst-, Postgres- und Mailpit-Ports nur auf Loopback - https://github.com/DavidOeztuerk/workertransfer/pull/110 - Evidence: https://github.com/DavidOeztuerk/workertransfer/actions/runs/40508698213/job/122204587642, https://github.com/DavidOeztuerk/workertransfer/actions/runs/40508698213/job/122204587701, https://github.com/DavidOeztuerk/workertransfer/actions/runs/40508698213/job/122204587195, https://github.com/DavidOeztuerk/workertransfer/actions/runs/40508698213/job/122204587636, https://github.com/DavidOeztuerk/workertransfer/actions/runs/40508698213/job/122204587439

## Offen
- Q-0043: Design-Delta (Notiz §13, ADR-0057) nach Warden-Runde umgesetzt
- Q-0043: Noelia auf 7.1.1 (7.1.0 weist jeden Türaufruf hinter UseTokenRevocation ab); identity stellt Dienst-Tokens aus (11 Clients, Audiences identity, notification, profile), ohne Gateway-Route
- Q-0043: Jede /internal-Tür trägt ihre feste Aufruferliste: 7 in identity, 1 in notification (E4, Entscheid des Users vom 05.11.), 2 in profile; je Gruppe ein Fallback, jede Ablehnung ist dasselbe 404
- Q-0043: KI-Schlüssel-Türen D6/D7 nur für die belegten Aufrufer (E1); alle anderen 404
- Q-0043: WORKERTRANSFER_NOTIFY_SECRET, X-Notify-Secret, Notify__Secret, Identity__Secret, Notifications__Secret und Profile__Secret sind entfernt
- Q-0043: Ist kein Token zu bekommen, nimmt der Aufrufer denselben Weg wie bei einem schweigenden Dienst (503, nie 500); der Client company-site trägt nie ein Token
- Q-0043: Compose, Helm, make env, .env.example und CI angepasst; ein Pod trägt nur sein eigenes Secret
- Q-0043: Matrix Türen × Aufrufer + unbekannt, Wächter Route ↔ Tabelle, Konsistenztest gegen compose; je Dienst ein Test über die echte Client-Fabrik; Tests rot → grün
- Q-0054: Neue CLAUDE.md höchstens etwa 20 KB; enthält nur, was jede Session braucht: was WT ist, Layout, wichtigste Befehle, Architektur-Regeln kurz, je eine verbindliche Zeile pro tragender Regel mit Verweis, Conventions that bite, Docs-Karte
- Q-0054: Jede
- Q-0054: Inhalt verschoben, nicht umgeschrieben; Sprache je Abschnitt unverändert
- Q-0054: Jede Regel, die ein Agent immer einhalten muss (Sicherheit, Einwilligung, Löschung, Fehlerformat, Branches), steht weiter als kurze Regel in der CLAUDE.md
- Q-0054: Skills unter .claude/skills/<thema>/SKILL.md mit präziser description; wt-nachweis und wt-tore erweitert statt gedoppelt
- Q-0054: AGENTS.md auf Doppelungen und Widersprüche geprüft, nur das Nötigste angeglichen
- Q-0054: .claude/worktrees/ in .gitignore
- Q-0054: Eine Warden-Runde: keine verbindliche Regel verloren oder nur in einem Skill versteckt; descriptions präzise
- Q-0054: develop vor und nach dem Merge grün

## Nächster konkreter Schritt
Q-0043 steht in_progress, Commit 65347f94 ist gepusht: 2125 Tests grün, 0 übersprungen, neun Gegenproben gefallen. Als Nächstes: 1. CI von PR 110 prüfen. 2. Warden-Code-Runde über 65347f94 (major fixen, minor als Issue). 3. quest submit. 4. Ticket aus .monarch/knowledge/bug-pruefung-der-tueren-laeuft-vor-den-routen.md nach bugs/ legen und daraus das Noelia-Issue per --body-file anlegen; Monarch-Issue aus .monarch/knowledge/issue-monarch-sed-fehlalarm.md ebenso, Body vorher aus .monarch heraus kopieren. 5. Q-0002: Design als ADR, Warden-Runde, per gate_plan den Scope um src/shared und bugs erweitern. Fakten: alle 15 Dienste tragen den Master-Key nur für ihren Schlüsselring, die KI-Schlüssel liest nur identity; initdb-SQL legt alle Datenbanken für worker an. 6. Q-0004, gate merge, aufräumen.

## Offene Entscheidungen
- Noelia-Fehler gefunden und ohne WT-Code reproduziert: noelia.m2m.caller-policy läuft beim Start vor den Routen, der Bericht zeigt für identity Fail, nach dem Start Pass mit 7 Türen. CI ist nicht betroffen, make evidence-check lokal rot für identity. Kein Umweg in WT (bugs/README.md). Der Ticket-Text liegt in .monarch/knowledge/bug-pruefung-der-tueren-laeuft-vor-den-routen.md; bugs/ liegt außerhalb des Scopes von Q-0043, gh darf keinen .monarch-Pfad nennen. Der User muss entscheiden, ob vor dem Merge ein Noelia 7.1.2 nötig ist.
