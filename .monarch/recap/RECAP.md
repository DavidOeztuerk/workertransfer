# RECAP

## Stand
- Zeitpunkt: 2026-11-06T03:57:30Z
- Plan: Pro
- Mana 5h: 69% (Limit 90%, Reset 2026-11-06T05:50:00Z)
- Mana 7d: 76% (Limit 96%, Reset 2026-11-11T11:00:00Z)
- Limits: 5h 90%, 7d 96% (adaptive)

## Aktiv
- keine aktive Quest

## Erledigt
- Q-0002 [A] cleared: DB-Rolle je Dienst, Master-Key nur wo nötig - https://github.com/DavidOeztuerk/workertransfer/pull/110 - Evidence: https://github.com/DavidOeztuerk/workertransfer/actions/runs/42153375917/job/127681915618, https://github.com/DavidOeztuerk/workertransfer/actions/runs/42153375917/job/127681915517, https://github.com/DavidOeztuerk/workertransfer/actions/runs/42153375917/job/127681915290, https://github.com/DavidOeztuerk/workertransfer/actions/runs/42153375917/job/127681915771, https://github.com/DavidOeztuerk/workertransfer/actions/runs/42153375917/job/127681915651, https://github.com/DavidOeztuerk/workertransfer/pull/110
- Q-0003 [A] cleared: Compose: Dienst-, Postgres- und Mailpit-Ports nur auf Loopback - https://github.com/DavidOeztuerk/workertransfer/pull/110 - Evidence: https://github.com/DavidOeztuerk/workertransfer/actions/runs/40508698213/job/122204587642, https://github.com/DavidOeztuerk/workertransfer/actions/runs/40508698213/job/122204587701, https://github.com/DavidOeztuerk/workertransfer/actions/runs/40508698213/job/122204587195, https://github.com/DavidOeztuerk/workertransfer/actions/runs/40508698213/job/122204587636, https://github.com/DavidOeztuerk/workertransfer/actions/runs/40508698213/job/122204587439, https://github.com/DavidOeztuerk/workertransfer/pull/110
- Q-0004 [A] cleared: Operator-Secret je Dienst - https://github.com/DavidOeztuerk/workertransfer/pull/110 - Evidence: https://github.com/DavidOeztuerk/workertransfer/actions/runs/42153375917/job/127681915618, https://github.com/DavidOeztuerk/workertransfer/actions/runs/42153375917/job/127681915517, https://github.com/DavidOeztuerk/workertransfer/actions/runs/42153375917/job/127681915290, https://github.com/DavidOeztuerk/workertransfer/actions/runs/42153375917/job/127681915771, https://github.com/DavidOeztuerk/workertransfer/actions/runs/42153375917/job/127681915651, https://github.com/DavidOeztuerk/workertransfer/pull/110
- Q-0043 [A] cleared: `/internal/*` je Tür und Aufrufer eigenes Recht (ersetzt Q-0001) - https://github.com/DavidOeztuerk/workertransfer/pull/110 - Evidence: https://github.com/DavidOeztuerk/workertransfer/actions/runs/42153375917/job/127681915618, https://github.com/DavidOeztuerk/workertransfer/actions/runs/42153375917/job/127681915517, https://github.com/DavidOeztuerk/workertransfer/actions/runs/42153375917/job/127681915290, https://github.com/DavidOeztuerk/workertransfer/actions/runs/42153375917/job/127681915771, https://github.com/DavidOeztuerk/workertransfer/actions/runs/42153375917/job/127681915651, https://github.com/DavidOeztuerk/workertransfer/pull/110

## Offen
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
G-0001 ist in develop (5ae80a5a), Q-0002, Q-0003, Q-0004 und Q-0043 sind gecleart. Owner-Aktion: make db-roles und make up. Offen: Issues 135 bis 138; Q-0044 und Q-0045 als eigenes Gate (Punkt 13). Weiter mit Punkt 2 der Prioliste.

## Offene Entscheidungen
- Noelia-Fehler gefunden und ohne WT-Code reproduziert: noelia.m2m.caller-policy läuft beim Start vor den Routen, der Bericht zeigt für identity Fail, nach dem Start Pass mit 7 Türen. CI ist nicht betroffen, make evidence-check lokal rot für identity. Kein Umweg in WT (bugs/README.md). Der Ticket-Text liegt in .monarch/knowledge/bug-pruefung-der-tueren-laeuft-vor-den-routen.md; bugs/ liegt außerhalb des Scopes von Q-0043, gh darf keinen .monarch-Pfad nennen. Der User muss entscheiden, ob vor dem Merge ein Noelia 7.1.2 nötig ist.
