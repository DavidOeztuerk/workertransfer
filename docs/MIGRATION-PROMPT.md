# Migration WorkerTransfer: Python → .NET auf Girder

Dieses Dokument ist der Einstieg für eine neue Session. Es ersetzt den Verlauf
der Session, in der Girder gebaut wurde. Lies es ganz, bevor du etwas anfasst.

---

## Was du vor dir hast

Zwei Repositorien auf derselben Maschine:

| | Pfad | Zustand |
|---|---|---|
| **WorkerTransfer** | `/Users/davidozturk/Projects/workertransfer` | Python, produktiv gedacht, zehn Dienste + React-App |
| **Girder** | `/Users/davidozturk/Projects/Girder` | .NET 10, **2.3.0**, elf NuGet-Pakete, 3.168 Tests |
| *Demo* | `/Users/davidozturk/Projects/Demo` | Ein kleines Projekt auf Girder. Du brauchst es nicht — was daraus zählt, steht in diesem Dokument |
| *Skillswap* | `/Users/davidozturk/Projects/Skillswap` | **NUR LESEN.** Nie ändern. Herkunft von Girder. |

Girder liegt auf `github.com/DavidOeztuerk/girder`, wird nach GitHub Packages
veröffentlicht und ist die Grundlage der Migration.

**Lies zuerst:**

1. `/Users/davidozturk/Projects/workertransfer/CLAUDE.md` — die Projektregeln.
   Sie sind lang und jeder Absatz hat einen Grund. Besonders die Abschnitte zu
   Einwilligung, Löschung und Mandantenfähigkeit.
2. `/Users/davidozturk/Projects/Girder/README.md` — was Girder anbietet, mit
   Beispielen für jeden Anwendungsfall.
3. `/Users/davidozturk/Projects/Girder/docs/adr/0001-souveraenitaet-durch-portschnitt.md`
   — warum Girder so geschnitten ist. Der Abschnitt „Was die Umsetzung
   korrigiert hat" nennt Fehler, die du nicht wiederholen sollst.
4. `/Users/davidozturk/Projects/workertransfer/docs/ULTRAPLAN.md` und
   `docs/ROADMAP.md` — Stand des Python-Systems.

Mehr brauchst du nicht. Dieses Dokument und Girders README reichen aus; die
Muster, die du wiederholst, stehen unten ausgeschrieben.

---

## Der Umfang, gemessen

```
identity-service      5.562 Zeilen Python   8 Migrationen
transfer-service      2.634                 5
resume-service        2.082                 4
consent-service       1.833                 1
applications-service  1.699                 2
jobs-service          1.630                 2
profile-service       1.528                 1
github-service        1.317                 1
portfolio-service     1.276                 1
companies-service     1.036                 2
                     ─────
                     20.597 Zeilen, 27 Migrationen, 203 Testdateien, 31 ADRs
```

Dazu 19 Python-Pakete unter `packages/` und eine React-App unter `apps/web`.

**Die React-App wird nicht migriert.** Sie spricht HTTP; solange die Verträge
gleich bleiben, merkt sie vom Wechsel nichts. Das ist auch der Prüfstein für
jeden migrierten Dienst.

---

## Die eiserne Regel

> **Der Benutzer hat gesagt: keine Skripte für die Migration. Handübersetzung.**
> *„das würde nur vieles kaputt machen"*

Das gilt. Du darfst Skripte für *Recherche* benutzen (grep, Messungen,
Inventare), aber der übersetzte Code entsteht von Hand, Datei für Datei, mit
Verständnis für das, was dort steht.

Der Grund ist in diesem Projekt belegbar: In `CLAUDE.md` stehen Dutzende
Entscheidungen, die im Code aussehen wie Nachlässigkeit und keine sind — `404`
statt `403` für ein verborgenes Profil, `GRANTED` das nicht „gilt jetzt"
bedeutet, eine Löschung ohne Begründungsfeld. Eine mechanische Übersetzung
zerstört genau diese Stellen, weil sie sie nicht erkennt.

---

## Was du übernimmst und was nicht

### Übernimm aus Girder

- Identität und Mandantenfähigkeit (`Capacity`, `Principal`, `ITenantOwned`-Filter)
- **Anmeldung, Erneuerung, Abmeldung** (`AddTokenSessions`) — Sitzungen liegen
  in *deiner* Datenbank, ein Abmelden kostet eine Zeile und keinen Server
- **Getrennte Schlüssel** (`SigningKey`, `KeyRing`) — nur identity-service hält
  den privaten Teil, jeder andere Dienst kann prüfen und nicht ausstellen
- **Passwort-Hashing** (`AddPasswordHashing`) — und für die Migration
  entscheidend: `Girder.Passwords.BCrypt` liest die bcrypt-Einträge, die
  identity-service heute schreibt
- Berechtigungen, Ressourcenrechte, bedingte Berechtigungen
- Token-Widerruf samt Degradation — **als Ausbaustufe**, nicht als Voraussetzung
- Ratenbegrenzung, Cache, Geheimnisse, Verschlüsselung, Prüfspur
- Middleware-Pipeline, Gesundheitsprüfungen, Telemetrie, Resilience
- Egress-Politik und Souveränitätsbericht

### Baue neu, weil Girder es bewusst nicht hat

- **Einwilligung.** Girders Compliance-Bereich wurde gelöscht (3.002 Zeilen),
  weil er Domäne ist. `apps/consent-service` ist die maßgebliche Fassung — mit
  ADR-0013 (synchron gelesen, nie zwischengespeichert) und ADR-0030
  (`/check-batch`). Übersetze **den**, nicht Girders gelöschte Variante.
- **Löschung/Erasure.** ADR-0027. Die Kaskade, der Outbox-Weg, die sieben
  Empfänger, `RETAIN_*` als Konstante statt Einstellung.
- **Alle Domänenmodelle.** Profil, Lebenslauf, Portfolio, Stellen, Bewerbungen,
  Transfers, Unternehmen.
- **Hintergrundaufgaben.** Girder hat zehn Schleifen gelöscht, weil Aufräum-
  *Politik* dem Dienst gehört. Was `transfer-service` und `identity-service`
  brauchen, gehört in ihre eigene Infrastrukturschicht.
- **Outbox.** Girder hat keine. WorkerTransfer braucht sie für ADR-0025 und für
  die Löschkaskade in ADR-0027. Der saubere Schnitt ist derselbe wie bei
  `IRefreshTokenStore.PurgeAsync`: Tabelle und „Absicht in derselben
  Transaktion aufschreiben" gehören nach Girder, der Versand-Dienst ist eine
  Hintergrundschleife und gehört der Anwendung. Wenn du sie baust, baue sie so
  — und dann gehört sie nach Girder, nicht in einen Dienst.

---

## Vorgeschlagene Reihenfolge

**Nicht mit identity-service anfangen**, obwohl es die Referenz ist. Es ist mit
5.562 Zeilen das größte und hat die meisten Sonderfälle. Ein Fehler im
Vorgehen kostet dort am meisten.

| # | Dienst | Warum diese Stelle |
|---|---|---|
| 0 | **Gerüst + ein Dienst als Muster** | `companies-service` (1.036 Zeilen), einfachste Domäne, benutzt aber Mandantenfähigkeit — beweist das Girder-Fundament |
| 1 | `consent-service` | Alles andere hängt daran. Muss vor Profil/Lebenslauf/Portfolio stehen |
| 2 | `profile-service` | Erster Konsument des Ledgers, klärt das 404/403/503-Muster |
| 3 | `jobs-service` | Caching-Regeln (ADR-0031), Skill-Vokabular |
| 4 | `resume-service`, `portfolio-service` | Dasselbe Muster, strenger |
| 5 | `applications-service` | |
| 6 | `transfer-service` | Outbox, ADR-0025 |
| 7 | **`identity-service`** | Zuletzt, mit allen gelernten Mustern |
| 8 | `github-service` | ADR-0022 beachten: es bewertete Menschen |

**Nach jedem Dienst:** die React-App muss unverändert gegen ihn laufen. Wenn
sie es nicht tut, hat sich ein Vertrag geändert, und das ist ein Fehler, kein
Fortschritt.

---

## Schritt 0 im Detail

Bevor irgendein Dienst übersetzt wird:

1. **Solution anlegen.** `WorkerTransfer.slnx`, `.NET 10`, Central Package
   Management, dieselbe Struktur wie Girder (`src/`, `tests/`).
2. **Girder einbinden — als `PackageReference` auf 2.3.0.** Nicht als
   Projektverweis: die Pakete liegen auf GitHub Packages und die Ports haben
   sich an einem echten Konsumenten bereits bewegt.

   `NuGet.Config` braucht eine Quellzuordnung, sonst kann ein gleichnamiges
   öffentliches Paket die Antwort geben:

   ```xml
   <packageSourceMapping>
     <packageSource key="GitHub"><package pattern="Girder.*" /></packageSource>
     <packageSource key="nuget.org"><package pattern="*" /></packageSource>
   </packageSourceMapping>
   ```
3. **Einen Dienst vollständig bauen**, mit Tests, Migrationen und laufender
   React-App dagegen. Erst wenn das steht, ist das Muster bewiesen.

**Die Pro-Dienst-Struktur** (aus Girders README und dem workertransfer-ADR-0003
— Composition Root pro Dienst, kein fluent PlatformBuilder):

```
src/WorkerTransfer.Companies/
  Domain/          Entitäten, Wertobjekte, Domänenereignisse
  Application/     Commands, Queries, Handler, Ports
  Infrastructure/  DbContext, Repositories, Migrationen
  Api/             Endpunkte, Composition Root
tests/WorkerTransfer.Companies.Tests/
```

---

## Der Composition Root, ausgeschrieben

Jeder Dienst sieht so aus. Nur die Modulliste unterscheidet sich, und sie
unterscheidet sich, weil ein Dienst weniger braucht — nicht weil jemand etwas
vergessen hat.

**Ein Dienst, der Token nur prüft** — also alle außer identity-service:

```csharp
var builder = WebApplication.CreateBuilder(args);
const string serviceName = "companies-service";

builder.Services.AddSharedInfrastructure(
    builder.Configuration, builder.Environment, serviceName, infrastructure => infrastructure
        .AddJwtAuthentication(jwt => jwt.ValidationKeys.Add(
            SigningKey.FromEcdsaPublicKey(
                builder.Configuration["Jwt:PublicKey"]
                ?? throw new InvalidOperationException("Jwt:PublicKey is not configured."),
                builder.Configuration["Jwt:KeyId"]
                ?? throw new InvalidOperationException("Jwt:KeyId is not configured."))))
        .AddPrincipal()
        .AddCaching()
        .AddSecurityHeaders()
        .AddHealthChecks()
        .AddObservability());

builder.Services.AddInMemoryCache(serviceName);

var app = builder.Build();

app.UseSharedInfrastructure(builder.Environment, serviceName, pipeline => pipeline
    .UseExceptionHandling()
    .UseCorrelationId()
    .UseSecurityHeaders()
    .UseAuth()
    .UsePrincipal()
    .UseHealthCheckEndpoints());
```

Dieser Dienst hält nur den öffentlichen Schlüssel. Er kann prüfen und **kann
keinen Token ausstellen** — nicht aus Disziplin, sondern weil ihm das Material
fehlt. `KeyRing.CanIssue` ist dort `false`.

**identity-service** bekommt zusätzlich den privaten Teil und die Sitzungen:

```csharp
        .AddJwtAuthentication(jwt =>
        {
            jwt.SigningKey = SigningKey.FromEcdsaPrivateKey(privateKey, keyId);
            jwt.ValidationKeys.Add(SigningKey.FromEcdsaPublicKey(publicKey, keyId));
        })
        .AddPasswordHashing()
        .AddTokenSessions()
```

```csharp
builder.Services.AddEntityFrameworkRefreshTokens<IdentityDbContext>();
services.AddBCryptPasswordReader();   // liest, was das Python-System geschrieben hat
```

Das Schlüsselpaar erzeugst du mit `SigningKey.GenerateKeyPair(kid)`; beide
Hälften sind je eine base64-Zeile und passen in eine Umgebungsvariable. Der
private Teil geht **nur** an identity-service.

**Anmelden, Erneuern, Abmelden** sehen so aus:

```csharp
var signIn = await sessions.SignInAsync(user.Id);          // Sitzung + erster Erneuerungstoken
var access = await jwt.GenerateTokenAsync(new UserClaims
{
    UserId = user.Id.ToString(),
    Email  = user.Email.Value,
    SessionId = signIn.Session.ToString()                  // damit ein Widerruf ein Gerät benennen kann
});

var again = await sessions.RefreshAsync(presentedToken);   // rotiert, ein Token je Verwendung
await sessions.SignOutAsync(session);                      // dieses Gerät
await sessions.SignOutEverywhereAsync(subject);            // alle
```

Der Erneuerungstoken gehört in ein `HttpOnly`-Cookie mit `SameSite=Strict` und
einem Pfad, der nur die Erneuerungs-Endpunkte trifft — nicht in `localStorage`
und nicht in `sessionStorage`, wo Skript ihn lesen kann. Der Zugriffstoken lebt
im Speicher der Seite und wird beim Laden über das Cookie neu geholt.

**Die Pro-Dienst-Struktur:**

```
src/WorkerTransfer.Companies/
  Domain/          Entitäten, Wertobjekte, Domänenereignisse
  Application/     Commands, Queries, Handler, Ports
  Infrastructure/  DbContext, Repositories, Migrationen
  Api/             Endpunkte, Composition Root
tests/WorkerTransfer.Companies.Tests/
```

Die Anwendungsschicht nennt kein Signaturverfahren und keine Hashfunktion. Sie
erklärt Ports; die Infrastruktur beantwortet sie mit Girder.

---

## Was Girder von dir erwartet

Vier Entscheidungen, die in Girder festgeschrieben sind. Sie zu umgehen ist
möglich und jedes Mal ein Rückschritt.

**Zwei Token, und nur einer ist zurücknehmbar.** Der Zugriffstoken wird von
jedem Dienst allein aus der Signatur geprüft, lebt 15 Minuten und lässt sich
ohne Widerrufsspeicher nicht zurücknehmen. Der Erneuerungstoken liegt als Zeile
in der Datenbank des Ausstellers und wird durch Setzen einer Spalte beendet.
Abmelden heißt: die Sitzung beenden. Es heißt nicht: den Zugriffstoken
widerrufen — das ist die Ausbaustufe für die Fälle, in denen die 15 Minuten zu
lang sind.

**Nur identity-service darf ausstellen.** Es hält den privaten Teil eines
ES256-Paares; alle anderen bekommen den öffentlichen. Ein gemeinsames
`JWT_SECRET` gibt jedem Dienst die Fähigkeit, Token für jede Person mit jeder
Rolle auszustellen. Das ist kein theoretischer Einwand: genau so war es
aufgesetzt, bis es auffiel.

**Girder protokolliert keine Werte.** Weder geschwärzt noch bereinigt: Befehle
und Anfragekörper erscheinen als *Form* — Feldnamen und Wertlängen. Bau das
nicht zurück. Schwärzen nach Liste entfernt nur, woran jemand gedacht hat, und
ein Feld namens `title` mit dem Inhalt „Termin bei Dr. Weber" steht auf keiner
Liste. Wenn du irgendwo eine Nutzlast protokollieren willst, ist die richtige
Frage, ob du sie überhaupt brauchst.

**Aufräumen gehört dem Dienst.** Girder liefert `PurgeAsync` und ruft es nie.
Keine Hintergrundschleife in einer Bibliothek — das gilt auch für die Outbox,
die du bauen wirst.

---

## Wenn etwas nicht funktioniert: wessen Fehler ist es?

Bei jedem Fehler entscheidest du zuerst, **woher er kommt**. Das ist keine
Förmlichkeit — es ist die Stelle, an der man sonst um einen Bibliotheksfehler
herumprogrammiert und ihn damit für immer behält.

### Der Stapelabzug beantwortet die Frage nicht

Girders Pipeline liegt um jeden Handler herum, also steht sie in **jedem**
Abzug. Ein echtes Beispiel aus dem Demoprojekt:

```
System.NullReferenceException
   at UserService.Domain.EmailAddress.Parse(String input)
   at UserService.Application.RegisterUserCommandHandler.Handle(...)
   at Girder.Application.Behaviors.AuditBehavior`2.Handle(...)
   at Girder.Application.Behaviors.PerformanceBehavior`2.Handle(...)
   at Girder.Application.Behaviors.CacheInvalidationBehavior`2.Handle(...)
   at Girder.Application.Behaviors.CachingBehavior`2.Handle(...)
   at Girder.Application.Behaviors.ValidationBehavior`2.Handle(...)
```

Sieben Girder-Zeilen. Der Fehler war trotzdem der der Anwendung: ein Formular
hatte `null` geschickt, und `Parse` hat es nicht abgefangen.

### Der Test, der sie beantwortet

> **Lässt sich der Fehler ohne WorkerTransfer-Code auslösen?**

Schreib einen kleinen Testfall, der nur Girder benutzt. Reproduziert er den
Fehler, ist es Girders. Reproduziert er ihn nicht, ist es unserer — auch dann,
wenn der Abzug voller `Girder.*`-Zeilen steht.

Bleibt es unklar, die zweite Frage: **widerspricht das Verhalten dem, was
Girders README oder die XML-Dokumentation zusagt?** Eine Zusage, die nicht gilt,
ist ein Fehler. Etwas, das nirgends zugesagt ist, ist eine Lücke — und die
gehört genauso gemeldet.

### Wenn es Girders ist: anhalten

1. **Aufhören.** Nicht umgehen, nicht anpassen, nicht „vorläufig so lassen".
2. Ein Ticket schreiben nach `bugs/<was-kaputt-ist>.md`, Vorlage in
   `bugs/VORLAGE.md`. Der kleine Testfall gehört **hinein**, nicht als
   Beschreibung, sondern als Code.
3. Dem Benutzer melden: was kaputt ist, was es blockiert, ob es einen Umweg
   gäbe und was der kosten würde.
4. Auf seine Entscheidung warten.

`bugs/README.md` erklärt, warum der Ordner existiert. Kurz: ein Umweg um einen
Bibliotheksfehler kostet zweimal — einmal beim Bauen, und ein zweites Mal, wenn
Girder den Fehler behebt und der Umweg stehen bleibt, ohne dass noch jemand
weiß, wofür er da war.

**Wenn der Benutzer einen Umweg entscheidet**, markiere ihn im Code mit einem
Verweis auf das Ticket, damit er wieder verschwinden kann:

```csharp
// Umweg für bugs/refresh-token-store-vergisst-subject.md — entfernen,
// sobald Girder das behoben hat.
```

### Und wenn es unserer ist

Dann ist es eine gewöhnliche Aufgabe: Test zuerst, der den Fehler zeigt, dann
der Code. Kein Ticket, keine Unterbrechung.

---

## Fallen, die dieses Projekt schon kennt

Aus `CLAUDE.md` und den 31 ADRs. Jede hat Geld oder Vertrauen gekostet:

- **Mandant ist ein Unternehmen; eine natürliche Person hat keinen.** ADR-0017.
  Girders `Capacity` bildet genau das ab — `AsSelf` vs. `ForCompany`, kein
  nullbares `TenantId`.
- **Einwilligung wird synchron gelesen und nie zwischengespeichert.** ADR-0013.
  Ein Widerruf muss beim nächsten Lesen wirken. Kein `CachingBehavior` auf
  irgendetwas, das von einer Einwilligungsprüfung abhängt.
- **`404` heißt „verborgen oder nicht vorhanden", und beide Antworten müssen
  byte-gleich sein.** Wer das „aufräumt", baut einen Aufzählungskanal.
- **`GRANTED` heißt „wurde einmal gewährt", nicht „gilt jetzt."** Deshalb hat
  `ResumeRequest` weder `is_active` noch `revoked_at`.
- **Die Löschung hat kein Begründungsfeld.** Von jemandem, der gehen will, eine
  Rechtfertigung zu verlangen, ist ein Hebel gegen ihn.
- **Aggregate kommen losgelöst aus den Repositories.** Eine Änderung erreicht
  die Datenbank nur über ein ausdrückliches `save()`. In .NET mit EF Core ist
  das anders (Change Tracking) — **das ist eine echte Verhaltensänderung und
  muss bewusst entschieden werden.**
- **Skill-Vokabular benennt um, es folgert nie.** ADR-0023. `"Postgres" ==
  "PostgreSQL"` ist erlaubt, `"React impliziert JavaScript"` nicht.
- **Passung wird im Browser berechnet und existiert sonst nirgends.**
  Kein serverseitiges Matching, kein Score, keine Rangliste von Menschen.

---

## Wie du arbeiten sollst

Der Benutzer hat im Verlauf mehrfach dasselbe eingefordert, und es hat jedes
Mal echte Fehler gefunden:

1. **Tests zuerst festlegen, dann den Code anpassen.** Nicht den Test
   umschreiben, bis er grün wird. Wenn ein Test fällt, ist erst zu klären, ob
   der Code falsch ist.
2. **Gegenprobe fahren.** Ein Test, der auch bei kaputtem Code grün bleibt,
   beweist nichts. In der Girder-Session waren zwei meiner eigenen Tests
   wirkungslos, und beide sahen richtig aus.
3. **Nichts blind übernehmen.** Girder entstand aus Skillswap, und ein großer
   Teil der Arbeit bestand darin, Übernommenes wieder zu löschen: zehn
   Hintergrundschleifen, Compliance, Backup, tote Codepfade. Frage bei jeder
   Datei, ob sie an dieser Ebene richtig ist.
4. **Kommentare sagen was und wie, nicht warum.** Entscheidungen gehören in
   ADRs. Keine Historie im Code („früher stand hier…").
5. **Plan vorlegen und Zustimmung holen, bevor gelöscht wird.**
6. **Ehrlich berichten.** Wenn Tests fallen, sag es mit Ausgabe. Wenn etwas
   übersprungen wurde, sag das auch.
7. **Bei jedem Fehler zuerst klären, wessen er ist.** Kommt er aus Girder:
   anhalten, Ticket nach `bugs/`, melden, warten. Nicht umgehen. Der Abschnitt
   „Wenn etwas nicht funktioniert" oben sagt, wie man das auseinanderhält —
   und warum der Stapelabzug es nicht verrät.

---

## Erster Auftrag für die neue Session

> Lies `CLAUDE.md` und Girders `README.md`. Mehr Vorlauf brauchst du nicht —
> die Muster stehen in diesem Dokument.
>
> Miss dann den Ist-Zustand von `apps/companies-service` — Endpunkte, Modelle,
> Migrationen, Tests — und leg mir einen Plan für Schritt 0 vor: Solution-Gerüst
> plus `companies-service` als .NET-Dienst auf Girder 2.3.0.
>
> Beantworte darin ausdrücklich die Change-Tracking-Frage. Der Vorschlag aus
> der Girder-Sitzung war: `AsNoTracking()` beim Lesen und ein ausdrückliches
> `SaveAsync` beim Schreiben, weil das die Semantik erhält, gegen die 20.000
> Zeilen Python geschrieben wurden, und weil der Schreibvorgang dann an der
> Aufrufstelle steht. Prüf das und widersprich, wenn du es anders siehst.
>
> Wenn dabei ein Fehler auftaucht: erst klären, ob er aus WorkerTransfer oder
> aus Girder kommt. Aus Girder heißt anhalten, Ticket nach `bugs/`, melden.
>
> Erst nach meiner Zustimmung anfangen.

---

## Zum Schluss: was Girder noch fehlt

Damit du es nicht suchst. Nichts davon blockiert `companies-service`.

| | |
|---|---|
| **Outbox** | Gibt es nicht. Wird spätestens bei `transfer-service` gebraucht |
| **`ILogSanitizer` ohne Konsumenten** | Bleibt als Werkzeug; Girder benutzt es seit dem Umstieg auf Formen nicht mehr |
| **`DataProtectionSecretProvider` ohne Konsumenten** | ASP.NETs Schlüsselring wird angelegt und von nichts benutzt |
| **Doppelte Typnamen** | `CacheStatistics`, `RateLimitResult`, zwei Prüfspur-Systeme |
| **Drei Wege zu einer Berechtigung** | `PermissionMiddleware`, `PermissionPolicyProvider`, per-Berechtigung-Richtlinien — sie antworten nicht gleich |
| **Deutsche Fehlertexte** | `ErrorMessageService` gibt Nutzertexte auf Deutsch zurück |

Der Fahrplan in Girders README führt sie mit Begründung.
