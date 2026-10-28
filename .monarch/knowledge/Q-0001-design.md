# Q-0001 Design: `/internal/*` je Tür eigenes Recht

**Status: vorgeschlagen (Design-Review Warden ausstehend)**

Gate G-0001, Rank A, Tags auth/pii, Befund WT-1. Nur Design, kein Code geändert. Pfade relativ zum Repo-Wurzelverzeichnis; `ID` = `src/identity-service/WorkerTransfer.Identity.Api`.

## 1. Inventar der Türen (identity, 7 Türen)

Prüfstelle überall identisch: `MayNotify` (`ID/InternalEndpoints.cs:221-232`, Kopie `ID/NotifyEndpoints.cs:73-84`): Header `X-Notify-Secret` gegen `NotifySettings.Secret` (`Identity.Infrastructure/Mail/NotifySettings.cs`, Env `Notify__Secret`), FixedTimeEquals; leer = zu; falsch = 404. Alle Türen teilen also **ein** Recht.

| # | Methode, Pfad | Quelle | Liefert | Einstufung |
|---|---|---|---|---|
| D1 | POST `/internal/notify` | NotifyEndpoints.cs:38 | 202, nichts; löst Mail an Konto aus | nur Auslösen |
| D2 | GET `/internal/companies/{t}` | InternalEndpoints.cs:101 | Firmenname, bewiesene Domain | Firmendaten, geringes pii |
| D3 | GET `/internal/companies/{t}/members` | :29 | Subject-IDs aller Mitglieder | pii (pseudonym, ganze Belegschaft) |
| D4 | GET `/internal/companies/{t}/members/{s}/role` | :64 | Rolle eines Mitglieds | pii (gering), Berechtigungsfrage |
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
- Sicherheit: Notify-Secret öffnet nur D1; Kompromittierung eines Dienstes öffnet nur dessen Türen; Aufrufer im Log erkennbar. Bleibt Bearer-Geheimnis ohne Ablauf und ohne Bindung an die Anfrage.
- Betrieb: 10 neue Secrets, Rotation je Aufrufer (zwei Slots, Abschnitt 6).

**(a-min) Ein Secret je Tür-Gruppe** (notify, D2-D4, D5, D6+D7): nur 4 Secrets, aber D6/D7 bleiben für 5 Dienste offen und der Aufrufer ist nicht erkennbar. Erfüllt „Mail-Dienst liest keine Schlüssel“, nicht „nur Berechtigte“. Verworfen.

**(b) M2M-Token mit Scope.** In Workertransfer nicht vorhanden: Grep nach `client_credentials`, Keycloak, `service account`, `m2m` in `src`, `deploy`, `docker-compose.yml` ergibt keinen M2M-Pfad. Identity stellt eigene JWTs aus (ES256, `Jwt__PrivateKey`), aber nur für Personen; Aufruferseitig wird das Personen-Token weitergereicht (`ICallerToken`, `HttpConsentGate`), kein Dienstkonto. ADR-0045 hält fest, die Plattform sei nicht in Betrieb; Q-0035 (Keycloak-Betriebsform) ist offen. M2M bedeutet: Token-Endpunkt (`client_credentials` je Dienst), Aufrufer-Code in 10 Diensten (außerhalb des Scopes), Audience/Scope-Prüfung, Schlüsselverteilung.
- Aufwand hoch; Sicherheit besser (kurzlebig, Scope, Audience); Betrieb: Token-Handling in jedem Aufrufer, Ausfallverhalten.

**(c) Weitere.**
- (c1) mTLS/NetworkPolicy je Aufrufer: begrenzt das Netz, nicht das Recht je Tür; als Ergänzung in Helm sinnvoll (verwandt mit Q-0003), kein Ersatz. In Compose nicht abbildbar.
- (c2) HMAC-signierte Anfragen (Aufrufer, Methode, Pfad, Zeitstempel): bindet das Credential an die Tür, kein Replay; Aufrufer-Code in 10 Diensten nötig. Zwischenstufe zu (b).
- (c3) KI-Schlüssel nicht mehr herausgeben: ein Dienst führt die KI-Aufrufe aus (Broker). Löst E1 strukturell, ist aber ein Umbau über 5 Dienste und eine eigene Quest.

**Empfehlung: (a) jetzt**, mit zwei Slots je Aufrufer-Secret, Rechtetabelle im Code und unveränderter Wire-Form; (b) oder (c2) als spätere Quest, sobald Q-0035 entschieden ist. Begründung: schließt WT-1 im Sinne „Notify öffnet nur Notify“ und „fremdes Secret → 404“ ohne Code in den 10 Diensten (außerhalb des Scopes), nutzt die vorhandene Secret-Mechanik (Makefile, Helm `keepOrMake`) und ist rückbaubar. Grenze: es bleiben Bearer-Secrets (Abschnitt 8).

## 5. Zielbild

Credential = aufruferspezifisches Secret `S_<dienst>`, präsentiert im bestehenden Header `X-Notify-Secret` (Name irreführend, später umbenennen). Alle anderen Fälle (kein Header, unbekanntes Secret, bekanntes Secret ohne Recht, leere Konfiguration) = **404** mit demselben ProblemDetails wie heute, damit nichts über die Existenz verraten wird.

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

Neu in identity: `Internal__Callers__<name>__Secret` und `__SecretNext` (zwei Slots für Rotation), nach der Konvention „Property-Name = Env-Key“ (`NotifySettings.cs:16`).
- `docker-compose.yml`: neue Variablen `WORKERTRANSFER_INTERNAL_SECRET_<DIENST>` (10 Stück, `:?missing` wie bisher). Identity bekommt alle 10; jeder Aufrufer bekommt **nur seinen** als `Identity__Secret` (statt der gemeinsamen Variable). `Notify__Secret` am identity entfällt (notification ist normaler Aufrufer); am profile-service bleibt `Notify__Secret` (dessen eigene Tür, Grenzen).
- `deploy/helm/workertransfer`: `values.yaml` `secrets.internalSecrets.<dienst>`; `templates/secret.yaml` Schlüssel `WORKER_INTERNAL_SECRET_<DIENST>` über `keepOrMake`; `_helpers.tpl`: `Identity__Secret` je `$svc.name` aus dem eigenen Schlüssel, identity erhält alle.
- `Makefile:100` und `.env.example` müssen die neuen Variablen erzeugen bzw. führen; `tests/WorkerTransfer.CrossCutting.Tests/EnvironmentFileTests.cs:86` listet die erwarteten Schlüssel. **Alle drei liegen außerhalb des Scopes** (E3).
- Übergang: ADR-0045 hält fest, die Plattform sei nicht in Betrieb (Annahme verfällt mit dem ersten echten Konto). Daher Hartschnitt in einem PR ohne Altpfad. Falls doch Betrieb: erst identity mit dem Altwert als `SecretNext` aller Aufrufer ausrollen, dann Aufrufer umstellen, dann Altwert entfernen.
- Rotation: neues Secret in `SecretNext`, Aufrufer umstellen, dann `Secret` := neuer Wert, `SecretNext` leeren. Identity akzeptiert beide Slots. Ein leerer Slot ist nie ein Treffer.
- Migrationen: **keine** (keine Datenbankänderung); Migrations-Pfade sind ausgeschlossen.

## 7. Testplan

Projekt: `tests/WorkerTransfer.Identity.Tests` (existiert; `WebApplicationFactory<Program>` mit Postgres-Fixture, Vorbild `CompanyAiTests.cs:25-60`, `SettingsTests.cs:279-351`, `NotificationTests.cs:41-83`). Neue Datei `InternalDoorsTests.cs`. Die bestehenden Tests, die `Notify:Secret` setzen (`InternalMembersTests`, `CompanyAiTests`, `SettingsTests`, `NotificationTests`, `AccountLanguageTests`), werden auf aufruferspezifische Secrets umgestellt.

Aufbau: Host mit `Internal:Callers:<name>:Secret` für alle 10 Aufrufer mit unterscheidbaren Werten (`secret-notification`, `secret-jobs`, …). Hilfsmethode `As(caller)` setzt `X-Notify-Secret`.

- **Negativ je Tür (7):** Theorie Türen × (kein Header, unbekanntes Secret, Secret eines nicht berechtigten Aufrufers, leeres Secret) erwartet 404 mit demselben Body wie bei unbekanntem Pfad.
- **Positiv je Tür (7):** je ein berechtigter Aufrufer erhält 200 bzw. 202 mit den erwarteten Daten (D4 liefert bei Nichtmitglied weiterhin `200 null`).
- **Boss `InternalEndpoints_NotifySecret_CannotReadIdentityOrAiKey`:** Konto mit hinterlegtem KI-Schlüssel und Firma mit Firmenschlüssel anlegen (Konstante wie `CompanyAiTests.cs:26`); mit `secret-notification` und zusätzlich mit dem alten `Notify:Secret`-Wert D5, D6, D7 aufrufen; erwartet 404, und der Body enthält weder Name, E-Mail noch Schlüssel. Gegenproben: `secret-advisor` auf D5 → 200, `secret-profile` auf D6 → 200, `secret-notification` auf D1 → 202. Der Test ist gegen den heutigen Code rot (TDD).
- **Wächter-Test:** über `EndpointDataSource` alle Routen mit Präfix `/internal/` enumerieren; jede muss in der Rechtetabelle stehen, sonst rot (verhindert neue ungeschützte Türen).
- **Rotation:** `SecretNext` wird akzeptiert, beide Slots gleichzeitig; leere Slots öffnen nichts.
- Ledger-Prüfung: Test erst nach Entscheidung E2.

## 8. Offene Fragen, Annahmen, Grenzen

**Entscheidungen für den User (nicht getroffen):**
- **E1 „genau ein Dienst“ für KI-Schlüssel:** heute 5 Aufrufer über D6/D7. Optionen: (i) Aufrufermenge je Tür wie in Abschnitt 5 (Empfehlung für diese Quest, DoD-Wortlaut anpassen); (ii) (c3) Broker als eigene Quest, die Quest bleibt bei (i); (iii) Aufrufe zusammenlegen (Produktentscheidung).
- **E2 Ledger-Prüfung in identity:** erfordert Consent-Client in identity und Änderung von advisor und transfer (Tenant-Parameter, Token-Weitergabe), außerhalb des Scopes. Entweder Scope erweitern oder den DoD-Punkt in eine Folge-Quest verschieben.
- **E3 Scope-Erweiterung:** `Makefile`, `.env.example`, `tests/WorkerTransfer.CrossCutting.Tests/EnvironmentFileTests.cs`, ggf. ADR unter `docs/adr`. Ohne sie bricht `make env` bzw. der Env-Test.

**Annahmen:** Plattform nicht live (ADR-0045), daher Hartschnitt; Wire-Header `X-Notify-Secret` bleibt vorerst; Aufrufer-Code der Dienste wird nicht angefasst; Grep deckt nur `src/**` ab, Aufrufer außerhalb des Repos sind unbekannt; nicht geprüft, ob die Consent-API Aufrufe ohne Personen-Token zulässt.

**Grenzen (nicht gelöst):**
- Secrets bleiben unbefristete Bearer-Geheimnisse; wer einen Dienst vollständig übernimmt, hat dessen Türen. Bindung an die Anfrage, Ablauf und Audience erst mit (b)/(c2).
- Ein berechtigter Aufrufer kann jede Subject-ID abfragen (D5, D6): „nur Konten mit Einwilligung“ ist erst mit E2 durchgesetzt.
- Andere Secret-Türen bleiben: profile-service `/internal` (`Notify__Secret`, `src/profile-service/WorkerTransfer.Profile.Api/InternalEndpoints.cs:134`, scout als Aufrufer), notification-service-Türen (`NotificationEndpoints.cs:21`, 7 Sender mit `Notifications__Secret`), Evidence- und Erasure-Türen (Q-0004 u. a.).
- Netz-Isolation (Q-0003) und Master-Key-Trennung (Q-0002, Q-0010) sind nicht Teil dieses Designs.
- Der Header-Name `X-Notify-Secret` bleibt irreführend, bis die Aufrufer angefasst werden dürfen.
