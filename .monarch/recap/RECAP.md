# RECAP

## Stand
- Zeitpunkt: 2026-11-05T09:25:19Z
- Plan: Pro
- Mana 5h: 12% (Limit 90%, Reset 2026-11-05T12:20:00Z)
- Mana 7d: 38% (Limit 96%, Reset 2026-11-11T11:00:00Z)
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
Warten, bis Noelia mit dem Fix zu Major 1 veröffentlicht ist. Danach: NoeliaVersion und DoD von Q-0043 auf die Fix-Version; ADR-0057 nachziehen (Major 2, Minor 3-7, Datum 29.10. streichen, weil ungeprüft, 48 statt 49 Testdateien); Warden-Delta-Runde; dann quest accept Q-0043 und TDD; danach Q-0002, Q-0004, gate merge. Keine Quest ist akzeptiert.

## Offene Entscheidungen
- Entscheid des Users 05.11.2026: erst Noelia. Der Fix läuft in einer eigenen Noelia-Session, dort entstehen Issue und roter Test; in WT kein bugs-Ticket, kein Noelia-Issue, kein Test, keine Quest. Q-0002 und Q-0004 ruhen, bis Noelia veröffentlicht ist. | Warden-Designrunde ADR-0057, Runde 1: reject, Delta-Runde reicht. Nur Major 1 ist vom Orchestrator im Quelltext nachgeprüft, die übrigen Belege stammen vom Warden. | Major 1: Jeder berechtigte Türaufruf endet in 401. UseTokenRevocation und UseNoeliaPrincipal laufen nach UseAuthorization und weisen die Dienst-Identität ohne sub ab (ServiceFoundation.cs:486-493; Noelia TokenRevocationMiddleware.cs:44,84; PrincipalFactory.cs:23; ServiceTokenIssuer.cs:98-105; Harness endet bei UseNoelia: ServiceCallerHarness.cs:117). Folge: D4 antwortet überall 503, D8 verbrennt Outbox-Versuche. Auflage: Fix in Noelia, DoD von 7.1.0 anheben, kein Pfad-Bypass in WT, UseTokenRevocation bleibt; die Ausnahme bindet an AuthenticationType NoeliaService, nie an fehlendes sub. Laufzeitprobe fehlt. | Major 2: Token-Verdrahtung ist nur für company-site getestet; Stub-Factories umgehen die Handlerkette (NotificationWireTests.cs:196, CompanyKeyTests.cs:250). Ein vergessenes AddServiceCall ergibt 404, das advisor als nichts da liest (HttpLookups.cs:141); ohne UseServiceTokenClient wirft CreateClient außerhalb des try, also 500 statt 503 (HttpCompanyRoles.cs:81). Auflage: ein Test je Dienst über die echte Factory; Türclients tragen ein Token der richtigen Audience, alle anderen keins (company-model, ats, cover-letter, reply-draft, HttpDrafter, und der Client des GitHub-Dienstes). | Minor 3: jobs braucht neben identity-internal auch identity-roles (D4) und identity-company-ai (D7) (CompanyAiAccess.cs:240); Auflage: Tabelle Dienst, Clientname, Audience ins ADR. | Minor 4: k8s-up.sh:113-117 entscheidet nach Existenz des Secrets, eine Release ohne M2M-Schlüssel liefert leere Werte und required bricht ab; Auflage: je Schlüssel prüfen; HELM_PROBE (Makefile:167-168) braucht alle 22 Werte. | Minor 5: Regex in EnvironmentFileTests.cs:66-67 endet auf SECRET, _SECRET_HASH fällt durch den Leer-Wächter; Auflage: Muster erweitern; make env-m2m ergänzt nur ganze Paare und meldet halbe laut. | Minor 6: D8 hängt neu an identity, ein Ausfall über die Tokenlaufzeit verbraucht die zehn Versuche (OutboxDispatcher.cs:20), die Zeile bleibt, die Mail nicht; Auflage: im ADR benennen oder Tokenfehler nicht zählen. | Minor 7: HttpMailer.cs:94 lässt TaskCanceledException durch, D8 antwortet dann 500, der Token-Abruf verlängert die Strecke; Auflage: abfangen; AddServiceCall fängt nur ServiceTokenUnavailableException, nie OperationCanceledException. | Bestätigt vom Warden: D1-D10 stimmen, companies und jobs kommen über AddAdminPermissions zu D4; 15 Identity-Testdateien brauchen alle elf Hashes. | Frühere Entscheidungen: E4 neu (User, 05.11.2026): alle /internal-Türen bekommen Aufruferlisten, das alte Notify-Secret entfällt, assessment wird 11. Client. Die Vorlage liegt im Risk-Tag infra statt secrets (Änderung des Owners, Commit 7456c03a); im PR als Lockerung mit Begründung nennen.
