# Messung: die Geheimnisfrage (PBI-8.2 / H2)

> **Das hier ist eine Messung, kein Umbau.** Es wurde nichts eingeschaltet und
> nichts umgebaut; die Proben lagen ausserhalb der Lösung und sind weg. Am Ende
> steht je Modul eine Empfehlung und **eine** Frage, die ein Mensch entscheidet.

Gemessen am **12.09.2026** gegen **Girder 4.4.0** aus nuget.org, .NET 10.0.400,
auf dieser Maschine. Quelltext gelesen im Klon `~/Projects/Girder` (sauber,
`VersionPrefix` 4.4.0); **jede Aussage über Verhalten** stammt aus einem Lauf
gegen die veröffentlichten Pakete, nicht aus dem Lesen — der Unterschied hat
hier dreimal etwas anderes ergeben, als der Methodenname sagt.

Zwei Regeln aus `CLAUDE.md` haben die Messung geführt, und beide haben sich
bezahlt gemacht:

- **Suche nach dem Zweck, nicht nach dem Bauteil.** `grep -ril secret` über
  Girders `src/` findet **48** Dateien, nicht eine. Zum Zweck „Ressourcenrecht"
  gehören **zwei** unabhängige Umsetzungen, und **eine davon läuft bei uns
  bereits** — sie steht im Modul `Authorization`, das wir fahren.
- **Lies die Quelle, nicht den Namen.** `AddSecretManagement` verwaltet keine
  Geheimnisse. `AddEncryption` verschlüsselt nichts. Und die einzige
  ausgelieferte Umsetzung von `IDataEncryptionService` **verschlüsselt
  überhaupt nicht** — sie legt den Klartext base64-kodiert ab und meldet
  `AES256GCM`.

---

## Befund 0 — die Frage „wo liegt der Hauptschlüssel" hat längst eine Antwort

`PLAN-TRANSFERMARKT.md` §8.2 liest sich, als sei das Feld unbestellt
(*„wer damit anfängt, entscheidet **zuerst**, wo der Hauptschlüssel liegt"*).
Es ist bestellt, seit dem KI-Zugang in den Kontoeinstellungen. Nur stand es
nirgends zusammenhängend, und deshalb steht es hier zuerst.

### Was heute steht

| | |
|---|---|
| **Wo** | `src/shared/WorkerTransfer.ServiceDefaults/Geheimnisspeicher.cs` |
| **Verfahren** | AES-256-GCM. GCM und nicht CBC, weil es die Echtheit mitprüft |
| **Schlüssel** | `SHA-256` über `WORKERTRANSFER_SECRETS_KEY` → immer genau 32 Byte |
| **Vektor** | 12 Byte, **gewürfelt je Geheimnis**, steht vorn im Ergebnis |
| **Prüfsumme** | 16 Byte (GCM-Tag) |
| **Wer schreibt** | `SchluesselSetzenHandler` — der KI-Schlüssel, den eine Person selbst einträgt |
| **Wer liest** | `InterneKiZugangHandler`, nur über `/internal/account/{id}/ai` |
| **Wer sieht ihn nie** | der Browser: `GET /account/settings` gibt `SchluesselDa` und vier Zeichen Endung, nie den Schlüssel |
| **Ablage** | eine Spalte, `account_settings.ai_key_encrypted`, in identity-service |
| **Woher der Hauptschlüssel** | Umgebung. `make env` würfelt ihn, `docker-compose.yml` erzwingt ihn mit `${...:?}` |
| **Schlüsselwechsel** | **nein — und das ist bekannt und aufgeschrieben** |

Gemessen, nicht gelesen (Probe gegen den echten Typ):

```
Chiffre (Base64): Z5n48L3OnagbT6Ik2iljdyhNmlgQ008sSHRRyA4pQ1v8bkUfOwoirzHY...
Laenge gesamt   : 67 Byte  (Vektor 12 + Pruefsumme 16 + Inhalt 39)
Klartext drin?  : nein
Zweimal gleich? : nein - je Geheimnis ein eigener Vektor
Hin und zurueck : ok
Ein Bit gekippt : null (GCM merkt es)
Nach Wechsel    : null - jedes hinterlegte Geheimnis ist Muell
Der Typ kann    : .ctor, Endung, Entschluessele, Variable, Verschluessele
Ohne Schluessel : HauptschluesselFehlt: 'WORKERTRANSFER_SECRETS_KEY' ist nicht
                  gesetzt. Ohne Hauptschlüssel kann kein Geheimnis gespeichert
                  werden — und im Klartext wird keines gespeichert.
```

Die vorletzte Zeile ist die Antwort auf die Schlüsselwechselfrage, und sie ist
**keine Lücke, sondern eine ausgesprochene Entscheidung**: `.env.example` sagt
in Grossbuchstaben *„ROLLEN HEISST VERLIEREN"*, der Klassenkommentar sagt, was
das für die Person heisst (die Oberfläche zeigt „keiner hinterlegt", sie legt
einen neuen an), und `Entschluessele` gibt **`null` statt zu werfen**, damit
genau dieser Fall kein Absturz ist. Fünf öffentliche Glieder, kein Wort von
Rotation, Versionierung oder zweitem Empfänger — der Typ *behauptet* nichts,
was er nicht kann.

### Zwei Dinge, die dabei auffielen und keine Meinungsfrage sind

**Das Helm-Chart trägt den Hauptschlüssel nicht.** Gerendert
(`helm template … --set-file postgres.initSql=…`): `WORKER_JWT_SECRET` kommt
15-mal vor, `WORKERTRANSFER_SECRETS_KEY` **null-mal**. `.env` ist in
`.dockerignore`, liegt also auch nicht im Bild. Und gemessen, *wann* das
auffällt:

```
Container gebaut ohne WORKERTRANSFER_SECRETS_KEY: kein Fehler
Erstes Aufloesen : HauptschluesselFehlt — erst hier, nicht beim Start
```

Der Speicher ist ein Singleton (`IdentityInfrastructure.cs:84`), entsteht also
beim ersten Auflösen. Im Cluster hiesse das: identity-service fährt hoch,
meldet sich gesund, und die **erste Person, die einen KI-Schlüssel
speichert**, bekommt einen 500. `docker-compose.yml` kennt das Problem nicht,
weil dort `${...:?}` steht — genau die Mechanik, die im Chart fehlt. Das ist
der einzige Punkt dieser Messung, der eine echte Lücke benennt, und er hat mit
keinem der drei Module zu tun.

**Vierzehn Dienste tragen einen Schlüssel, den einer benutzt.** Die Variable
steht im `x-dienst`-Anker, also in allen fünfzehn Umgebungen; gelesen wird sie
nur von identity-service. Beide Seiten sind vertretbar — ein Anker kann nicht
auseinanderlaufen, eine Aufzählung je Dienst schon —, aber der Wirkungsradius
des Schlüssels ist heute grösser als sein Gebrauch. Keine Empfehlung, nur eine
Feststellung.

---

## Die Methode, je Modul dieselbe

1. **Was tut es wirklich?** Quelltext, aber nachgemessen als Differenz der
   Dienstbeschreibungen vor und nach dem Modul.
2. **Was verlangt es, was zieht es?** `dotnet list package
   --include-transitive` je Paket, und die **Mehrkosten** gegen das, was schon
   im Baum liegt.
3. **Was passiert ohne Anbieter?** Ein Dienst gebaut, gestartet, zugesehen.
4. **Was haben wir schon selbst?** Und ist unseres besser, schlechter oder nur
   anders.

Die Mehrkosten zuerst, weil sie einmal schon eine Entscheidung gekippt haben
(`Bremse.cs`, 4.2.0):

| Paket | transitiv gesamt | **Mehrkosten** über `Girder.Infrastructure` hinaus |
|---|---|---|
| `Girder.Abstractions` | 9 | — |
| `Girder.Http` | 10 | — |
| `Girder.InMemory` | 12 | **2** (`Caching.Abstractions`, `Caching.Memory`) |
| `Girder.Redis` | 16 | **6** (`StackExchange.Redis`, `RESPite`, `System.IO.Hashing`, `Caching.StackExchangeRedis`, `Caching.Abstractions`, `HealthChecks.Abstractions`) |
| `Girder.Infrastructure` | **63** | liegt bereits im Baum (`ServiceDefaults`) |

Die 63 sind die heutige Zahl; `CLAUDE.md` nennt 44, gemessen an 4.2.0 für den
Vergleich mit `Girder.Http`. Beide Zahlen stimmen für ihren Tag — und für die
drei Fragen hier zählt ohnehin nur die letzte Spalte: **sechs Pakete und ein
Server**, oder **zwei Pakete**.

---

## 1. `AddSecretManagement` — läuft seit 4.4.0 bei uns, und tut nichts

**Der erste Befund ist, dass die Frage falsch gestellt war.** `CLAUDE.md`
führt das Modul als „offen, gehört zu H2", also als etwas, das man einschalten
könnte. Es **ist** eingeschaltet: `SecretManagement` steht in
`GirderModuleCatalogue.Defaults`, und `UseDefaults()` holt es. Der laufende
Stapel sagt es selbst:

```
Girder für identity-service: 21 Module in Betrieb (Logging, HttpContextAccess,
JsonOptions, Jwt, SecurityMonitoring, Resilience, SecretManagement, Audit, …)
```

### Was es wirklich tut

Nichts, was jemand aufrufen könnte. Gemessen als Differenz:

```
[1] SecretManagement: 2 Registrierungen (ohne die zwei Buchhaltungszeilen)
      Singleton IOptionsChangeTokenSource<SecretRotationOptions> -> (Fabrik)
      Singleton IConfigureOptions<SecretRotationOptions>         -> (Fabrik)
```

Zwei Zeilen, und beide binden **dieselbe** Optionsklasse an den
Konfigurationsabschnitt `SecretRotation`. Der Quelltext dahinter ist kurz genug
zum Zitieren:

```csharp
// Girder.Infrastructure/Security/SecurityExtensions.cs
var rotationConfig = configuration.GetSection("SecretRotation");
services.Configure<SecretRotationOptions>(rotationConfig);

// Add secret rotation background service
```

Der Kommentar steht da, der Dienst dahinter nicht. Und **`SecretRotationOptions`
hat in ganz Girder keinen Leser** — `grep` findet die Registrierung, die
Klassendefinition, sonst nichts. Dasselbe für das Merkmal
`SecretManagementEnabled`: einmal geschrieben, nie gelesen.

### Was es verlangt

Nichts. Es ist das einzige der drei Module, das ohne Anbieter durchstartet:

```
[2] SecretManagement ohne Anbieter: LAEUFT AN
      ISecretManager     -> NICHT REGISTRIERT
      ISecretProvider    -> NICHT REGISTRIERT
```

Es läuft also **leer mit** — kein Abbruch, kein Hinweis, kein Schaden.

### Nimmt es den Platz von Infisical ein?

**Nein, und das ist gemessen statt geschlossen.** Zwei unabhängige Gründe:

**Erstens: in ganz Girder gibt es keinen Verbraucher von `ISecretManager`.**
`grep -rn "ISecretManager"` über `src/` findet die Schnittstelle, zwei
Umsetzungen (`Girder.Redis`, `Girder.InMemory`), eine dritte, unregistrierte
(`SecureSecretManager` in `Girder.Infrastructure` — nur Tests bauen sie) und
**keinen einzigen Aufruf**. Wer den Speicher füllt, füllt ihn für sich selbst.

**Zweitens: er speist die Konfiguration nicht.** Das ist der Kern, denn genau
das täte Infisical. Gemessen:

```
[2] SecretManagement + AddInMemorySecretManager: LAEUFT AN
      ISecretManager     -> InMemorySecretManager, liest zurueck: abc
      IConfiguration["JwtSettings:Secret"] danach -> (nichts — der Speicher
                                                     speist die Konfiguration NICHT)
```

Girder hat **keinen** Konfigurationsanbieter, der aus einem Geheimnisspeicher
liest (`grep` nach `IConfigurationProvider`/`ConfigurationSource` über `src/`:
ein Treffer, und der protokolliert nur die vorhandenen Quellen). Die
Geheimnisse, die wirklich zählen, kommen weiterhin aus der Umgebung, und Girder
liest sie dort auch selbst:

```csharp
// Girder.Infrastructure/Security/Keys/KeyRingFactory.cs:38
var secret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? …
```

**Die Zusage aus `CLAUDE.md` — „Infisical füllt später die Umgebung; es füttert
`.env`, es ersetzt den Mechanismus nicht" — stimmt an 4.4.0 unverändert.**
Dieses Modul steht nicht daneben und nicht davor; es steht daneben und ist
leer.

### Was wir schon haben, und wie es sich vergleicht

Es ist gar nicht dieselbe Sache, und das ist der Grund, warum hier nichts
ersetzt werden kann:

| | unser `Geheimnisspeicher` | Girders `ISecretManager` |
|---|---|---|
| worum es geht | **Daten einer Person** (ihr KI-Schlüssel), eine Datenbankspalte | **Betriebsgeheimnisse** (JwtSecret, EncryptionKey) |
| wer es setzt | die Person, im Formular | der Betreiber, vor dem Start |
| was Rotation hiesse | fremde Daten neu verschlüsseln | einen neuen Wert **würfeln** (`SecretGenerator.GenerateSecret(32, …)`) |
| Verbraucher in Girder | — | **keiner** |

Girders `RotateSecretAsync` erzeugt ein neues Geheimnis und schreibt es weg.
Für einen Maschinenschlüssel ist das Rotation. Für den API-Schlüssel eines
Menschen wäre es das Gegenteil von dem, was gebraucht wird. **Unsere Frage —
„was wird aus den bereits hinterlegten Geheimnissen?" — beantwortet dieses
Modul nicht, auch nicht mit Anbieter.**

### Empfehlung: **nicht nehmen — und die Zeile ehrlich machen**

Nicht `.Without(...)`: es läuft heute mit, kostet nichts und schadet nichts,
und ein `Without` wäre eine Änderung, die dieser Auftrag nicht will. Was
geändert gehört, ist die **Beschreibung**: aus „offen, gehört zu H2" wird
„läuft mit der Vorgabe, registriert eine Optionsbindung ohne Leser". Die
H2-Frage ist damit nicht offen, sondern **beantwortet: dieses Modul ist keine
Antwort darauf.**

Ein Nebenweg, der es fast wäre, steht in `Girder.Infrastructure` und ist nicht
verdrahtet: `SecureSecretManager` kann `openbao` sprechen (KV v2 über HTTP,
also auch Vault). **Wer das je verdrahtet, muss zwei Dinge wissen:** der
`OpenBaoSecretProvider` baut sich bei fehlendem `HttpClient` selbst einen
(`_httpClient = httpClient ?? new HttpClient()`) — damit an der Fabrik vorbei,
also **an der Egress-Grenze von `SovereignPlatform` vorbei** und in die
100-Sekunden-Vorgabe hinein, gegen die `ZeitlimitTests` steht.

---

## 2. `AddEncryption` — nehmen wäre der schlimmste Fehler, den diese Messung finden konnte

### Was es verlangt, und was ohne Anbieter passiert

Es stirbt beim Start, und es sagt vorbildlich, woran:

```
[2] Encryption ohne Anbieter: STIRBT BEIM START -> InvalidOperationException
      Girder is missing 2 provider registration(s):
        • AddEncryption() needs IDataEncryptionService — call AddRedisEncryption() from Girder.Redis
        • AddEncryption() needs IMasterKeyProvider — call AddConfiguredMasterKey() or AddSecretStoreMasterKey()
```

Das Modul selbst registriert sechs Zeilen, davon **eine** mit Inhalt:
`IFieldEncryptionService` → `FieldEncryptionService`, und der nimmt
`IDataEncryptionService` im Konstruktor. Ohne Anbieter ist das ein Verbraucher,
den niemand bauen kann.

**Anbieter gibt es genau einen: `Girder.Redis`.** Das ist die ganze Antwort auf
„geht es ohne Redis": nein. `IDataEncryptionService` wird in ganz Girder von
einer Klasse umgesetzt, und die nimmt `IConnectionMultiplexer` im Konstruktor.
Die Schlüssel liegen in Redis (`StringSet`/`StringGet` je Schlüssel-Id), mit
dem Hauptschlüssel umhüllt. Kosten: **sechs Pakete und ein Server, den wir
bewusst nicht betreiben** — und ein Server, dessen Persistenz die Schlüssel
trägt, ist kein Zwischenspeicher mehr, sondern eine Datenbank mit
Sicherungspflicht.

### Und dann die eigentliche Messung

Der Weg, den Girders eigene Dokumentation nennt, gegen einen echten
RESP-Server (Redis 8 in einem Wegwerf-Container):

```
[4] Encryption + Redis + Hauptschluessel aus Konfiguration: LAEUFT AN
      Success=True  Algorithm=AES256GCM  Fehler=(keiner)
      Gespeichert wuerde: {"Version":"1.0","KeyId":"key_0fe5…","Algorithm":"AES256GCM",
        "IV":"C9eTgM5VJKDX+6WU","AuthTag":"AAAAAAAAAAAAAAAAAAAAAA==",
        "Data":"c2stYW50LUdFSEVJTU5JUy1EQVMtTklFTUFORC1TRUhFTi1EQVJG", …,
        "IntegrityHash":"d9iC+fmJfvnQBQd4okCXYu275NSYZ/Pi2pxWOvJbmKo=", …}
      Feld Data dekodiert: >>>sk-ant-GEHEIMNIS-DAS-NIEMAND-SEHEN-DARF<<<
      IST DER KLARTEXT: JA
      AuthTag ist lauter Null: True
      IntegrityHash = SHA-256 des KLARTEXTS: True
      Gegenprobe, FREMDER Schluessel: Success=True IntegrityVerified=True
                                      Data=>>>sk-ant-GEHEIMNIS-DAS-NIEMAND-SEHEN-DARF<<<
```

**Es verschlüsselt nicht.** Der Klartext wird base64-kodiert in eine JSON-Hülle
gelegt, die `AES256GCM` behauptet; die Prüfsumme ist lauter Null; daneben liegt
der SHA-256 **des Klartexts**; und ein *fremder* Schlüssel entschlüsselt dieselbe
Hülle anstandslos und meldet `IntegrityVerified = true`. Der Quelltext sagt es
im Kommentar, wenn man ihn findet:

```csharp
// Girder.Redis/Security/DataEncryptionService.cs:623
// Perform GCM encryption (simplified - in production use proper GCM implementation)
Array.Copy(data, encryptedData, data.Length);
```

Die Gegenprobe, dass die Messung überhaupt etwas unterscheiden kann, ist
Befund 0: dieselbe Frage an unseren `Geheimnisspeicher` antwortet
„Klartext drin? **nein**".

**Warum Girders eigene Tests das nicht merken**, und warum das der lehrreiche
Teil ist: `DataEncryptionServiceTests` prüft `result.Success`, Fehlertexte und
einen Hin-und-Rückweg — `EncryptedData.Should().NotBeNullOrEmpty()`. Kein Test
vergleicht je den Geheimtext mit dem Klartext. **Ein Hin-und-Rückweg ist
trivial grün, wenn gar nichts passiert.** Dieselbe Familie wie unsere eigene
Lehre bei den Fakes, die Eingaben wegwerfen.

Das Ticket liegt in
[`bugs/verschluesselung-verschluesselt-nicht.md`](../bugs/verschluesselung-verschluesselt-nicht.md),
mit einer Reproduktion ohne WorkerTransfer-Code.

**Ein zweiter, kleinerer Fund derselben Messung.** Girders Dokumentation
empfiehlt ausdrücklich `AddSecretStoreMasterKey()` gegenüber
`AddConfiguredMasterKey()` (*„prefer … which reads the key from a secret store
you run"*). Dieser Weg **besteht die Startprüfung und fällt dann bei der ersten
Benutzung**:

```
[2] Encryption + AddSecretStoreMasterKey (Girders eigene Empfehlung): LAEUFT AN
      erste Benutzung -> InvalidOperationException: No service for type
                         'Girder.Abstractions.Security.Secrets.ISecretProvider'
                         has been registered.
```

Denn **kein Girder-Modul registriert je einen `ISecretProvider`** — die vier
vorhandenen Anbieter baut allein `SecureSecretManager` intern, und der ist
nicht verdrahtet. Die Startprüfung sieht `IMasterKeyProvider` (als Fabrik
registriert) und ist zufrieden; das Loch liegt eine Ebene tiefer. Genau der
Ausfallmodus, den diese Prüfung abschaffen sollte: *„a missing provider
surfaces on the first request that happens to need it."*

### Was wir schon haben — und die ehrliche Fassung der Frage

Die Aufgabe stellte die Möglichkeit in den Raum, die ehrliche Empfehlung könne
„unseren löschen" lauten. Sie lautet es nicht, und der Vergleich ist nicht
knapp:

| | `Geheimnisspeicher` (unser) | `DataEncryptionService` (Girder.Redis) |
|---|---|---|
| verschlüsselt | **ja**, AES-256-GCM, gemessen | **nein**, gemessen |
| Vektor | 12 Byte je Geheimnis, gewürfelt | gewürfelt, gespeichert, **unbenutzt** |
| Verfälschung fällt auf | ja (`null`) | nein (`IntegrityVerified=true`) |
| fremder Schlüssel | entschlüsselt nicht | entschlüsselt |
| Fehlerweg | wirft beim Start / gibt `null` | `Success=false` in einem Rückgabewert, den ein Aufrufer übersehen kann |
| braucht | eine Umgebungsvariable | Redis + 6 Pakete |
| Zeilen | ~130 | — |

Die vorletzte Zeile ist die, die unabhängig vom Fehler bliebe: `EncryptAsync`
gibt bei jedem Fehlschlag ein Ergebnis mit `Success=false` und **leerem**
`EncryptedData` zurück. Ein Aufrufer, der das Merkmal nicht prüft, schreibt eine
leere Zeichenkette in die Spalte und glaubt, er habe ein Geheimnis gespeichert.
Das ist dieselbe Bauart, an der der Anschreiben-Agent am 10.09.2026 still
gestorben ist.

### Empfehlung: **nicht nehmen. Und die Begründung im Quelltext ersetzen.**

Die heutige Begründung in `Dienstgrundlage.cs` lautet sinngemäss „wir
verschlüsseln auf Feldebene nichts; wer damit anfängt, entscheidet zuerst, wo
der Hauptschlüssel liegt". **Ihre erste Hälfte war schon am Tag ihrer
Niederschrift überholt** — wir verschlüsseln ein Feld, seit es die
Kontoeinstellungen gibt —, und ihre zweite ist beantwortet. Eine Begründung,
die nicht mehr stimmt, ist schlimmer als keine.

Was an ihre Stelle gehört, ist stärker und ändert sich nicht mehr: *die einzige
ausgelieferte Umsetzung verschlüsselt nicht, und sie verlangt dafür einen
Server.* Diese Zeile zu ändern ist ein Einzeiler und gehört in den nächsten
Auftrag, nicht in diese Messung — **hier wird nichts geändert**, und die
Entscheidung, sie zu ändern, ist deine.

---

## 3. `AddResourceAuthorization` — nein, und deshalb

### Was es wirklich tut

16 Registrierungen, davon dreizehn ASP.NETs eigener Autorisierungsapparat, den
wir über `Authorization` ohnehin schon haben. Neu sind drei:
`IPermissionResolver`, `ResourceAuthorizationHandler`,
`OwnershipAuthorizationHandler` — dazu sechs Richtliniennamen
(`ResourceRead`, `ResourceWrite`, `ResourceDelete`, `ResourceOwner`,
`AdminOnly`, `SuperAdminOnly`).

### Was es verlangt

Einen Speicher, sonst stirbt es beim Start:

```
[2] ResourceAuthorization ohne Anbieter: STIRBT BEIM START
        • AddResourceAuthorization() needs IResourceAuthorizationService —
          call AddRedisResourceAuthorization() or AddInMemoryResourceAuthorization()
[2] ResourceAuthorization + AddInMemoryResourceAuthorization: LAEUFT AN
```

`Girder.InMemory` kostet nur zwei Pakete — die Kosten sind hier also **nicht**
das Argument.

### Nimmt es uns etwas weg?

Nein, gemessen, mit Gegenprobe:

```
[3] nur Authorization — was wir heute fahren
      Anbieter: PermissionPolicyProvider
      Permission:companies.invite  -> DenyAnonymousAuthorizationRequirement, PermissionRequirement
      ResourceRead                 -> (keine)
[3] Authorization + ResourceAuthorization
      Anbieter: PermissionPolicyProvider
      Permission:companies.invite  -> DenyAnonymousAuthorizationRequirement, PermissionRequirement
      ResourceRead                 -> ResourceRequirement
```

Der `PermissionPolicyProvider`, an dem unsere Firmenrechte hängen, bleibt. Das
Modul ist rein additiv. **Die Frage ist also nicht, ob es schadet, sondern ob es
etwas *trägt*.**

### Trägt es darüber hinaus etwas? Nein — aus drei Gründen, jeder für sich genug

**Erstens: die Richtlinien blieben leer.** `ResourceAuthorizationHandler`
ermittelt die Ressourcenart aus einer `IResourceMap`; ohne eine registrierte
Karte gilt `ResourceMap.Empty`, und dann liefert jede Herleitung `null` →
`context.Fail()`. Es antwortet also auf keinen unserer Pfade, bis jemand eine
zweite Karte über unsere Endpunkte schreibt. Eine zweite Liste über dieselbe
Frage — genau das Argument, mit dem `PermissionEnforcement` draussen steht, und
neben `docs/routenkarte.yml` wäre es diesmal die **dritte**.

**Zweitens, und das ist der tragende Grund: der Speicher ist eine
Erlaubnistabelle.** `IResourceAuthorizationService` hat
`GrantPermissionAsync`/`RevokePermissionAsync` samt `expiresAt`; die
In-Memory-Fassung hält `_userPermissions[userId][resourceType:resourceId]`.
Eine Erlaubnis läge damit an einer **zweiten** Stelle — neben der
Mitgliedschaftstabelle und neben dem Ledger. Wir lesen die Rolle heute **je
Anfrage und ohne Zwischenspeicher** (`Mitgliedschaftsrecht`), und der Grund ist
derselbe wie bei der Einwilligung: eine Entfernung muss **sofort** wirken.
Eine gewährte und irgendwann widerrufene Zeile ist ein Zwischenspeicher mit
Ablaufdatum. Das ist keine Verbesserung, sondern ein Rückschritt in genau der
Sache, die PBI-2 geradegezogen hat.

**Drittens: `AdminOnly` und `SuperAdminOnly` lesen Rollen aus Ansprüchen**
(`policy.RequireRole("Admin")`), und unser Token trägt keine — absichtlich,
`TokenformTests` nagelt den vollständigen Satz fest. Zwei der sechs
Richtlinien könnten bei uns also nie etwas anderes als „nein" sagen.

### Und was wir davon schon haben, ohne es zu wissen

Beim Suchen nach dem *Zweck* statt dem Bauteil kam heraus: Eigentumsprüfung
gibt es in Girder **zweimal**, und die andere läuft bei uns bereits mit.
`AddGirderAuthorization()` — Teil des Moduls `Authorization`, das wir fahren —
registriert `ResourceOwnerHandler`. Gemessen:

```
[3] nur Authorization — was wir heute fahren
      IAuthorizationHandler: PassThroughAuthorizationHandler, ResourceOwnerHandler,
                             EmailVerifiedHandler, ActiveAccountHandler,
                             PermissionAuthorizationHandler
```

Er beantwortet `ResourceOwnerRequirement` — eine Anforderung, die in unserem
Baum keine Richtlinie stellt. Er ist also untätig, und zwar seit immer. Das
ändert an der Empfehlung nichts, aber es ist die Antwort auf „haben wir davon
schon etwas": ja, den kleineren Teil, und er tut nichts.

### Empfehlung: **nicht nehmen.**

Und diesmal soll die Zeile in `Dienstgrundlage.cs` **schärfer** werden, nicht
länger: heute steht dort „ruft kein Endpunkt hier, und das Modul verlangt einen
Speicher". Beides stimmt weiterhin. Der gemessene Zusatz ist, **was der
Speicher ist** — eine Erlaubnistabelle, also eine zweite Stelle, an der eine
Erlaubnis steht. Auch diese Änderung gehört in den nächsten Auftrag; sie ist
keine Messung.

---

## Die Zusammenfassung, eine Zeile je Modul

| Modul | Stand heute | Empfehlung | der tragende Grund, gemessen |
|---|---|---|---|
| `AddSecretManagement` | **läuft mit der Vorgabe** | **nicht nehmen** (nichts tun, Beschreibung richtigstellen) | registriert eine Optionsbindung ohne Leser; kein Verbraucher von `ISecretManager` in ganz Girder; speist die Konfiguration nicht |
| `AddEncryption` | draussen | **nicht nehmen** | die einzige ausgelieferte Umsetzung legt den **Klartext** ab und meldet `AES256GCM`; verlangt dafür Redis + 6 Pakete |
| `AddResourceAuthorization` | draussen | **nicht nehmen** | sein Speicher ist eine Erlaubnistabelle — eine zweite Stelle für eine Erlaubnis, die sofort wirken muss |

Und die Frage aus H2 — *„nimmt Infisical den Platz dieses Moduls ein oder steht
es daneben?"* — ist damit nicht mehr offen: **keines der drei Module berührt
sie.** Die Umgebung bleibt der Mechanismus, weil Girder gar keinen anderen
anbietet.

---

## Die eine Frage, die nur ein Mensch beantwortet

> **Betreiben wir einen Geheimnisspeicher als eigenen Dienst — OpenBao oder
> Infisical —, oder bleibt die Umgebung der Mechanismus und ein Speicher füllt
> sie nur?**

Das ist keine Codefrage. Ein Speicher ist ein Server mit Entsiegelung,
Sicherung und einem Schlüssel, der ihn selbst schützt — und die Antwort
entscheidet, ob `WORKERTRANSFER_SECRETS_KEY` weiterhin aus `.env` kommt oder
eines Tages aus einem Dienst.

**Was diese Messung empfiehlt, falls gefragt wird:** die Umgebung bleibt. Sie
ist heute erzwungen (`${...:?}`), sie ist erklärt (`.env.example`), sie
funktioniert in einem frischen Klon ohne Login, und Girder liest seine eigenen
Geheimnisse ebenfalls von dort. Ein Speicher lohnt an dem Tag, an dem es
mehrere Betreiber oder mehrere Installationen gibt — bis dahin verschöbe er das
Problem um eine Ebene und brächte einen Dienst mit, der ausfallen kann.

**Zwei Dinge sind davon unabhängig und stehen zur Entscheidung an, sobald du
sie ansiehst:**

1. **Das Helm-Chart trägt `WORKERTRANSFER_SECRETS_KEY` nicht.** Heute fällt das
   niemandem auf, weil `make k8s-up` und das Speichern eines KI-Schlüssels
   noch nie zusammengetroffen sind. Das ist eine echte Lücke, kein Modul.
2. **Die Begründung bei `.Without(GirderModule.Encryption, …)` stimmt in ihrer
   ersten Hälfte nicht mehr** („wir verschlüsseln auf Feldebene nichts").
   Ersetzen durch das Gemessene.

---

## Was hier *nicht* geändert wurde

Kein Modul ein- oder ausgeschaltet, keine `.Without`-Begründung angefasst,
keine Registrierung, kein Test. Der Baum ändert sich in dieser Messung um
Dokumente: diese Datei, drei Zeilen in `CLAUDE.md`, ein Ticket in `bugs/`.

Die Proben lagen unter dem Sitzungs-Scratchpad, ausserhalb der Lösung, und
bestanden aus drei Wegwerf-Projekten (`pak-*` für die Paketzählung, `module`
für Girder, `unser` für den `Geheimnisspeicher`) plus einem Redis-Container
`probe-redis` auf Port **6399** — nicht 6379, damit nichts mit einem echten
Server kollidiert. Alles davon ist weg.

### Wie man es nachmisst

```csharp
// nur Girder, kein WorkerTransfer. Pakete: Girder.Infrastructure,
// Girder.Redis, Girder.InMemory — alle 4.4.0 von nuget.org.
// docker run -d --rm --name probe-redis -p 6399:6379 redis:8-alpine

var b = WebApplication.CreateBuilder();
b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Encryption:MasterKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
});
b.Services.AddRedisConnection("127.0.0.1:6399", "probe");
b.Services.AddRedisEncryption();
b.Services.AddConfiguredMasterKey();
b.Services.AddGirder(b.Configuration, b.Environment, "probe", g => g.Use(GirderModule.Encryption));

var app = b.Build();
await app.StartAsync();

var keyId = await app.Services.GetRequiredService<IKeyManagementService>()
    .CreateKey(KeyType.Symmetric, KeyPurpose.DataEncryption);
var e = await app.Services.GetRequiredService<IDataEncryptionService>()
    .EncryptWithKeyAsync("sk-ant-GEHEIMNIS", keyId);

var huelle = Encoding.UTF8.GetString(Convert.FromBase64String(e.EncryptedData));
using var doc = JsonDocument.Parse(huelle);
Console.WriteLine(Encoding.UTF8.GetString(
    Convert.FromBase64String(doc.RootElement.GetProperty("Data").GetString()!)));
// -> sk-ant-GEHEIMNIS
```
