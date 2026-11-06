# RECAP

## Stand
- Zeitpunkt: 2026-11-05T19:17:05Z
- Plan: Pro
- Mana 5h: 77% (Limit 90%, Reset 2026-11-05T22:20:00Z)
- Mana 7d: 67% (Limit 96%, Reset 2026-11-11T11:00:00Z)
- Limits: 5h 90%, 7d 96% (adaptive)

## Aktiv
- keine aktive Quest

## Erledigt
- Q-0003 [A] verified: Compose: Dienst-, Postgres- und Mailpit-Ports nur auf Loopback - https://github.com/DavidOeztuerk/workertransfer/pull/110 - Evidence: https://github.com/DavidOeztuerk/workertransfer/actions/runs/40508698213/job/122204587642, https://github.com/DavidOeztuerk/workertransfer/actions/runs/40508698213/job/122204587701, https://github.com/DavidOeztuerk/workertransfer/actions/runs/40508698213/job/122204587195, https://github.com/DavidOeztuerk/workertransfer/actions/runs/40508698213/job/122204587636, https://github.com/DavidOeztuerk/workertransfer/actions/runs/40508698213/job/122204587439

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
Q-0043 ist inreview (3e40063c: drei major-Befunde der Warden-Code-Runde behoben). ADR-0058 Fassung 2 steht in 70c92ecb, Design freigegeben (Warden approve-with-conditions, Auflagen eingearbeitet, keine Delta-Runde nötig). Scope von Q-0002 und Q-0004 ist per gate_plan erweitert. Issues angelegt: noelia #62 und #63, monarch #69, workertransfer #134. Als Nächstes: 1. quest accept und start Q-0002. 2. Ticket nach bugs/ legen, Text in .monarch/knowledge/bug-pruefung-der-tueren-laeuft-vor-den-routen.md, Verweis auf noelia #62; das heilt auch den toten Verweis in InternalDoorsTests.cs:296. 3. TDD nach ADR-0058: zuerst scripts/initdb/01-service-roles.sh und der Boss CrossServiceDbAccess_Denied in CrossCutting (Testcontainers ins csproj, kein Skip), dann der Bestandstest mit den echten Migrationen, KeyRingSecret, Compose, make db-roles, Helm, appsettings, Testwirte mit SecretStore.Variable (erst EINE Reihe), Doku (ADR-0046, AGENTS.md, wt-stack, commands/dienst.md, Kopf der Compose-Datei). Healthcheck im Zustand PASSWORD NULL messen, das hat noch niemand gefahren. 4. Q-0004 nach dem ADR-Abschnitt. 5. Eine Warden-Runde über den PR-Stand, zugleich Code-Runde von Q-0002 und Schlussrunde; Verdict per gate verdict. 6. gh pr ready 110, gate merge, Quests clearen, aufräumen nach Standardregel 2. Im Kurzbericht nennen: Owner-Aktionen make env-m2m und make db-roles; Q-0010 listet Getrennte Schlüssel je Zweck noch als eigenes Ziel.

## Offene Entscheidungen
- Noelia-Fehler gefunden und ohne WT-Code reproduziert: noelia.m2m.caller-policy läuft beim Start vor den Routen, der Bericht zeigt für identity Fail, nach dem Start Pass mit 7 Türen. CI ist nicht betroffen, make evidence-check lokal rot für identity. Kein Umweg in WT (bugs/README.md). Der Ticket-Text liegt in .monarch/knowledge/bug-pruefung-der-tueren-laeuft-vor-den-routen.md; bugs/ liegt außerhalb des Scopes von Q-0043, gh darf keinen .monarch-Pfad nennen. Der User muss entscheiden, ob vor dem Merge ein Noelia 7.1.2 nötig ist.
