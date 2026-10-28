# Q-0001 Design: `/internal/*` je Tür eigenes Recht

**Status: überarbeitet nach Design-Review Phase 1; offen: E4, Scope (User)**

Gate G-0001, Rank A, Tags auth/pii, Befund WT-1. Nur Design, kein Code geändert. Pfade relativ zum Repo-Wurzelverzeichnis; `ID` = `src/identity-service/WorkerTransfer.Identity.Api`.

## 1. Inventar der Türen (identity, 7 Türen)

Prüfstelle überall identisch: `MayNotify` (`ID/InternalEndpoints.cs:221-232`, Kopie `ID/NotifyEndpoints.cs:73-84`): Header `X-Notify-Secret` gegen `NotifySettings.Secret` (`Identity.Infrastructure/Mail/NotifySettings.cs`, Env `Notify__Secret`), FixedTimeEquals; leer = zu; falsch = 404. Alle Türen teilen also **ein** Recht.

| # | Methode, Pfad | Quelle | Liefert | Einstufung |
|---|---|---|---|---|
| D1 | POST `/internal/notify` | NotifyEndpoints.cs:38 | 202, nichts; löst Mail an Konto aus | nur Auslösen |
| D2 | GET `/internal/companies/{t}` | InternalEndpoints.cs:101 | Firmenname, bewiesene Domain | Firmendaten, geringes pii (jede ID abfragbar) |
| D3 | GET `/internal/companies/{t}/members` | :29 | Subject-IDs aller Mitglieder | pii (pseudonym, ganze Belegschaft) |
| D4 | GET `/internal/companies/{t}/members/{s}/role` | :64 | Rolle eines Mitglieds | **pii mittel, Orakel**: bei beliebiger ID verrät D4, wer in welcher Firma arbeitet (6 Aufrufer) |
| D5 | GET `/internal/account/{s}/identity` | :141 | Klarname und E-Mail eines beliebigen Kontos | **pii, höchste Stufe** (Kommentar :129) |
| D6 | GET `/internal/account/{s}/ai` | :169 | Provider, Adresse, Modell, **entschlüsselter KI-Schlüssel** der Person (`Settings.cs:193`: `secrets.Decrypt`) | **KI-Schlüssel** |
| D7 | GET `/internal/companies/{t}/ai` | :195 | dasselbe für die Firma (`InternalCompanyAiAccessQuery`) | **KI-Schlüssel** |

Beide Dateien definieren `MayNotify` doppelt; der Scope erlaubt Zusammenlegen.

## 2. Aufrufer je Tür (Grep über `src/**`)

| Tür | Aufrufer (Datei:Zeile) |
|---|---|
| D1 notify | notification `Infrastructure/Mail/HttpMailer.cs:71,82` |
| D2 company | advisor `Lookup/HttpLookups.cs:81`; transfer `Identity/HttpIdentityLookup.cs:49`; jobs `Sources/CompanySite.cs:266-267` |
| D3 members | applications `Companies/HttpCompanyMemberLookup.cs:80`; messages `Companies/HttpCompanyMemberLookup.cs:57-58`; transfer `HttpIdentityLookup.cs:60` |
| D4 role | `src/shared/WorkerTransfer.ServiceDefaults/Roles/HttpCompanyRoles.cs:61,92`, registriert über `AddCompanyPermissions` (`AdminPermissions.cs:134-136`) in companies, advisor, resume, jobs, messages, transfer (je `Api/Program.cs`) |
| D5 identity | advisor `HttpLookups.cs:92`; transfer `HttpIdentityLookup.cs:53` |
| D6 account/ai | profile `Draft/HttpDrafter.cs:200-201`; applications `CoverLetter/HttpAiAccessLookup.cs:78`; messages `Draft/HttpReplyDrafter.cs:304-305` |
| D7 company/ai | `src/shared/WorkerTransfer.ServiceDefaults/Ai/CompanyAiAccess.cs:75,96`, registriert über `AddCompanyAiAccess` in jobs (`JobsInfrastructure.cs:71`), scout (`ScoutInfrastructure.cs:98`), messages (`MessagesInfrastructure.cs:79`) |

**Aufrufer gesamt: 10 Dienste** (notification, advisor, transfer, jobs, applications, messages, companies, resume, profile, scout). Das entspricht den 10 `Identity__Secret`-Zeilen in `docker-compose.yml` (profile 293, resume 311, jobs 363, applications 407, companies 466, transfer 488, notification 504, scout 576, advisor 604, messages 672) plus identity (200) und profile als Prüfer (275) = die „12 Dienste“ des Befunds. Keine Aufrufer von identity: assessment, github, portfolio, consent (assessment hat nur `Notifications__Secret`, das geht an notification-service).

**Tote Türen: keine.** Jede der 7 Türen hat mindestens einen Aufrufer.

**Befund gegen die Quest-Annahme:** Es gibt **zwei** KI-Schlüssel-Türen (D6, D7) mit zusammen **5 verschiedenen Aufrufern** (profile, applications, messages, jobs, scout). „Genau der eine Dienst“ ist mit dem heutigen Code nicht erreichbar, siehe E1 in Abschnitt 8.

## 3. Heutiges Credential

- Ein Wert `WORKERTRANSFER_NOTIFY_SECRET` (compose), `WORKER_NOTIFY_SECRET` (Helm `templates/secret.yaml:34`, `values.yaml:105`), erzeugt in `Makefile:100`.
- Prüfer: identity `Notify__Secret` (compose 200; Helm `_helpers.tpl:151` für identity und profile). Aufrufer präsentieren denselben Wert als `Identity__Secret` (compose; Helm `_helpers.tpl` Block `Identity__BaseUrl`), `Notifications__Secret` (an notification-service, andere Tür), `Profile__Secret` (scout an profile, andere Tür).
- Header einheitlich `X-Notify-Secret` in allen Aufrufern. Keine Aufruferidentität, Rotation nur durch gleichzeitigen Austausch aller Stellen.
- Folge: jeder der 10 Aufrufer kann D5, D6, D7 mit beliebiger ID aufrufen. Die „Ledger-Prüfung vorher“ (`InternalEndpoints.cs:131-137`) ist reine Konvention beim Aufrufer.

## 4. Optionen

**(a) Eigenes Shared Secret je Aufrufer, Rechte je Tür in fester Tabelle im Code.**
Identity hält pro Aufrufer ein Secret. Das präsentierte Secret wird gegen alle Aufrufer-Secrets in konstanter Zeit verglichen (kein früher Abbruch) und ergibt den Aufrufer-Namen; eine statische Tabelle Tür → erlaubte Aufrufer entscheidet, sonst 404. Die Tabelle steht bewusst im Code, nicht in Konfiguration, damit ein Konfigfehler kein Recht verbreitert.
- Aufwand: klein bis mittel. Eine gemeinsame Prüfklasse in identity (ersetzt zwei `MayNotify`), Settings-Klasse, Compose/Helm/Makefile. **Aufrufer-Code bleibt unverändert**, wenn die Wire-Form (`X-Notify-Secret`, `Identity__Secret`) bleibt; nur der Wert je Dienst ist ein anderer.
- Sicherheit: Notify-Secret öffnet nur D1; ein gestohlenes `S_<dienst>` öffnet über `/internal` nur die Türen dieses Dienstes; Aufrufer im Log erkennbar. Das gilt **nur für diese Schicht**: Wer einen Dienst vollständig kompromittiert, hat nach N-3 ohnehin DB-Superuser und Master-Key (Q-0002, Q-0010 offen) und braucht `/internal` nicht. Der Gewinn betrifft gestohlene oder fehlgeleitete Secrets und Fehlgebrauch, nicht die Übernahme eines Dienstes. Bleibt Bearer-Geheimnis ohne Ablauf und ohne Bindung an die Anfrage.
- Betrieb: 10 neue Secrets, Rotation je Aufrufer über den Slot `SecretPrevious` (Abschnitt 6).

**(a-min) Ein Secret je Tür-Gruppe** (notify, D2-D4, D5, D6+D7): nur 4 Secrets, aber D6/D7 bleiben für 5 Dienste offen und der Aufrufer ist nicht erkennbar. Erfüllt „Mail-Dienst liest keine Schlüssel“, nicht „nur Berechtigte“. Verworfen.

**(b) M2M-Token mit Scope.** In Workertransfer nicht vorhanden: Grep nach `client_credentials`, Keycloak, `service account`, `m2m` in `src`, `deploy`, `docker-compose.yml` ergibt keinen M2M-Pfad. Identity stellt eigene JWTs aus (ES256, `Jwt__PrivateKey`), aber nur für Personen; Aufruferseitig wird das Personen-Token weitergereicht (`ICallerToken`, `HttpConsentGate`), kein Dienstkonto. ADR-0045 hält fest, die Plattform sei nicht in Betrieb; Q-0035 (Keycloak-Betriebsform) ist offen. M2M bedeutet: Token-Endpunkt (`client_credentials` je Dienst), Aufrufer-Code in 10 Diensten (außerhalb des Scopes), Audience/Scope-Prüfung, Schlüsselverteilung.
- Aufwand hoch; Sicherheit besser (kurzlebig, Scope, Audience); Betrieb: Token-Handling in jedem Aufrufer, Ausfallverhalten.

**(c) Weitere.**
- (c1) mTLS/NetworkPolicy je Aufrufer: begrenzt das Netz, nicht das Recht je Tür; als Ergänzung in Helm sinnvoll (verwandt mit Q-0003), kein Ersatz. In Compose nicht abbildbar.
- (c2) HMAC-signierte Anfragen (Aufrufer, Methode, Pfad, Zeitstempel): bindet das Credential an die Tür, kein Replay; Aufrufer-Code in 10 Diensten nötig. Zwischenstufe zu (b).
- (c3) KI-Schlüssel nicht mehr herausgeben: ein Dienst führt die KI-Aufrufe aus (Broker). Löst E1 strukturell, ist aber ein Umbau über 5 Dienste und eine eigene Quest.

**Empfehlung: (a) jetzt**, mit Rotationsslot `SecretPrevious` (nur identity), Rechtetabelle im Code und unveränderter Wire-Form; (b) oder (c2) als spätere Quest, sobald Q-0035 entschieden ist. Begründung: schließt WT-1 im Sinne „Notify öffnet nur Notify“ und „fremdes Secret → 404“ ohne Code in den 10 Diensten (außerhalb des Scopes), nutzt die vorhandene Secret-Mechanik (Makefile, Helm `keepOrMake`) und ist rückbaubar. Grenze: es bleiben Bearer-Secrets (Abschnitt 8).

## 5. Zielbild

Credential = aufruferspezifisches Secret `S_<dienst>`, präsentiert im bestehenden Header `X-Notify-Secret` (Name irreführend, später umbenennen). Alle anderen Fälle (kein Header, unbekanntes Secret, bekanntes Secret ohne Recht, leere Konfiguration) = **404**, damit nichts über die Existenz verraten wird.

**404-Vertrag (B4).** Heute hat identity weder Fallback noch StatusCodePages; `GET /internal/notify` liefert 405, D1 liefert 400, weil `NotifyBodyV1` vor der Secret-Prüfung gebunden wird (`NotifyEndpoints.cs:39`). Neu:
- Ein Fallback `/internal/{**rest}` für alle Methoden liefert dasselbe 404 wie eine abgelehnte Tür.
- Die Secret-Prüfung läuft **vor** dem Body-Binding (Endpoint-Filter je Tür); fehlender oder kaputter Body ohne Berechtigung ergibt 404, nie 400.
- Referenz ist die Antwort „ohne Header“ auf dieselbe Tür; alle anderen Ablehnungen sind byte-gleich bis auf die `correlationId`.

| Tür | Berechtigte Aufrufer | Credential |
|---|---|---|
| D1 notify | notification | `S_notification` |
| D2 company | advisor, transfer, jobs | je eigenes `S_<dienst>` |
| D3 members | applications, messages, transfer | je eigenes `S_<dienst>` |
| D4 role | companies, advisor, resume, jobs, messages, transfer | je eigenes `S_<dienst>` |
| D5 identity (pii) | advisor, transfer | je eigenes `S_<dienst>` |
| D6 account/ai (KI-Schlüssel) | profile, applications, messages (siehe E1) | je eigenes `S_<dienst>` |
| D7 company/ai (KI-Schlüssel) | jobs, scout, messages (siehe E1) | je eigenes `S_<dienst>` |

Folge: `S_notification` öffnet nur D1. scout, resume, companies sehen D5/D6 nicht; profile und applications sehen D7 nicht. Die KI-Schlüssel-Türen haben nicht „genau einen“ Aufrufer, sondern die kleinste heute belegte Menge (E1).

**Ledger-Prüfung in identity (DoD 4):** D5 und die Schlüssel-Türen sollen die Einwilligung selbst prüfen. Vorschlag: identity erhält einen Consent-Client (heute **kein** Consent-Bezug in `src/identity-service`, Grep leer) und fragt synchron ohne Cache (ADR-0013) die Fähigkeit `advisor.identity:tenant:<id>` bzw. `employer.route:tenant:<id>` ab. Dafür braucht die Tür den Tenant als Parameter und den Aufrufer-Token (`ICallerToken`). Das ändert advisor und transfer (außerhalb des Scopes), siehe E2.

## 6. Konfiguration

Neu in identity: `Internal__Callers__<name>__Secret` und `__SecretPrevious` (nur identity, optional, Default leer; Rotationsslot), nach der Konvention „Property-Name = Env-Key“ (`NotifySettings.cs:16`).
- `docker-compose.yml`: neue Variablen `WORKERTRANSFER_INTERNAL_SECRET_<DIENST>` (10 Stück, `:?missing` wie bisher). Identity bekommt alle 10; jeder Aufrufer bekommt **nur seinen** als `Identity__Secret` (statt der gemeinsamen Variable). `Notify__Secret` am identity entfällt (notification ist normaler Aufrufer); am profile-service bleibt `Notify__Secret` (dessen eigene Tür, Grenzen).
- `deploy/helm/workertransfer`: `values.yaml` `secrets.internalSecrets.<dienst>`; `templates/secret.yaml` Schlüssel `WORKER_INTERNAL_SECRET_<DIENST>` über `keepOrMake` (nur Hauptschlüssel, nie `SecretPrevious`); `_helpers.tpl`: `Identity__Secret` je `$svc.name` aus dem eigenen Schlüssel, identity erhält alle.
- `Makefile:100` und `.env.example` müssen die neuen Variablen erzeugen bzw. führen; `tests/WorkerTransfer.CrossCutting.Tests/EnvironmentFileTests.cs:86` listet die erwarteten Schlüssel. **Alle drei liegen außerhalb des Scopes** (E3).
- Übergang: ADR-0045 hält fest, die Plattform sei nicht in Betrieb (Annahme verfällt mit dem ersten echten Konto). Daher Hartschnitt in einem PR ohne Altpfad. Falls doch Betrieb: erst identity mit dem Altwert als `SecretPrevious` aller Aufrufer ausrollen, dann Aufrufer umstellen, dann `SecretPrevious` leeren.
- **Rotation (B2).** Heute lesen Aufrufer und der identity-Slot `Secret` dieselbe Variable; ein Wechsel öffnet ein 404-Fenster. Neu: ein Slot `Internal__Callers__<name>__SecretPrevious`, nur bei identity, optional, Default leer. Ablauf: im selben Deploy `SecretPrevious` := alter Wert und `Secret` := neuer Wert, dann Rollout-Restart, dann `SecretPrevious` leeren. Identity akzeptiert dazwischen beide Werte; ein leerer Slot ist nie ein Treffer.
- **Helm-Besonderheiten.** `keepOrMake` (`_helpers.tpl:36-44`) liefert bei vorhandenem Schlüssel immer den alten Wert und kann einen Slot nie leeren; `SecretPrevious` kommt deshalb direkt aus `values` (leer = nicht gesetzt), nicht über `keepOrMake`. Eine Secret-Änderung startet keine Pods neu (`services.yaml:46`: nur checksum/env); der Rollout-Restart ist ein expliziter Schritt (`kubectl rollout restart`) und gehört ins Runbook.
- Migrationen: **keine** (keine Datenbankänderung); Migrations-Pfade sind ausgeschlossen.

## 7. Testplan

Projekt: `tests/WorkerTransfer.Identity.Tests` (existiert; `WebApplicationFactory<Program>` mit Postgres-Fixture, Vorbild `CompanyAiTests.cs:25-60`, `SettingsTests.cs:279-351`, `NotificationTests.cs:41-83`). Neue Datei `InternalDoorsTests.cs`. Die bestehenden Tests, die `Notify:Secret` setzen (`InternalMembersTests`, `CompanyAiTests`, `SettingsTests`, `NotificationTests`, `AccountLanguageTests`), werden auf aufruferspezifische Secrets umgestellt.

Aufbau: Host mit `Internal:Callers:<name>:Secret` für alle 10 Aufrufer mit unterscheidbaren Werten (`secret-notification`, `secret-jobs`, …). Hilfsmethode `As(caller)` setzt `X-Notify-Secret`.

- **Volle Matrix (Theory):** 7 Türen × 10 Aufrufer plus „unbekannter Aufrufer“; laut Tabelle erlaubt → 200/202, sonst 404. Zusätzlich je Tür: kein Header, leeres Secret. Erwartete Daten je Tür mindestens einmal geprüft (D4 liefert bei Nichtmitglied weiterhin `200 null`).
- **404-Vertrag:** falsche Methode (z. B. `GET /internal/notify`) und fehlender oder kaputter Body auf D1 ohne Berechtigung → 404, byte-gleich zur Antwort „ohne Header“ bis auf die `correlationId`; unbekannter Pfad unter `/internal/` ebenso.
- **Konsistenztest gegen compose** (in Identity.Tests, liest `docker-compose.yml`): jeder Aufrufer bekommt genau seine eigene Variable als `Identity__Secret`, keine Variable doppelt, kein `Identity__Secret` zeigt auf NOTIFY.
- **Boss `InternalEndpoints_NotifySecret_CannotReadIdentityOrAiKey`:** Konto mit hinterlegtem KI-Schlüssel und Firma mit Firmenschlüssel anlegen (Konstante wie `CompanyAiTests.cs:26`); `Notify:Secret` auf den **veralteten** Wert setzen und damit sowie mit `secret-notification` D5, D6, D7 aufrufen; erwartet 404, im Body weder Name, E-Mail noch Schlüssel. Gegenproben: `secret-advisor` auf D5 → 200, `secret-profile` auf D6 → 200, `secret-notification` auf D1 → 202. Der Test ist gegen den heutigen Code rot (TDD).
- **Wächter-Test, beide Richtungen:** Route → Tabelle (jede Route unter `/internal/` über `EndpointDataSource` steht in der Rechtetabelle) und Tabelle → Route (jeder Eintrag hat eine reale Route).
- **Rotation:** `SecretPrevious` und `Secret` gleichzeitig gültig; leere Slots öffnen nichts; zwei gleiche nicht-leere Werte → kein Start.
- **Fail-open in den Aufrufern (Grenze, Follow-up F1):** Mehrere Aufrufer werten ein 404 still als „nichts da“. D6: profile `HttpDrafter.cs:217-219`, applications `HttpAiAccessLookup.cs:98-100`, messages `HttpReplyDrafter.cs:322-324` fallen auf das Plattformmodell zurück. D2: advisor-Ausschluss `HttpLookups.cs:141-146`, `ConversationCommands.cs:115-117`. Ein falsches oder rotiertes Secret fällt dort nicht auf, sondern ändert still das Verhalten. Aufrufer-Code liegt außerhalb des Scopes; **F1**: 404 von identity in diesen Aufrufern als Fehler behandeln. Der Testplan prüft nur identity-seitig.
- Ledger-Prüfung: Q-0044, nicht Teil dieser Quest.

## Auflagen Warden (Phase 1)

1. Die Prüfung läuft am Endpunkt (Metadaten oder Filter je Tür), nie über ein Pfadpräfix.
2. SHA-256 über präsentierten Wert und alle Slots, `FixedTimeEquals` auf 32 Byte, alle Slots ohne frühen Abbruch, genau ein Headerwert; ein leerer Slot oder Header ist nie ein Treffer.
3. Beim Start prüfen: Aufrufernamen nur aus der Codetabelle, als feste Properties; alle nicht-leeren Werte paarweise verschieden, sonst kein Start.
4. Im Log nur Aufrufer und Tür, nie den Wert, ein Präfix oder einen Hash.
5. Hermetische Tests: alle 10 Slots setzen. Der Boss-Test setzt `Notify:Secret` auf den veralteten Wert und erwartet 404 auf D5/D6/D7 ohne Name, E-Mail oder Schlüssel im Body. Der Wächter-Test prüft beide Richtungen (Route ↔ Tabelle).
6. Helm: Schlüssel je `$svc.name` mit `required`, `existingSecret` dokumentiert, ein Render-Test „kein Pod trägt fremde Schlüssel“, die CrossCutting-Suite läuft mit.

## 8. Offene Fragen, Annahmen, Grenzen

**Entscheidungen für den User (nicht getroffen):**
- **E1 „genau ein Dienst“ für KI-Schlüssel:** heute 5 Aufrufer über D6/D7. Optionen: (i) Aufrufermenge je Tür wie in Abschnitt 5 (Empfehlung für diese Quest, DoD-Wortlaut anpassen); (ii) (c3) Broker als eigene Quest, die Quest bleibt bei (i); (iii) Aufrufe zusammenlegen (Produktentscheidung).
- **E2 Ledger-Prüfung in identity:** erfordert Consent-Client in identity und Änderung von advisor und transfer (Tenant-Parameter, Token-Weitergabe), außerhalb des Scopes. Entweder Scope erweitern oder den DoD-Punkt in eine Folge-Quest verschieben.
- **E3 Scope-Erweiterung:** `Makefile`, `.env.example`, `tests/WorkerTransfer.CrossCutting.Tests/EnvironmentFileTests.cs`, ggf. ADR unter `docs/adr`. Ohne sie bricht `make env` bzw. der Env-Test.

- **E4 (offen, User): notification-service hat eine Doppelrolle (B1).** Seine eigene Tür `/internal/notifications` prüft gegen `MailSettings.Secret` = `Identity__Secret` (`notification-service/…/Api/NotificationEndpoints.cs:50,194`, `…/Mail/HttpMailer.cs:18,24`). Mit `S_notification` bekämen die 7 Sender dort 404. Optionen: (a) Scope um notification-service erweitern, mit eigenem eingehenden Secret für diese Tür; Änderung an einem weiteren Dienst, Compose/Helm um ein Secret größer. (b) Als Grenze festhalten: notification-service bleibt unverändert, seine Tür bleibt bei einem Wert, den die Sender kennen, und der Punkt steht offen im PR; kein Zusatzaufwand, aber `S_notification` kann so nicht zugleich der eigene Aufrufer-Wert für D1 sein, ohne dass die 7 Sender ihn kennen.
- **Scope (offen, User; B5).** Zusätzlich benötigt: `IdentityInfrastructure.cs:54`, `NotifySettings.cs` und eventuell neue Dateien. Die DoD-Zeilen zu „ein Dienst“ und zur Ledger-Prüfung sind durch E1/E2 überholt. Festhalten: **Q-0043 vor oder mit dem Q-0001-Code**, sonst bricht `:?missing` `make up` und CI (`.github/workflows/ci.yml:139,264`).

**Annahmen:** Plattform nicht live (ADR-0045), daher Hartschnitt; Wire-Header `X-Notify-Secret` bleibt vorerst; Aufrufer-Code der Dienste wird nicht angefasst; Grep deckt nur `src/**` ab, Aufrufer außerhalb des Repos sind unbekannt; nicht geprüft, ob die Consent-API Aufrufe ohne Personen-Token zulässt.

**Grenzen (nicht gelöst):**
- Secrets bleiben unbefristete Bearer-Geheimnisse; wer einen Dienst vollständig übernimmt, hat dessen Türen. Bindung an die Anfrage, Ablauf und Audience erst mit (b)/(c2).
- Ein berechtigter Aufrufer kann jede ID abfragen, nicht nur bei D5/D6, sondern auch bei D3, D4 (verrät, wer wo arbeitet, 6 Aufrufer) und D7: „nur Konten mit Einwilligung“ ist erst mit Q-0044 durchgesetzt, und D3/D4/D7 auch dort nur teilweise.
- Die Rechte je Tür begrenzen gestohlene Secrets, nicht die Übernahme eines Dienstes (DB-Superuser und Master-Key je Dienst bis Q-0002/Q-0010).
- Fail-open in Aufrufern bei 404 (F1, Abschnitt 7).
- Andere Secret-Türen bleiben: profile-service `/internal` (`Notify__Secret`, `src/profile-service/WorkerTransfer.Profile.Api/InternalEndpoints.cs:134`, scout als Aufrufer), notification-service-Türen (`NotificationEndpoints.cs:21`, 7 Sender mit `Notifications__Secret`), Evidence- und Erasure-Türen (Q-0004 u. a.).
- Netz-Isolation (Q-0003) und Master-Key-Trennung (Q-0002, Q-0010) sind nicht Teil dieses Designs.
- Der Header-Name `X-Notify-Secret` bleibt irreführend, bis die Aufrufer angefasst werden dürfen.

## 9. Entscheidungen des Users (28.10.2026, 20:45)
- **E1 → Aufrufer je Tür.** D6/D7 lassen nur die heute belegten Aufrufer durch (Abschnitt 5); alle anderen, insbesondere notification, erhalten 404. Damit ist das Ziel der Quest erfüllt (ein Mail-Dienst liest keine Schlüssel). „Genau ein Dienst“ für KI-Schlüssel ist Folge-Quest **Q-0045** (KI-Broker).
- **E2 → eigene Folge-Quest.** Die Ledger-Prüfung in identity (Consent-Client, Änderungen in advisor und transfer) ist **Q-0044**. Q-0001 schließt ohne sie; die Lücke steht offen im PR (Grenze: berechtigte Aufrufer können jede Subject-ID abfragen).
- **E3 → Begleit-Quest Q-0043** (Rank A, Scope `Makefile`, `.env.example`, `tests/WorkerTransfer.CrossCutting.Tests/EnvironmentFileTests.cs`), damit `make env` und der Env-Test mit den Secrets je Aufrufer weiter funktionieren.

Status danach: überarbeitet nach Design-Review Phase 1; E1–E3 entschieden, E4 und Scope offen (User).

## 10. Entscheidungen nach Design-Review Phase 1 (projects-74 im Auftrag des Users, 28.10.2026, 21:20)
- **E4 → (a), eng gefasst (B1 bestätigt):** `MayNotify` in notification (`NotificationEndpoints.cs:194ff`) prüft `MailSettings.Secret` = `Identity__Secret`, also denselben Wert, den `HttpMailer` an identity sendet. notification bekommt ein **eigenes eingehendes Setting**; die 7 Sender bleiben unverändert und senden weiter ihren bisherigen Wert. `Identity__Secret` von notification wird `S_notification`. Geändert werden nur Settings und Bindung in notification sowie Compose/Helm. Tests: die 7 Sender erhalten mit ihrem Wert weiter 202; identity lehnt den alten gemeinsamen Wert an **allen** Türen mit 404 ab; `S_notification` öffnet nur D1.
- **Scope (B5) → Ersatz-Quest:** Q-0001 ist abgebrochen (Scope aus Backlog-Import unvollständig, Design-Review B1/B5, Bezug Monarch #16). Umsetzung in **Q-0043** (Scope: bisheriger Q-0001-Scope + `IdentityInfrastructure.cs`, `Mail/NotifySettings.cs`, neuer Ordner `Api/Internal/**` unter identity, notification Settings/Bindung, `Makefile`, `.env.example`, `EnvironmentFileTests.cs`, `docs/adr/**`). Die geplante Begleit-Quest entfällt. Folge-Quests: **Q-0044** Einwilligungsprüfung in identity, **Q-0045** KI-Broker.

Status: überarbeitet nach Design-Review Phase 1; E4 und Scope entschieden; Delta-Review (zweite und letzte Design-Runde) ausstehend.

## 11. Korrekturen nach Delta-Review (Warden, 28.10.2026) – ersetzen ältere Stellen
- **Ersetzt §5 (Mechanismus vor Body-Binding):** In Minimal APIs laufen Endpoint-Filter erst nach der Bindung. Deshalb: **D1 hat keinen gebundenen Body-Parameter**; der Body wird erst nach der Secret-Prüfung gelesen. Endpoint-Filter je Tür nur für die GET-Türen D2–D7. Der 404-Vertragstest (falsche Methode, kaputter/fehlender Body) entscheidet.
- **Ersetzt §6 (Übergang „Altwert als SecretPrevious aller Aufrufer“):** entfällt (verstieße gegen Auflage 3 und ergäbe die Vereinigung aller Rechte). Bei Betrieb: **Hartschnitt im Wartungsfenster**. Die Plattform ist laut ADR-0045 nicht live.
- **Überholt durch §10:** Statuszeile oben, §6 „Alle drei außerhalb des Scopes“, §8 „E4/Scope offen“, §9 „E3 → Begleit-Quest Q-0043“ (Q-0043 ist jetzt die Ersatz-Quest mit vollem Scope).
- **Scope-Hinweise:** identity `Program.cs` liegt nicht im Scope → der Fallback `/internal/{**rest}` liegt in `MapInternalEndpoints`. Helm läuft in CI nur im images-Job (`ci.yml:393-397`, `make k8s-lint`) → die Render-Prüfung gehört ins Makefile, nicht in einen .NET-Test.
- **Follow-up F1 (ohne Quest):** advisor-Ausschluss bei D2 bleibt fail-open (`HttpLookups.cs:141-146`); weder Q-0044 noch Q-0045 decken es ab → eigene Quest nach Q-0043.

### Auflagen für den Code (Ergänzung zu 1–6)
7. Das eingehende Setting in notification hat **keinen Rückfall** auf `Identity__Secret`. Tests: `Identity:Secret`-Wert an `/internal/notifications` → 404, eingehender Wert → 202, leeres eingehendes Setting → 404.
8. Der Konsistenztest gegen compose prüft zusätzlich: Der Eingang von notification nutzt dieselbe Variable wie die 7 `Notifications__Secret`, nicht die eigene `Identity__Secret`. Keine `WORKERTRANSFER_INTERNAL_SECRET_*` kommt doppelt vor.
9. Helm: `SecretPrevious` ist ein optionaler Schlüssel im Secret-Objekt (aus `values`, ohne keepOrMake, `secretKeyRef optional: true`), nie ein Klartext-`value:`. Die Prüfung „kein Pod trägt fremde Schlüssel“ läuft in `make k8s-lint`.
10. Fallback `/internal/{**rest}` in `MapInternalEndpoints`; der Wächter-Test nimmt den Fallback ausdrücklich aus.
11. Feste Properties unter `Internal:Callers` in identity, damit ComposeKeyTests unverändert grün bleibt; die CrossCutting-Suite läuft mit.

Status: Design nach Delta-Review vollständig; **Freigabe durch den User selbst ausstehend** (Warden-Auflage: §10 stammt von einem Agenten).

## 12. Umsetzung über Noelia 7.1 (Entscheid projects-74 mit dem User, 28.10.2026 ~22:00)
Umsetzung über **Noelia 7.1 (M2M + Rechte je Tür)**: Ausstellen von Service-Tokens und Empfänger-Policy „nur Aufrufer X, Y“ je Endpoint kommen als geteilte Infrastruktur aus Noelia. **Q-0043 wird nach dem Release als Übernahme neu zugeschnitten** (Design-Delta, Warden-Delta, Freigabe durch den User, TDD). Inventar, Rechtetabelle, E1/E4 und Testplan dieser Notiz bleiben gültig. Q-0043 wird bis dahin nicht gestartet.
