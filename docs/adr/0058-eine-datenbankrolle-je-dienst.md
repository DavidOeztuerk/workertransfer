# ADR-0058: Eine Datenbankrolle je Dienst, und der Hauptschlüssel nur bei identity

**Status:** angenommen für `develop` (05.11.2026), Fassung 2. Warden-Runde 1 approve-with-conditions, die Auflagen sind eingearbeitet. Zwei Punkte hat der User am 05.11.2026 entschieden: Der Superuser verliert sein Netz-Login, und der Schlüsselbund wird einmalig geleert.
**Betrifft:** alle fünfzehn Dienste, `src/shared/WorkerTransfer.ServiceDefaults`, `scripts/initdb`, Compose, Helm, `make env`, CI
**Verwandt:** ADR-0004 (eine Datenbank je Dienst), ADR-0044 (Nachweis), ADR-0045 (Plattform nicht live), ADR-0046 (Schlüsselbund in Postgres), ADR-0053 (fremde Anmeldung), ADR-0057 (Türen mit Aufruferliste), Gate G-0001, Quests Q-0002 und Q-0004, Folge-Quests Q-0010 (Versionierung und Rotation des Hauptschlüssels) und Q-0045 (KI-Broker)

## In einfacher Sprache

Heute melden sich alle fünfzehn Dienste mit demselben Namen und demselben Passwort bei der Datenbank an, und dieser Name ist der Administrator. Wer einen einzigen Dienst übernimmt, liest damit die Datenbanken aller anderen. Dazu trägt jeder Dienst den Hauptschlüssel, mit dem die KI-Schlüssel der Menschen verschlüsselt sind.

Künftig hat jeder Dienst eine eigene Anmeldung, die nur in seine eigene Datenbank führt, und den Hauptschlüssel trägt nur noch identity. Der Administrator kommt nur noch im Postgres-Container hinein, über das Netz gar nicht mehr. Für Nutzer ändert sich nichts, und gespeicherte KI-Schlüssel bleiben lesbar. Wer den Stack schon einmal gestartet hat, ruft einmal `make db-roles` auf. Das stellt die vorhandenen Daten um und löscht keine davon, bis auf den internen Schlüsselbund, den die Dienste von selbst neu anlegen. Ein Datenbankwerkzeug auf dem Mac meldet sich danach mit der Rolle eines Dienstes an.

## Warum es dieses ADR gibt

Gemessen am 05.11.2026 in `docker-compose.yml`: Alle fünfzehn Verbindungszeichenfolgen melden sich als `worker` mit dem Passwort `worker` an, und `worker` ist `POSTGRES_USER`, also der Superuser des Servers. Dieselben Werte stehen in acht `appsettings.json`, und Helm legt in `values.yaml` `postgresPassword: worker` fest. `WORKERTRANSFER_SECRETS_KEY` steht im Anker `x-environment` und in `_helpers.tpl` an jedem Dienst. Gebraucht wird er als `SecretStore` nur in identity, die übrigen vierzehn verschlüsseln damit ausschließlich ihren eigenen Schlüsselbund (`KeyRing.cs`). Das sind die Befunde N-3, WT-5 und WT-10.

Die Kette ist kurz: Ein übernommener Dienst liest über `worker` die Spalte mit den verschlüsselten KI-Schlüsseln in der Datenbank `identity` und entschlüsselt sie mit dem Hauptschlüssel aus seiner eigenen Umgebung. ADR-0057 begrenzt gestohlene Credentials an den Türen und sagt selbst, dass das bei der Übernahme eines Dienstes nicht hilft. Dieses ADR schließt den Weg über die Datenbank. Der Weg über die Türen D6 und D7 bleibt für die fünf dort genannten Dienste offen, bis Q-0045 ihn schließt (Entscheidung E1 vom 28.10.2026).

Messungen an Wegwerf-Containern mit `postgres:17-alpine` (17.11, derselbe Stand wie Compose und Helm) haben das Design bestimmt. Der Warden hat sie nachgestellt und ergänzt.

Eine eigene Rolle allein reicht nicht. Eine frisch angelegte Rolle kam ohne weiteres Zutun in die Datenbank `identity` hinein, nur die Tabelle verweigerte ihr den Zugriff, denn `CONNECT` steht in Postgres von Haus aus `PUBLIC` offen. Erst nach `REVOKE CONNECT, TEMPORARY ON DATABASE identity FROM PUBLIC` lautet die Antwort `FATAL: permission denied for database "identity"`. Auf die Datenbank `postgres` kam dieselbe Rolle danach weiterhin, solange dort niemand widerruft.

Rollen allein reichen auch deshalb nicht, weil `worker` mit dem Passwort `worker` nach Rollen, Eigentümerwechsel und allen `REVOKE` weiter über das Netz hineinkam, auch über die veröffentlichte Portbindung. `POSTGRES_PASSWORD` wirkt nur auf ein leeres Volume, und `keepOrMake` behält in einer bestehenden Helm-Release den alten Wert.

`REASSIGN OWNED BY worker` geht nicht. Postgres antwortet `cannot reassign ownership of objects owned by role worker because they are required by the database system`, denn `worker` ist der Bootstrap-Superuser. Der Wechsel je Objekt ist aber nicht einfach eine Schleife: `ALTER SEQUENCE … OWNER` scheitert an IDENTITY- und serial-Sequenzen (`cannot change owner of sequence … linked to table`, real in consent), `ALTER TYPE … OWNER` scheitert an Array- und Zeilentypen, und bei `citext` gelingt es still, obwohl der Typ der Erweiterung gehört.

Der Eigentümer einer Datenbank kann alles, was die Dienste beim Start brauchen. `CREATE EXTENSION citext` gelingt ihm ohne Superuser-Recht, weil `citext` als vertrauenswürdig gilt, und er kann im Schema `public` Tabellen anlegen. `ALTER TYPE … ADD VALUE`, `LOCK` und Schreiben auf `__EFMigrationsHistory` gingen als Rolle ebenfalls.

Das Passwort des Superusers lässt sich auf einem bestehenden Volume ändern, ohne das alte zu kennen. Laut `pg_hba.conf` vertraut das Image dem Unix-Socket und `127.0.0.1`; alles andere verlangt `scram-sha-256`.

## Die Entscheidung

**Jeder Dienst meldet sich mit der Rolle `svc_<datenbank>` an, und diese Rolle ist Eigentümer genau seiner Datenbank.** Eigentümer und nicht nur Inhaber einzelner Rechte, weil jeder Dienst beim Start sein Schema selbst migriert (`SchemaMigration`) und dafür DDL braucht. Die Rolle ist kein Superuser, darf keine Rollen und keine Datenbanken anlegen, repliziert nicht und umgeht keine Zeilenrechte. `CONNECT` und `TEMPORARY` werden `PUBLIC` auf allen fünfzehn Datenbanken entzogen, außerdem auf `postgres` und `template1`.

**Der Superuser hat kein Netz-Login mehr.** Das Skript setzt in jedem Lauf `ALTER ROLE worker PASSWORD NULL`. Das ist die Entscheidung des Users vom 05.11.2026, und sie ist einfacher als ein neues Passwort: Sie hängt nicht davon ab, was ein altes Volume oder eine alte Release trug, und es gibt kein Geheimnis mehr, das alle Datenbanken öffnet. `WORKERTRANSFER_POSTGRES_PASSWORD` bleibt, weil das Image ohne `POSTGRES_PASSWORD` auf einem leeren Volume nicht startet, gilt aber nur bis zum Ende von initdb. Der Healthcheck läuft über `127.0.0.1` und damit über `trust`, also ohne Passwort, und fragt künftig `-d postgres` statt `-d identity`.

**Der Superuser betritt keine Datenbank, die einer Dienstrolle gehört.** Gemessen: Der Eigentümer setzt für seine Datenbank einen `search_path` und legt `public.now()` an, und ein einfaches `SELECT now()` als `worker` machte ihn zum Superuser. Das Skript prüft den Zustand deshalb aus `postgres`, widerruft auch von dort, und betritt eine Dienstdatenbank nur, solange sie noch `worker` gehört. Abfragen von Hand laufen mit der Rolle des Dienstes.

**Ein Skript macht das.** `scripts/initdb/01-service-roles.sh` ersetzt `01-create-service-databases.sql`. Es legt Rollen an oder setzt ihr Passwort, legt fehlende Datenbanken an (per `\gexec`, denn `CREATE DATABASE` darf nicht in einem Block stehen) und setzt Eigentümer und Rechte. Auf einem leeren Volume ruft es der Entrypoint von Postgres, auf einem bestehenden Volume `make db-roles`, im Cluster kommt es wie bisher die SQL-Datei per `--set-file` in die ConfigMap. Die drei Wege sind nicht gleich, und das Skript berücksichtigt das: Der Entrypoint führt nur ausführbare `.sh`-Dateien aus und sourct alle anderen, und `make db-roles` läuft als root ohne `PGUSER`. Die Datei ist deshalb ausführbar (Git-Modus 755, in der ConfigMap `defaultMode: 0555`, geprüft in `k8s-render-check.sh`), nennt `-U` ausdrücklich und enthält kein `exit`. Das löst nebenbei eine alte Falle: Ein neuer Dienst auf einem bestehenden Volume brauchte bisher ein `CREATE DATABASE` von Hand.

**Auf einem bestehenden Volume wechselt der Eigentümer je Objekt, in einer Transaktion je Datenbank.** Gehört eine Datenbank noch `worker`, läuft dort in dieser Reihenfolge: zuerst `ALTER TABLE … OWNER TO` für Tabellen, Sichten und materialisierte Sichten, womit Indizes und verknüpfte Sequenzen mitwandern. Danach folgen die Sequenzen, die noch `worker` gehören, dann Enums, Domains und freistehende Composite-Typen, dann Funktionen. Mitglieder von Erweiterungen bleiben überall außen vor (`pg_depend.deptype = 'e'`), `citext` gehört weiter `worker` und ist für alle nutzbar. Danach wird `dataprotection_keys` geleert, sofern es die Tabelle gibt; auf einem leeren Volume läuft identity durch denselben Zweig, weil `POSTGRES_DB` sie `worker` übereignet, und die Tabelle entsteht erst beim ersten Zugriff (`to_regclass`). Zuletzt prüft das Skript, dass außer Erweiterungsobjekten nichts mehr `worker` gehört, bricht sonst ab, und übereignet die Datenbank selbst. Weil das eine Transaktion ist, gilt der Zustand „gehört der Rolle" nur, wenn alles gelungen ist; ein Abbruch stellt alles wieder her, und der nächste Lauf beginnt neu. Genau dieser Zustand entscheidet beim nächsten Lauf, dass nichts mehr zu tun ist, und das macht das Leeren des Bundes einmalig.

**Passwörter kommen aus der Umgebung und sind reines Hex.** `WORKERTRANSFER_DB_PASSWORD_<DIENST>` entsteht mit `openssl rand -hex 32`, nicht wie die anderen Secrets als Base64, denn `=`, `+` und `/` stehen sonst in einer Verbindungszeichenfolge. Jeder Dienst sieht nur seines. Der Container von Postgres sieht alle fünfzehn, weil das Skript sie setzen muss; das erweitert nichts, denn wer dort Superuser ist, kommt ohnehin an alles. Im Skript gehen Passwörter als psql-Variable hinein, und `log_min_error_statement` steht für diese Sitzung auf `panic`: Ein fehlgeschlagenes `ALTER ROLE` schriebe sonst sein Passwort ins Serverlog.

**Der Hauptschlüssel bleibt, wo er gebraucht wird, und nur dort.** `WORKERTRANSFER_SECRETS_KEY` wandert aus dem Anker `x-environment` an identity und behält seinen Wert, gespeicherte KI-Schlüssel öffnen sich also weiter. In Helm trägt nur identity `WORKER_SECRETS_KEY`.

**Der Schlüsselbund bekommt seinen eigenen Schlüssel, und zwar je Dienst.** Ein eigener Typ `KeyRingSecret` liest `WORKERTRANSFER_KEYRING_KEY`, der Wert kommt in Compose aus `WORKERTRANSFER_KEYRING_KEY_<DIENST>` und in Helm aus `WORKER_KEYRING_KEY_<DIENST>`. Ein eigener Typ und nicht ein zweiter `SecretStore`, weil in identity beide im selben Container stehen und von zwei Registrierungen desselben Typs die letzte gewinnt: Der Bund läge dann still wieder unter dem Hauptschlüssel. `KeyRingSecret` nutzt dasselbe AES-GCM, die Verschlüsselung bekommt keinen zweiten Code. `AddKeyRing` registriert keinen `SecretStore` mehr, die vierzehn anderen haben also keinen. Fehlt der Wert, startet der Dienst nicht und nennt die Variable. `MasterKeyEnvelope` behält seinen Namen, denn die Schlüssel im Bund nennen ihn, und holt sich `KeyRingSecret` über den `IServiceProvider`.

**ADR-0046 stimmt an zwei Stellen nicht mehr.** Dort steht, im Baum lese niemand den Bund. Seit ADR-0053 versiegelt `ExternalLoginEndpoints.cs` damit das Flow-Cookie der fremden Anmeldung, das zehn Minuten gilt. Das ist die einzige lesende Stelle, auch über Noelia. Und dort steht, der Bund liege unter dem Hauptschlüssel und ein geänderter Schlüssel falle beim Start auf. Unter dem Hauptschlüssel liegt er ab hier nicht mehr, und gefallen ist die zweite Zusage schon vorher: Das Gerüst meldet einen unlesbaren Eintrag als Fehler, überspringt ihn und legt einen neuen Schlüssel an (`[ERR]` von `XmlKeyManager`, `[WRN]` von `DefaultKeyResolver`, gemessen in den Logs der Testläufe vom 05.11.2026). Der Kommentar in `KeyRing.cs` wird entsprechend korrigiert.

Für das Leeren heißt das: Alte Einträge öffnen sich nach dem Wechsel nicht mehr und erzeugten bei jedem Start einen Fehler je Eintrag. Das Leeren hat der User am 05.11.2026 freigegeben. Verloren geht höchstens eine fremde Anmeldung, die in genau diesem Moment läuft: `ReadFlow` liefert `null`, und die Person beginnt neu.

**Helm geht denselben Weg.** `postgresPassword: worker` entfällt. Datenbankpasswörter und Bund-Schlüssel kommen je Dienst über `keepOrMake`, ihre zweite Hälfte ist aber die Rolle im Volume: Nach `helm uninstall` entstehen neue Werte gegen einen alten PVC, und `helm --wait` hängt, bis jemand das Skript ruft. `k8s-up.sh` gibt dazu einen Hinweis. `Username` folgt aus `$svc.database`, der Schlüssel im Secret aus demselben Wert; `secrets.existingSecret` muss diese Schlüssel tragen. Bei `postgres.enabled: false` taugt das Skript nicht, denn es braucht Superuser und Socket. Der Betreiber legt Rollen, Eigentümer und `REVOKE` selbst an, und `values.yaml` führt die Liste. Die Einträge in den `appsettings.json` fallen weg: Ein eingebauter Standardwert ist das Secret.

## Und das Evidence-Secret, aus demselben Grund (Q-0004)

`Evidence__Secret` steht heute im Anker und ist für alle fünfzehn Dienste derselbe Wert. Eine Überschneidung mit ADR-0057 gibt es nicht: Vor `/noelia` steht keine Dienst-Tür, sondern eine Operator-Tür. Es ruft `make evidence` oder die Control Plane, beide laufen auf dem Host, sind keine Clients des Ausstellers, und die Control Plane schickt fest `X-Noelia-Operator`. Dienst-Tokens ersetzen das also nicht.

Jeder Dienst bekommt deshalb `WORKERTRANSFER_EVIDENCE_SECRET_<DIENST>`. `scripts/evidence.sh` und `evidence-check.sh` fragen je Dienst mit dessen Wert. `scripts/control-plane.sh` übergibt sie als `ControlPlane__Fleets__0__Services__<i>__OperatorSecret` aus der Umgebung, denn die Fleet-Datei liegt im Repo und ein Secret gehört nicht in eine Datei. Die Control Plane bevorzugt den Wert je Dienst vor dem gemeinsamen (`FleetRecorder.cs:90`).

## Was das kostet

Compose verlangt die neuen Werte, und `${…:?}` prüft die ganze Datei, also auch für einen einzelnen Dienst. Eine vorhandene `.env` startet nach diesem Umbau nicht, bis die Werte da sind. `make db-roles` ergänzt deshalb erst fehlende Einträge (nichts wird überschrieben, kein Wert wird ausgegeben), stoppt die Dienste, erzeugt den Container von Postgres mit `up -d --wait postgres` und der neuen Umgebung neu, das Volume bleibt, ruft das Skript auf und nennt `make up` als nächsten Schritt. Das ist ein Schritt für den Owner, denn die Datei gehört ihm.

Auf einem bestehenden Volume schlagen die Dienste ohne diesen Schritt mit `password authentication failed` fehl und starten nicht. Das ist laut und gewollt. Werkzeuge, die `worker/worker` nutzen, hören auf zu gehen. Ein kind-Cluster ist Wegwerfware: Der Normalweg ist `make k8s-down` und `make k8s-up`. Wer seine Daten behalten will, ruft dasselbe Skript per `kubectl exec` im Pod von Postgres auf.

Die Testwirte aller fünfzehn Reihen setzen heute `SecretStore.Variable` (62 Dateien) und müssen auf den Schlüssel des Bundes wechseln; die 16 Dateien in Identity brauchen beide. Das ist mechanisch, verlangt aber nach der Erfahrung mit Pflichtkonfiguration zuerst eine einzelne Reihe und dann `helm template`.

Die Umbenennung des Skripts trifft `Makefile:174`, `scripts/k8s-up.sh:156`, `postgres.yaml` und `TopologyTests.cs:151`, und der Schlüssel in der ConfigMap muss auf `.sh` enden, sonst gibt der Entrypoint Bash an psql. Der Test sucht heute `CREATE DATABASE (\w+)` und bekommt eine Liste.

## Was verworfen wurde

Ein neues gewürfeltes Passwort für `worker` hätte Datenbankwerkzeuge auf dem Mac als Superuser arbeiten lassen. Es bliebe aber ein Geheimnis, das alle Datenbanken öffnet, und Helm bräuchte einen neuen Schlüssel, damit eine bestehende Release nicht beim alten Wert bleibt.

Ein einmaliger Compose-Dienst für die Rollen sähe sauberer aus als ein `make`-Ziel. Er müsste aber über das Netz mit dem Superuser kommen, und genau dieses Login verschwindet. Der Socket im Container von Postgres braucht das nicht.

Rechte einzeln zu vergeben statt die Datenbank zu übereignen wäre enger, geht aber nicht auf, solange ein Dienst sich selbst migriert. Eine Migrationsrolle neben einer Laufzeitrolle wäre der richtige nächste Schritt; das steht unten bei den Grenzen.

Ein abgeleiteter Schlüssel je Zweck (HKDF aus dem Hauptschlüssel) trennt nichts: Wer die Wurzel hält, leitet alles ab, und gerade die Wurzel soll aus vierzehn Diensten verschwinden. Ableitung und Versionierung sind Q-0010.

Ein gemeinsamer Bund-Schlüssel für alle fünfzehn wäre eine Variable statt fünfzehn und hielte die Zusage dieses Gates sogar ein, denn die Bünde liegen in getrennten Datenbanken. Er wäre aber wieder ein Geheimnis über alle Dienste. Die Mehrkosten sind eine Schleife in einem Skript.

Den alten Hauptschlüssel als Rückfall zum Lesen alter Bund-Einträge zu behalten, ließe ihn in jedem Dienst stehen. Das ist genau der Zustand, den dieses ADR beendet.

## Was dieses ADR NICHT entscheidet

Es entscheidet nicht über die Rotation des Hauptschlüssels und seine Versionierung, das ist Q-0010 (Rank S). Wer heute `WORKERTRANSFER_SECRETS_KEY` ändert, verliert weiter jeden gespeicherten KI-Schlüssel.

Ein übernommener Dienst hat weiter seine eigene Datenbank ganz, samt DDL, weil er Eigentümer ist. Eine eigene Migrationsrolle bräuchte einen Migrationsschritt vor dem Start und ist nicht Teil dieser Quest. Wer identity selbst übernimmt, hat Hauptschlüssel und KI-Schlüssel. Fünf Dienste bekommen KI-Schlüssel weiter im Klartext über D6 und D7; das schließt erst Q-0045.

Rollennamen, Datenbanknamen und Aktivitätszähler bleiben für jede Rolle sichtbar, Inhalte nicht. `plperl` und `pltcl` gelten im Image als vertrauenswürdig und scheitern nur an fehlenden Bibliotheken; das ist durch Abwesenheit geschlossen, nicht durch Konfiguration, und ein anderes Image müsste es neu prüfen. Auf einem alten Volume bleibt `citext` im Besitz von `worker`, und `ALTER EXTENSION citext UPDATE` schlüge dort als Rolle fehl; heute tut das keine Migration.

Es trennt keine Netze und verschlüsselt die Verbindung zu Postgres nicht. Valkey teilen sich weiter alle Dienste, und `X-Erasure-Secret` bleibt, wie ADR-0057 sagt, ein gemeinsames Secret. Beides ist dieselbe Krankheit und nicht Teil dieser Quests.

## Die Zusagen als Tests

`CrossServiceDbAccess_Denied` ist der Boss und steht in `WorkerTransfer.CrossCutting.Tests`, die dafür Testcontainers bekommt. Fehlt Docker, fällt er rot und überspringt nicht. Er startet Postgres mit dem echten `scripts/initdb` und prüft für jede der fünfzehn Rollen, dass sie in ihre eigene Datenbank kommt und von allen anderen sowie von `postgres` und `template1` mit SQLSTATE `42501` abgewiesen wird, dass sie keines der Rollenattribute trägt und in keiner fremden Rolle Mitglied ist. Ein Test belegt, dass `worker` über TCP scheitert, mit `worker` und ohne Passwort, und dass die Form des Healthchecks läuft.

Ein zweiter Test fährt den echten Bestand nach: Die echten Migrationen aller fünfzehn Dienste laufen als `worker`, dazu Zeilen in `dataprotection_keys` und Fachzeilen. Dann läuft das Skript zweimal. Geprüft wird, dass außer Erweiterungsobjekten nichts mehr `worker` gehört, dass die Fachdaten unverändert sind, dass der Bund nach dem ersten Lauf leer ist und was der zweite Lauf vorfindet, liegen bleibt. Danach läuft die Boss-Matrix noch einmal. Ein Test bricht das Skript mitten im Lauf ab und belegt, dass die Datenbank unverändert `worker` gehört und der nächste Lauf durchgeht. Ein Test belegt, dass kein Passwort im Serverlog steht, auch wenn eine Anweisung des Skripts fehlschlägt.

Ein Test hält fest, dass die Liste im Skript genau die Datenbanken aus `docker-compose.yml` nennt. Der Konsistenztest gegen Compose löst den Anker auf und prüft, dass jeder Dienst nur seine Rolle, sein Passwort, seinen Bund-Schlüssel und sein Evidence-Secret trägt, dass `WORKERTRANSFER_SECRETS_KEY` nur bei identity steht und `WORKERTRANSFER_POSTGRES_PASSWORD` nur bei Postgres, und dass in Code, Compose, Chart und `appsettings.json` keine Verbindungszeichenfolge mit `worker` mehr steht. `EnvironmentFileTests` erkennt die 46 neuen Namen als Secrets; das Muster endet heute auf `$`, und sie fielen durch die Prüfung „in der Vorlage leer“. `make k8s-lint` prüft dasselbe am gerenderten Chart, dazu den Modus der ConfigMap.

Ein Test öffnet mit festem Altchiffrat einen KI-Schlüssel, denn `Decrypt` liefert im Fehlerfall still `null`. Ein Test belegt, dass ein Dienst ohne Bund-Schlüssel nicht startet, dass der Bund einen Neustart überlebt und dass er sich mit dem Hauptschlüssel nicht öffnen lässt. Ein Test ruft das Ergänzen der `.env` auf einer Datei ohne Zeilenumbruch, belegt ganze Werte, nichts Überschriebenes und keinen Wert in der Ausgabe. Die Leak-Probe von Q-0004 belegt, dass das Secret von Dienst A an `/noelia` von Dienst B ein 404 ergibt.

Dass alle fünfzehn Dienste sich als Nicht-Superuser migrieren können, belegt am Ende nur der echte Start: Die Testwirte fahren Postgres als Superuser und sähen das nicht. Die CI-Jobs `images` und `e2e` fahren den ganzen Stack mit `--wait` hoch, und diese Jobs sind für diese Zusage der Test. Der Weg über den Cluster läuft in keinem CI-Job; dort prüft nur `make k8s-lint` das Rendering.
