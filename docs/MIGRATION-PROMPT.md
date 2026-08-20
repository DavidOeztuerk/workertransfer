# WorkerTransfer nach .NET

Dieses Dokument ist der Einstieg für jede Sitzung, die an der Migration
arbeitet. Lies es ganz, bevor du etwas anfasst.

Am Ende läuft alles auf .NET mit Girder, und von Python ist nichts übrig —
keine Dienste, keine Pakete, keine Konfigurationsdateien in der Wurzel.

---

## Die wichtigste Regel: Python ist Vorlage, nicht Maßstab

Das System ist **nicht produktiv**. Der Python-Code ist da, damit du die
Entscheidungen darin verstehst — nicht, damit du sie nachbaust.

**Übernimm die Begründungen. Entscheide die Formen neu.**

| Übernehmen | Neu entscheiden |
|---|---|
| Warum ein verborgenes Profil `404` gibt und nicht `403` | Wie viele Dienste es gibt. Zehn ist eine Python-Entscheidung |
| Warum `GRANTED` „wurde einmal gewährt" heißt und nicht „gilt jetzt" | Wie viele geteilte Pakete es gibt. Girder deckt das Querschnittliche |
| Warum die Löschung kein Begründungsfeld hat | Wie Outbox, Kaskade und Caching gebaut sind |
| Dass niemand Menschen bewertet (ADR-0022) | Wo die Grenzen zwischen Diensten verlaufen |
| Dass die Löschung wirklich löscht (ADR-0027) | Welche Endpunkte es gibt und wie sie heißen |

Die 31 ADRs in `docs/adr/` sind das Wertvollste am alten System. Der Code ist
ihre erste Umsetzung, nicht ihre einzig mögliche. Wo du es besser weißt, mach es
besser — und schreib auf, warum.

**Der Prüfstein ist nicht „läuft die React-App unverändert dagegen".** Er ist:
*tut der Dienst, was die Vision und die ADRs verlangen, und ist er sauberer
geschnitten als vorher.* Die React-App wird angepasst, wo sich ein Vertrag
ändert; sie ist der letzte Konsument, nicht der Maßstab.

### Keine Skripte für die Übersetzung

Der Benutzer hat gesagt: **Handübersetzung.** *„das würde nur vieles kaputt
machen"*. Für Recherche darfst du greppen, messen und Inventare ziehen; der Code
entsteht Datei für Datei, mit Verständnis für das, was dort steht.

Der Grund ist belegbar: In `CLAUDE.md` stehen Dutzende Entscheidungen, die im
Code wie Nachlässigkeit aussehen und keine sind. Eine mechanische Übersetzung
zerstört genau diese Stellen, weil sie sie nicht erkennt.

---

## Was du vor dir hast

| | Pfad | Zustand |
|---|---|---|
| **WorkerTransfer** | `/Users/davidozturk/Projects/workertransfer` | Python (Vorlage) + `dotnet/` (das Ziel) + `apps/web` (React) |
| **Girder** | `/Users/davidozturk/Projects/Girder` | .NET 10, **2.3.0**, elf Pakete auf GitHub Packages, 3.168 Tests |
| *Skillswap* | `/Users/davidozturk/Projects/Skillswap` | **NUR LESEN.** Nie ändern. Herkunft von Girder |

**Lies zuerst:**

1. `CLAUDE.md` — die Projektregeln. Sie sind lang und jeder Absatz hat einen
   Grund. Besonders Einwilligung, Löschung, Mandantenfähigkeit.
2. `/Users/davidozturk/Projects/Girder/README.md` — was Girder anbietet, mit
   Beispielen für jeden Anwendungsfall.
3. `docs/adr/` — die Begründungen, die du übernimmst.
4. `docs/vision/kon.txt` und `docs/vision/IMPLEMENTATION_PLAN.md` — die Absicht.
   Lies sie als Absicht, nicht als Beschreibung des Bestehenden.

Mehr brauchst du nicht. Die Muster, die du wiederholst, stehen unten
ausgeschrieben.

---

## Zweig- und Commit-Strategie

**Ein Zweig für das ganze Vorhaben: `dotnet-migration`**, von `develop`
abgezweigt. Nicht einer je Dienst — die Migration ist ein Vorhaben.

```
develop ──┬──────────────────────────────────────────────► (am Ende: PR)
          └── dotnet-migration ──●──●──●──●──●──●──●──►
                              identity  gateway  consent  …
```

Er ist ausgecheckt. Wenn du dich auf einem anderen Zweig wiederfindest, wechsle
zurück, **bevor** du committest. (Genau das ist in der Girder-Sitzung
schiefgegangen, zweimal hintereinander, obwohl es aufgefallen war.)

**Commit, wann immer eine Einheit fertig und grün ist.** Nicht erst, wenn ein
ganzer Dienst steht: identity-service sind einundzwanzig Routen, und ein Commit
darüber ist einer, den niemand mehr lesen kann. Eine Einheit ist zum Beispiel

- die Domänenschicht eines Dienstes,
- eine zusammengehörige Gruppe Routen (Anmeldung; Registrierung und Bestätigung;
  Einladungen und Rollen),
- ein Stück Infrastruktur (Persistenz, Outbox-Anbindung, DI-Registrierung),
- das Löschen eines Python-Dienstes, dessen Nachfolger steht.

**Bedingung für jeden Commit:** `dotnet build` ohne Warnung, `dotnet test` ohne
roten und ohne übersprungenen Test. Beides in **getrennten** Aufrufen — ein
`build && test` in einem Befehl lässt Testcontainers-Reihen scheitern und sieht
dabei aus wie ein echter Testfehler.

**Push, wann du willst.** Ein Zwischenstand auf dem Zweig schadet niemandem. Am
Ende ein PR nach `develop`, danach `develop` → `main` wie üblich. Ein
Feature-Zweig geht nie direkt nach `main`.

Nachrichten: Betreff sagt, was sich für den Leser ändert; der Rumpf sagt
**warum**, und nennt, was du gemessen oder verworfen hast. Deutsch, wie im Repo
üblich.

```
Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

---

## Die Zielarchitektur

```
                Browser (apps/web, React)
                          │
                  dotnet/src/gateway
                  Ocelot — routet, prüft nichts
                          │
    ┌──────────┬──────────┼──────────┬──────────┐
 identity   consent    profile     jobs        …
    │          │          │          │
    └──────────┴──────────┴──────────┘
           je eigenes Schema in Postgres
```

### Ein Projekt je Schicht

```
dotnet/src/<dienst>/
  WorkerTransfer.<Dienst>.Domain
  WorkerTransfer.<Dienst>.Contracts
  WorkerTransfer.<Dienst>.Application
  WorkerTransfer.<Dienst>.Infrastructure
  WorkerTransfer.<Dienst>.Api
dotnet/tests/WorkerTransfer.<Dienst>.Tests
```

Getrennte Projekte, weil dann der **Übersetzer** die Richtung erzwingt: die
Domänenschicht kann die Infrastruktur nicht aufrufen, weil sie keinen Verweis
darauf hat. Ordner in einem Projekt verlassen sich auf Disziplin, und Disziplin
hält, bis es eilig wird. Es ist auch der Zuschnitt, in dem Girder selbst gebaut
ist.

### Was in welche Schicht gehört

| Schicht | Inhalt | Verweist auf |
|---|---|---|
| **Domain** | Entitäten, Wertobjekte, Domänenereignisse, Domänenfehler, **Repository-Schnittstellen** | nichts |
| **Contracts** | Anfrage- und Antwortformen der HTTP-Grenze | nichts |
| **Application** | Commands, Queries, deren Handler, Dienstmethoden, Ports zu allem außer Persistenz | Domain |
| **Infrastructure** | `DbContext`, Repository-Umsetzungen, Migrationen, Adapter, **die gesamte DI-Registrierung** | Application, Domain |
| **Api** | Endpunkte, Composition Root | Application, Contracts, Infrastructure |

**Repository-Schnittstellen liegen in Domain**, nicht in Application. Die Domäne
besitzt den Vertrag über ihre eigene Persistenz. Der Preis: Domain kennt `Task`
und `CancellationToken`. Das ist vertretbar; entscheidend ist, dass es in
**allen** Diensten gleich ist — eine Regel, die je Dienst anders gilt, ist keine.

**Contracts werden nur in Api verwendet.** Handler nehmen Commands und Queries
entgegen und geben Domänenobjekte oder Anwendungsergebnisse zurück; die Api
bildet auf Contracts ab. Application darf keinen Verweis auf Contracts haben —
steht dort einer, entferne ihn. (Im heutigen Gerüst steht er und wird nicht
benutzt.)

### Wo Girder eingesetzt wird

**Girder wird vollständig eingesetzt, Paket für Paket, je Schicht:**

| Schicht | Girder-Pakete | Wofür |
|---|---|---|
| **Domain** | `Girder.Core` | `SubjectId`, `TenantId`, `SessionId`, `Capacity`, `Principal`, `Entity`, `ValueObject`, `Result`, `DomainError` |
| **Contracts** | `Girder.Contracts` | Paginierung, Vertragsversionierung |
| **Application** | `Girder.Abstractions`, `Girder.Application` | alle Ports; Mediator und Pipeline-Behaviors |
| **Infrastructure** | `Girder.Infrastructure`, `Girder.Data.EntityFrameworkCore` | Adapter, Persistenz, Sitzungen, Schlüssel |
| | dazu je nach Bedarf `Girder.Passwords.BCrypt` / `.Argon2`, `Girder.Redis`, `Girder.Messaging.MassTransit`, `Girder.InMemory` | |
| **Api** | `Girder.Infrastructure` | Composition Root und Pipeline |

Was ein Dienst nicht braucht, installiert er nicht. Ein ungenutztes Paket ist
kein kleineres Merkmal, sondern Code, der nie läuft — dieselbe Lehre wie
ADR-0031, wo neun Pakete ohne Konsumenten gelöscht wurden und drei davon
nachweislich kaputt waren.

Girder kommt als `PackageReference` auf 2.3.0, nicht als Projektverweis. Die
Quellzuordnung in `NuGet.Config` ist Pflicht, sonst kann ein gleichnamiges
öffentliches Paket die Antwort geben:

```xml
<packageSourceMapping>
  <packageSource key="GitHub"><package pattern="Girder.*" /></packageSource>
  <packageSource key="nuget.org"><package pattern="*" /></packageSource>
</packageSourceMapping>
```

### Die DI-Registrierung

**Die gesamte Registrierung eines Dienstes entsteht in Infrastructure**, hinter
genau einer Erweiterungsmethode:

```csharp
// Infrastructure/IdentityInfrastructure.cs
public static IServiceCollection AddIdentityInfrastructure(
    this IServiceCollection services, IConfiguration configuration)
{
    services.AddDbContext<IdentityDbContext>(…);
    services.AddScoped<IUserRepository, UserRepository>();
    services.AddScoped<IMembershipRepository, MembershipRepository>();
    services.AddScoped<IUnitOfWork, UnitOfWork>();
    services.AddBCryptPasswords();
    services.AddEntityFrameworkRefreshTokens<IdentityDbContext>();
    return services;
}
```

**In Api wird nur diese eine aufgerufen** — plus die Girder-Modulauswahl und die
Pipeline, denn das *ist* der Composition Root (ADR-0003), und man muss ihm
ansehen, woraus der Dienst besteht:

```csharp
var builder = WebApplication.CreateBuilder(args);
const string serviceName = "identity-service";

builder.Services.AddSharedInfrastructure(
    builder.Configuration, builder.Environment, serviceName, infrastructure => infrastructure
        .AddJwtAuthentication(jwt => { /* Schlüssel, siehe unten */ })
        .AddPrincipal()
        .AddPasswordHashing()
        .AddTokenSessions()
        .AddCaching()
        .AddSecurityHeaders()
        .AddHealthChecks()
        .AddObservability());

builder.Services.AddIdentityInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseSharedInfrastructure(builder.Environment, serviceName, pipeline => pipeline
    .UseExceptionHandling()
    .UseCorrelationId()
    .UseSecurityHeaders()
    .UseAuth()
    .UsePrincipal()
    .UseHealthCheckEndpoints());
```

Die Modulauswahl gehört **nicht** in `AddIdentityInfrastructure()`. Wandert sie
dorthin, ist der Composition Root vergraben und niemand sieht mehr, welche
Querschnittsentscheidungen dieser Dienst trifft. Nur die Modulliste
unterscheidet sich zwischen Diensten, und sie unterscheidet sich, weil ein
Dienst weniger braucht — nicht, weil jemand etwas vergessen hat.

Die Anwendungsschicht nennt kein Signaturverfahren und keine Hashfunktion. Sie
erklärt Ports; die Infrastruktur beantwortet sie mit Girder.

---

## Schlüssel: wer ausstellen darf

**Nur identity-service hält den privaten Schlüssel.** Jeder andere Dienst
bekommt den öffentlichen und kann prüfen, aber nicht ausstellen — nicht aus
Disziplin, sondern weil ihm das Material fehlt. `KeyRing.CanIssue` ist dort
`false`.

```csharp
// identity-service
.AddJwtAuthentication(jwt =>
{
    jwt.SigningKey = SigningKey.FromEcdsaPrivateKey(privateKey, keyId);
    jwt.ValidationKeys.Add(SigningKey.FromEcdsaPublicKey(publicKey, keyId));
})

// jeder andere Dienst
.AddJwtAuthentication(jwt => jwt.ValidationKeys.Add(
    SigningKey.FromEcdsaPublicKey(
        configuration["Jwt:PublicKey"]
        ?? throw new InvalidOperationException("Jwt:PublicKey is not configured."),
        configuration["Jwt:KeyId"]
        ?? throw new InvalidOperationException("Jwt:KeyId is not configured."))))
```

Das Paar erzeugst du mit `SigningKey.GenerateKeyPair(kid)`; beide Hälften sind je
eine base64-Zeile und passen in eine Umgebungsvariable. Ein gemeinsames
`JWT_SECRET` gäbe jedem Dienst die Fähigkeit, Token für jede Person mit jeder
Rolle auszustellen. Das ist kein theoretischer Einwand — genau so war es
aufgesetzt, bis es auffiel.

**Anmelden, Erneuern, Abmelden:**

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

Sitzungen liegen in identity-services eigener Datenbank
(`AddEntityFrameworkRefreshTokens<IdentityDbContext>`). Abmelden kostet eine
Zeile, keinen zusätzlichen Server.

Der Erneuerungstoken gehört in ein `HttpOnly`-Cookie mit `SameSite=Strict` und
einem Pfad, der nur die Erneuerungs-Endpunkte trifft — nicht in `localStorage`
und nicht in `sessionStorage`, wo Skript ihn lesen kann. Der Zugriffstoken lebt
im Speicher der Seite und wird beim Laden über das Cookie neu geholt.

---

## Das Gateway

`dotnet/src/gateway/WorkerTransfer.Gateway`, ein eigenes Projekt mit **Ocelot**.
Es routet und **prüft nichts**: jeder Dienst prüft für sich, damit ein Gateway,
das eine Anfrage durchwinkt, nie mit Autorisierung verwechselt werden kann.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);

builder.Services.AddSharedInfrastructure(
    builder.Configuration, builder.Environment, "gateway", infrastructure => infrastructure
        .AddCaching()
        .AddSecurityHeaders()
        .AddHealthChecks()
        .AddObservability());

builder.Services.AddInMemoryCache("gateway");
builder.Services.AddOcelot(builder.Configuration);

var app = builder.Build();

app.UseSharedInfrastructure(builder.Environment, "gateway", pipeline => pipeline
    .UseExceptionHandling()
    .UseCorrelationId()
    .UseSecurityHeaders()
    .UseHealthCheckEndpoints());

await app.UseOcelot();
await app.RunAsync();
```

Eine Route je Pfadgruppe in `ocelot.json`, ein Eintrag je Dienst. Die
Korrelations-ID reist als `X-Correlation-ID`-Kopf und wird von Girders
Middleware durchgereicht — sie hat mit dem Token nichts zu tun und darf nie aus
ihm abgeleitet werden.

Das Gateway ersetzt Traefik aus dem Compose-Stand. Solange beide Welten laufen,
darf es auf Python-Dienste zeigen; das ist der ganze Zweck eines Gateways.

**Eine Falle aus dem Python-System, die hier wiederkommt** (`CLAUDE.md`,
Abschnitt Kubernetes): Ein Ursprung lässt `/jobs` zwei Dinge bedeuten — die
API-Ressource und die Seite. Dasselbe für `/applications` und `/transfers`.
Klicken in der App funktioniert, weil der Router im Browser umschaltet und nie
fragt; nur der **Deep Link** und das **Neuladen** brechen — also genau das, was
Leute teilen und was nach einem Absturz passiert. Was die Frage wirklich
beantwortet, ist der Kopf `Sec-Fetch-Dest: document`: er kommt nur bei einer
Navigation auf oberster Ebene, `fetch` schickt `empty`, curl schickt nichts. Eine
Regel trennt „ein Mensch öffnet eine Seite" von „ein Programm holt Daten" — keine
zweite Pfadliste, die auseinanderdriften kann.

---

## Reihenfolge

| # | Schritt | Warum diese Stelle |
|---|---|---|
| ✅ 0 | **Gerüst** | steht: `dotnet/`, `WorkerTransfer.slnx`, zentrale Paketverwaltung, Quellzuordnung |
| ✅ 1 | **identity, dünne Scheibe** | steht: Anmelden, Erneuern, Abmelden, Tokenform bewiesen |
| **2** | **identity, vollständig** | 21 Routen: Registrierung, Bestätigung, Unternehmen, Einladungen, Rollen, Löschkaskade |
| 3 | **Gateway** | sobald ein Dienst vollständig ist, damit die App wieder etwas hat, worauf sie zeigt |
| 4 | **consent** | alles andere hängt daran (ADR-0013, ADR-0030) |
| 5 | **profile** | erster Konsument des Ledgers, klärt das 404/403/503-Muster |
| 6 | **companies** | einfachste Domäne, prüft Mandantenfähigkeit gegen echte Token |
| 7 | **jobs** | Caching-Regeln (ADR-0031), Skill-Vokabular (ADR-0023) |
| 8 | **resume, portfolio** | dasselbe Muster, strenger |
| 9 | **applications** | |
| 10 | **transfer** | Outbox (ADR-0025) |
| 11 | **github** | ADR-0022 beachten: der alte Dienst bewertete Menschen |
| 12 | **Aufräumen** | Python raus, Docker, Helm, CI/CD, Wurzel |

**identity zuerst** hat einen gemessenen Grund, siehe „Das Übergangsgerüst".

**Ob es am Ende elf Dienste sind, entscheidest du.** `consent`, `profile`,
`resume` und `portfolio` kreisen alle um die Daten *einer* Person hinter *einem*
Einwilligungstor; werden daraus zwei Dienste, ist das ein Gewinn und kein
Verlust. `github-service` ist unter ADR-0022 ohnehin fraglich. Leg den Schnitt
vor, **bevor** du ihn baust — mit Begründung, nicht als Nebensatz.

Ein Python-Dienst geht erst, wenn sein .NET-Nachfolger vollständig steht und das
Gateway auf ihn zeigt. Nicht vorher.

---

## Das Übergangsgerüst — und wann es wieder verschwindet

Alles in diesem Abschnitt existiert **nur**, solange Python und .NET
nebeneinander laufen. Es ist eine Brücke, keine Architektur. Jedes Stück ist im
Code mit einem Verweis auf `docs/uebergang-python-dotnet.md` markiert und wird in
Schritt 12 **ersatzlos entfernt**.

### Warum identity zuerst kommt

**Girder kann die Token nicht lesen, die der Python-identity-service ausstellt.
Und Python kann die nicht lesen, die Girder ausstellt.** Beides gemessen, nicht
vermutet.

| | Python → Girder | Girder → Python |
|---|---|---|
| `iss` | fehlt → Girder lehnt ab | Python prüft ihn nicht, harmlos |
| `aud` | fehlt → Girder lehnt ab | **steht immer drin → Python lehnt ab** |
| `type` | Girder prüft ihn nicht → **ein Refresh-Token gilt als Access-Token** | **fehlt** → Python lehnt ab |
| Mandant | `tenant_id` gegen Girders `tenant` | dasselbe umgekehrt |
| `roles`/`permissions` | Girder liest sie nicht, harmlos | **nur als Zeichenkette darstellbar → Python lehnt ab** |

Der Mandanten-Punkt ist der gefährliche, weil er **lautlos** scheitert:

```
wie Girder konfiguriert ist        ABGELEHNT: SecurityTokenInvalidAudienceException
mit abgeschalteter iss/aud-Prüfung angenommen als "as self"
                                                   ↑
im Token steht tenant_id = 2222…, Girder liest "tenant" — den gibt es dort nicht
```

Ein Firmen-Akteur wird zur Privatperson. Kein Fehler, kein Protokolleintrag,
andere Daten sichtbar.

Jeder Dienst außer identity prüft nur Token. Migrierst du einen davon zuerst,
muss er die Token des *Python*-Dienstes annehmen — und der einzige Weg dahin ist
genau diese Herabstufung, zehnmal wiederholt. **Also identity zuerst.**

### Was das Gerüst umfasst

- **`verify_aud: False`** in `packages/worker-auth/src/worker_auth/jwt.py` (Ü-1).
  Girder kann nicht ohne `aud` ausstellen — eine leere Zielgruppe lehnt
  `JwtService` schon im Konstruktor ab. Ticket:
  `bugs/jwtservice-kann-nicht-ohne-aud-ausstellen.md`.
- **`IssuerValidator`/`AudienceValidator` als Delegat** statt abgeschalteter
  Flaggen (Ü-2): *fehlt* der Anspruch, ist es der alte Aussteller und wird
  angenommen; *steht* er drin, muss er unserer sein. Eine Regel, die man
  hinschreiben und prüfen kann — keine Prüfung, die man ausschaltet.
- **Ein eigener `IPrincipalFactory`** (Girder registriert seinen mit schlichtem
  `AddSingleton`, unserer danach gewinnt), der `tenant` **und** `tenant_id` liest,
  bei `type != "access"` ablehnt und bei einem Mandanten-Anspruch, der dasteht
  und nicht lesbar ist, **401 antwortet statt herabzustufen**. Damit wird aus dem
  lautlosen Fehler ein lauter, und das ist der ganze Unterschied zum Flicken.
- **Zwei zusätzliche `CustomClaims`** im ausgestellten Token (Ü-3):

  ```csharp
  CustomClaims = new()
  {
      ["tenant_id"] = t.ToString(),   // was Python zusätzlich braucht
      ["type"]      = "access"
  }
  ```

  Zwei, nicht vier: `roles` und `permissions` bleiben draußen. `CustomClaims` ist
  ein `Dictionary<string, string>` und kann keine Liste ausdrücken
  (`bugs/customclaims-kann-keine-liste-ausdruecken.md`); Weglassen geht, weil
  Python dann `[]` setzt. Der stärkere Grund ist aber, dass die Ansprüche nie
  maßgeblich waren: Rollen kommen aus `user_tenant_memberships`, nie aus dem
  Token.
- **Kein Wandern des Erneuerungstokens** (Ü-4): Girders ist ein undurchsichtiger
  Zufallswert, Pythons ein JWT. Beim Umstieg meldet sich jeder einmal neu an.
- **Tabelle `SessionId → TenantId`** (Ü-5): `GirderRefreshToken` hat keine
  Mandantenspalte, Pythons `sessions` schon. Ohne diese Tabelle überlebt die
  Firma eine Erneuerung nicht — ein Firmen-Akteur wird nach fünfzehn Minuten
  stillschweigend zur Privatperson. Weil `SessionId` über die ganze
  Rotationskette stabil bleibt, ist das eine Zeile je Anmeldung.

Fahr die Gegenproben mit: falsche Signatur, abgelaufen, ein Refresh-Token als
Zugriffstoken, ein Mandanten-Anspruch, der dasteht und nicht lesbar ist. Ohne
sie beweist ein grüner Durchlauf nur, dass etwas durchkommt.

---

## Zwei Entscheidungen, die du früh treffen musst

Beide beißen ab etwa Route zehn, und beide sind teuer, wenn du sie spät triffst.

**Mediator: ja oder nein?** Die dünne Scheibe hat ihn weggelassen — richtig für
drei Handler, und `CachingBehavior` auf einer Anmeldung wäre falsch. Bei
einundzwanzig Routen kippt das: `Girder.Application` bringt Audit, Validierung,
Logging, Performance und Cache-Invalidierung als Pipeline mit, und genau die
willst du aus Girder ziehen statt sie nachzubauen. Entscheide es bei Schritt 2,
nicht bei Route fünfzehn. Wenn ja, dann `AddCQRS([assembly])` in der
Anwendungsschicht, und die Handler bekommen `IRequest`/`IRequestHandler`.

Was Girder **nicht** liefert, ist ein Transaktions-Behavior. Das eine
`SaveChangesAsync` gehört in eine UnitOfWork je Anfrage, und die ist unsere.

**Wohin gehören Domänenereignisse und ausgehende Ereignisse?** Die Löschkaskade
(ADR-0027) verschickt an sieben Empfänger und gilt erst als fertig, wenn keine
Outbox-Zeile mehr ohne `delivered_at` steht. **Girder hat keine Outbox.** Der
Schnitt, wenn du sie baust: Tabelle und „Absicht in derselben Transaktion
aufschreiben" gehören nach Girder, der Versand-Dienst ist eine
Hintergrundschleife und gehört der Anwendung — dieselbe Trennung wie bei
`IRefreshTokenStore.PurgeAsync`. Kläre das, **bevor** du die Löschung baust.

Drei Eigenschaften der Outbox sind tragend und leicht wegzuräumen: sie hält
**keinen Inhalt**, nur `user_id` und `kind` (sie landet in jedem Backup); Aufgeben
heißt **die Zeile stehen lassen**, nie löschen; und die Zustellung der Löschung
**muss scheitern können** — ein toter Empfänger blockiert die Fertigmeldung, und
das ist der Sinn.

---

## Change Tracking

Entschieden, damit es niemand zweimal herleitet.

**Was Python zusichert, ist nicht „kein Tracking", sondern: das Aggregat ist ein
anderes Objekt als die Zeile.** `_to_domain` baut ein neues; nichts am Aggregat
erreicht die Datenbank außer über ein ausdrückliches `save()`. Gäbe ein
.NET-Repository EF-Entitäten mit `AsNoTracking()` heraus, müsste `save()` einen
losgelösten Graphen `Update()`n — das schreibt *alle* Spalten und holt genau die
Fehlerklasse zurück, vor der die Python-Repositories warnen.

Also, in dieser Reihenfolge:

1. **Die Abbildung bleibt** (Zeile → Domänenobjekt). Sie erhält die Semantik.
2. **`NoTracking` als Voreinstellung des DbContext**, nicht als Aufruf je
   Abfrage. Ein Aufruf je Abfrage ist Disziplin, und die nächste hinzugefügte
   Abfrage vergisst ihn.
3. **Ausdrückliches `SaveAsync` am Repository**, damit der Schreibvorgang an der
   Aufrufstelle steht.
4. Eine Versionsspalte wird EF-Nebenläufigkeitsmarke.

**Die Normalisierungsfalle** — der Grund, warum Punkt 2 keine Vorsichtsmaßnahme,
sondern eine Notwendigkeit ist: der Domänenkonstruktor normalisiert. Er trimmt,
entdoppelt nach `casefold`, wirft Leeres weg, weist eine feindliche URL ab. Mit
eingeschaltetem Tracking wäre das Ergebnis dieser Normalisierung eine *Änderung*
an einer verfolgten Entität — ein reiner Lesevorgang schriebe beim nächsten
`SaveChanges` Zeilen um, die niemand anfassen wollte. Das ist kein Gedankenspiel,
sondern der Normalfall auf jedem Lesepfad.

---

## Schritt 12: was verschwinden muss

Am Ende ist von Python nichts übrig. Gemessener Bestand:

```
apps/                zehn Python-Dienste       (apps/web bleibt!)
packages/            29 Einträge               (packages/ui bleibt!)
pyproject.toml       uv-Workspace
uv.lock
Makefile             die Python-Tore
tests/               Workspace-Tests
scripts/             initdb, validate.sh
docker/              service.Dockerfile, entrypoint.sh, traefik
docker-compose.yml   Python-Dienste
deploy/              Helm-Chart auf die Python-Dienste
Kon2.txt
var/, node_modules/, login-before.png
```

**Was bleibt:** `apps/web`, `packages/ui`, `package.json`, `pnpm-lock.yaml`,
`pnpm-workspace.yaml`, `turbo.json`, `tsconfig.base.json` — die React-App und ihr
Werkzeug. Dazu `docs/`, `bugs/`, `LICENSE`, `README.md`, `CLAUDE.md`,
`AGENTS.md`, `CONTRIBUTING.md`; die vier letzten werden auf .NET umgeschrieben,
nicht gelöscht.

**Reihenfolge:** `docker-compose.yml`, `deploy/` und die CI kommen ganz zum
Schluss, in einem Rutsch. Vorher zeigen sie auf beide Welten, und das ist
richtig so. Wenn Python weg ist, kann `dotnet/` flach in die Wurzel gezogen
werden — als eigener Commit, damit die Umbenennungen im Verlauf lesbar bleiben.

**`make check` passt du nicht an.** Es verschwindet mit Python. Solange beide
Welten laufen, fährst du die .NET-Reihe von Hand, in getrennten Aufrufen:

```bash
dotnet build dotnet/WorkerTransfer.slnx
dotnet test  dotnet/WorkerTransfer.slnx
```

CI und Kubernetes am Ende, wenn feststeht, was es zu bauen gibt. Merk dir dabei
aus der Python-Zeit: **die CI baut kein Docker-Image und kein Helm-Chart**, also
beweist eine grüne CI über einer Dockerfile-Änderung nichts.

---

## Was Girder von dir erwartet

Vier Entscheidungen, die in Girder festgeschrieben sind. Sie zu umgehen ist
möglich und jedes Mal ein Rückschritt.

**Zwei Token, und nur einer ist zurücknehmbar.** Der Zugriffstoken wird von jedem
Dienst allein aus der Signatur geprüft, lebt fünfzehn Minuten und lässt sich ohne
Widerrufsspeicher nicht zurücknehmen. Der Erneuerungstoken liegt als Zeile in der
Datenbank des Ausstellers. Abmelden heißt: die Sitzung beenden. Wer das Fenster
auf null will, hängt einen Widerrufsspeicher an — als Ausbaustufe, nicht als
Voraussetzung.

**Nur identity-service darf ausstellen.** Siehe oben.

**Girder protokolliert keine Werte.** Weder geschwärzt noch bereinigt: Befehle
und Anfragekörper erscheinen als *Form* — Feldnamen und Wertlängen. Bau das nicht
zurück. Schwärzen nach Liste entfernt nur, woran jemand gedacht hat, und ein Feld
namens `title` mit dem Inhalt „Termin bei Dr. Weber wegen der Kündigung" steht auf
keiner Liste. Wenn du irgendwo eine Nutzlast protokollieren willst, ist die
richtige Frage, ob du sie überhaupt brauchst.

**Aufräumen gehört dem Dienst.** Girder liefert `PurgeAsync` und ruft es nie.
Keine Hintergrundschleife in einer Bibliothek — das gilt auch für die Outbox.

---

## Wenn etwas nicht funktioniert: wessen Fehler ist es?

Bei jedem Fehler entscheidest du zuerst, **woher er kommt**. Das ist keine
Förmlichkeit — es ist die Stelle, an der man sonst um einen Bibliotheksfehler
herumprogrammiert und ihn damit für immer behält.

### Der Stapelabzug beantwortet die Frage nicht

Girders Pipeline liegt um jeden Handler herum, also steht sie in **jedem** Abzug:

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

Bleibt es unklar, die zweite Frage: **widerspricht das Verhalten dem, was Girders
README oder die XML-Dokumentation zusagt?** Eine Zusage, die nicht gilt, ist ein
Fehler. Etwas, das nirgends zugesagt ist, ist eine Lücke — und die gehört genauso
gemeldet.

### Wenn es Girders ist: anhalten

1. **Aufhören.** Nicht umgehen, nicht anpassen, nicht „vorläufig so lassen".
2. Ticket nach `bugs/<was-kaputt-ist>.md`, Vorlage in `bugs/VORLAGE.md`. Der
   kleine Testfall gehört **hinein**, nicht als Beschreibung, sondern als Code.
3. Melden: was kaputt ist, was es blockiert, ob es einen Umweg gäbe und was der
   kosten würde.
4. Auf die Entscheidung warten.

`bugs/README.md` erklärt, warum der Ordner existiert. Kurz: ein Umweg um einen
Bibliotheksfehler kostet zweimal — einmal beim Bauen, und ein zweites Mal, wenn
Girder den Fehler behebt und der Umweg stehen bleibt, ohne dass noch jemand weiß,
wofür er da war.

**Wenn der Benutzer einen Umweg entscheidet**, markiere ihn mit dem Ticketnamen:

```csharp
// Umweg für bugs/refresh-token-store-vergisst-subject.md — entfernen,
// sobald Girder das behoben hat.
```

### Und wenn es unserer ist

Dann ist es eine gewöhnliche Aufgabe: Test zuerst, der den Fehler zeigt, dann der
Code. Kein Ticket, keine Unterbrechung.

---

## Fallen, die dieses Projekt schon kennt

Aus `CLAUDE.md` und den 31 ADRs. Jede hat Geld oder Vertrauen gekostet. Sie
gelten für die **Begründung**, nicht für die Form — auch in einem neu
geschnittenen Dienst.

- **Mandant ist ein Unternehmen; eine natürliche Person hat keinen.** ADR-0017.
  Girders `Capacity` bildet genau das ab — `AsSelf` gegen `ForCompany`, kein
  nullbares `TenantId`.
- **Einwilligung wird synchron gelesen und nie zwischengespeichert.** ADR-0013.
  Ein Widerruf muss beim nächsten Lesen wirken. Kein `CachingBehavior` auf
  irgendetwas, das von einer Einwilligungsprüfung abhängt — auch nicht auf dem
  *Ergebnis*, denn darin steckt die Entscheidung mit drin.
- **`404` heißt „verborgen oder nicht vorhanden", und beide Antworten müssen
  byte-gleich sein.** Wer das „aufräumt", baut einen Aufzählungskanal.
- **`GRANTED` heißt „wurde einmal gewährt", nicht „gilt jetzt."** Deshalb hat die
  Anfrage weder `is_active` noch `revoked_at`.
- **Die Löschung hat kein Begründungsfeld**, und die Ausnahme von der Löschung
  ist eine **Konstante, keine Einstellung**. Bei einem Löschversprechen ist
  „in Produktion anders als im Test" der schlechteste denkbare Zustand.
- **Skill-Vokabular benennt um, es folgert nie.** ADR-0023. `"Postgres" ==
  "PostgreSQL"` ist eine Aussage über Sprache und erlaubt; `"React impliziert
  JavaScript"` ist eine Aussage über einen *Menschen* und verboten.
- **Passung wird im Browser berechnet und existiert sonst nirgends.** Kein
  serverseitiges Matching, kein Score, keine Rangliste von Menschen, keine
  Prozentzahl — sie verbirgt genau das Einzige, was hilft: *welche* Fähigkeit
  fehlt.
- **Rollen kommen aus der Mitgliedschaftstabelle, nie aus dem Token.** Der Token
  sagt, *für welches* Unternehmen jemand handelt, nie *mit welchem Recht*.
- **Der KI-Entwurf speichert nichts** und sagt nie etwas *über* jemanden.
  ADR-0024. Kein Gedächtnis, kein Vektorspeicher, kein Vorschlag im Hintergrund.

---

## Wie du arbeiten sollst

Der Benutzer hat im Verlauf mehrfach dasselbe eingefordert, und es hat jedes Mal
echte Fehler gefunden:

1. **Tests zuerst festlegen, dann den Code anpassen.** Nicht den Test
   umschreiben, bis er grün wird. Wenn ein Test fällt, ist erst zu klären, ob der
   Code falsch ist.
2. **Gegenprobe fahren.** Ein Test, der auch bei kaputtem Code grün bleibt,
   beweist nichts. Brich die Regel absichtlich und sieh nach, ob der Test fällt —
   und achte darauf, dass der Bruch **übersetzt**: ein Build-Fehler sieht in der
   Ausgabe aus wie ein bestandener Test.
3. **Nichts blind übernehmen.** Girder entstand aus Skillswap, und ein großer
   Teil der Arbeit bestand darin, Übernommenes wieder zu löschen. Frage bei jeder
   Datei, ob sie an dieser Ebene richtig ist und ob es sie überhaupt braucht.
4. **Kommentare sagen was und warum, nicht die Historie.** Keine Sätze wie
   „früher stand hier…". Entscheidungen gehören in ADRs. Sparsam kommentieren —
   das wird vielleicht ein Open-Source-Projekt.
5. **Plan vorlegen und Zustimmung holen, bevor gelöscht wird.**
6. **Ehrlich berichten.** Wenn Tests fallen, sag es mit Ausgabe. Wenn etwas
   übersprungen wurde, sag das auch.
7. **Bei jedem Fehler zuerst klären, wessen er ist.** Aus Girder heißt anhalten,
   Ticket nach `bugs/`, melden, warten. Nicht umgehen.
8. **Messen statt vermuten.** Jede Behauptung über das alte System, über Girder
   oder über eine Bibliothek wird nachgesehen, bevor sie in einen Plan geht.

---

## Erster Auftrag

> Der Zweig ist `dotnet-migration`, von `develop` abgezweigt. Arbeite darauf.
>
> **Schritt 2: identity-service vollständig.** Alle Routen, Commands, Queries und
> deren Handler; Dienstmethoden in Application; Repository-Schnittstellen in
> Domain, Umsetzungen in Infrastructure; Contracts nur in Api; die gesamte
> DI-Registrierung in Infrastructure, in Api nur `AddIdentityInfrastructure()`
> plus Girder-Modulauswahl und Pipeline.
>
> Miss zuerst, was `apps/identity-service` heute beantwortet — alle Routen, die
> Modelle, die Regeln. Entscheide dann, was davon so bleibt und was du anders
> schneidest, und **begründe beides**. Python ist Vorlage, nicht Maßstab.
>
> Beantworte in deinem Plan ausdrücklich:
> - Mediator ja oder nein, und warum
> - wohin Domänenereignisse gehen und wie die Outbox für die Löschkaskade
>   geschnitten wird
> - welche Girder-Pakete in welche Schicht kommen und welche dieser Dienst nicht
>   braucht
>
> Wenn ein Fehler auftaucht: erst klären, ob er aus WorkerTransfer oder aus
> Girder kommt. Aus Girder heißt anhalten, Ticket nach `bugs/`, melden.
>
> Erst nach meiner Zustimmung anfangen.

**Beantwortet, damit es niemand zweimal herleitet:** Change Tracking steht in
seinem eigenen Abschnitt; die Tokenform ist gemessen und trägt zwei
`CustomClaims`, nicht vier; was nur wegen des Übergangs existiert, führt
`docs/uebergang-python-dotnet.md` und verschwindet in Schritt 12.

---

## Zum Schluss: was Girder noch fehlt

Damit du es nicht suchst. Nichts davon blockiert Schritt 2.

| | |
|---|---|
| **Ausstellen ohne `aud`** | Geht nicht, und deshalb muss `worker_auth` es ignorieren. `bugs/jwtservice-kann-nicht-ohne-aud-ausstellen.md` |
| **Listenwertige `CustomClaims`** | Geht nicht — ein `Dictionary<string, string>` kann keine Liste. `bugs/customclaims-kann-keine-liste-ausdruecken.md` |
| **Outbox** | Gibt es nicht. Gebraucht bei identity (Löschkaskade) und transfer |
| **Transaktions-Behavior** | Gibt es nicht. Die UnitOfWork je Anfrage ist unsere |
| **`ILogSanitizer` ohne Konsumenten** | Bleibt als Werkzeug; Girder benutzt es seit dem Umstieg auf Formen nicht mehr |
| **`DataProtectionSecretProvider` ohne Konsumenten** | ASP.NETs Schlüsselring wird angelegt und von nichts benutzt |
| **Doppelte Typnamen** | `CacheStatistics`, `RateLimitResult`, zwei Prüfspur-Systeme |
| **Drei Wege zu einer Berechtigung** | `PermissionMiddleware`, `PermissionPolicyProvider`, per-Berechtigung-Richtlinien — sie antworten nicht gleich |
| **Deutsche Fehlertexte** | `ErrorMessageService` gibt Nutzertexte auf Deutsch zurück |

Der Fahrplan in Girders README führt sie mit Begründung.
