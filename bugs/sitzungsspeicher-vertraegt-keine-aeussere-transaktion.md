# Der Sitzungsspeicher scheitert, wenn der Aufrufer schon eine Transaktion offen hat

- **Girder-Fassung:** 3.0.0
- **Gefunden beim:** identity-service, Schritt 2 (Prüfspur und Löschkaskade)
- **Art:** Lücke — zugesagt ist nichts, und die Zusage fehlt genau dort, wo
  Girder selbst das Muster empfiehlt
- **Blockiert:** teilweise — `/auth/refresh` und `/auth/logout` können keine
  Transaktionsklammer bekommen, die übrigen neunzehn Routen schon

## Was passiert

`TokenSessionService.RefreshAsync` scheitert, sobald auf demselben `DbContext`
bereits eine Transaktion offen ist:

```
System.InvalidOperationException : The connection is already in a transaction
and cannot participate in another transaction.
   at Microsoft.EntityFrameworkCore.Storage.RelationalConnection.EnsureNoTransactions()
   at Microsoft.EntityFrameworkCore.Storage.RelationalConnection.BeginTransactionAsync(…)
   at Girder.Data.EntityFrameworkCore.Sessions.EntityFrameworkRefreshTokenStore`1.TryConsumeAsync(…)
```

`EntityFrameworkRefreshTokenStore.TryConsumeAsync` öffnet eine eigene
Transaktion (`context.Database.BeginTransactionAsync`). EF Core lässt keine
zweite zu; der Aufruf endet in `EnsureNoTransactions`.

Die anderen Wege des Speichers sind davon nicht betroffen: `CreateAsync` ruft
nur `SaveChangesAsync`, und ein Speichern tritt einer offenen Transaktion bei.
**Anmelden geht also, Erneuern nicht** — die Grenze verläuft mitten durch
dieselbe Schnittstelle.

## Warum es Girders ist

Reproduziert ohne eine Zeile WorkerTransfer-Code. Ein `DbContext`, der nichts
enthält als Girders eigene Tabelle:

```csharp
public sealed class NurGirderKontext(DbContextOptions<NurGirderKontext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ConfigureGirderRefreshTokens("probe_refresh_tokens");
}

// Verdrahtung: AddLogging, AddSingleton(TimeProvider.System),
// Configure<TokenSessionOptions>(_ => { }),
// AddDbContext<NurGirderKontext>(o => o.UseNpgsql(…)),
// AddEntityFrameworkRefreshTokens<NurGirderKontext>(),
// AddScoped<ITokenSessionService, TokenSessionService>()

[Fact]
public async Task Eine_Anmeldung_laeuft_in_einer_offenen_Transaktion()
{
    await using var klammer = await kontext.Database.BeginTransactionAsync();

    var angemeldet = await sitzungen.SignInAsync(SubjectId.New());   // geht
    await klammer.CommitAsync();

    angemeldet.RefreshToken.Should().NotBeEmpty();
}

[Fact]
public async Task Eine_Erneuerung_laeuft_nicht_in_einer_offenen_Transaktion()
{
    var angemeldet = await sitzungen.SignInAsync(SubjectId.New());

    await using var klammer = await kontext.Database.BeginTransactionAsync();

    var erneuern = async () => await sitzungen.RefreshAsync(angemeldet.RefreshToken);

    (await erneuern.Should().ThrowAsync<InvalidOperationException>())
        .WithMessage("*already in a transaction*");
}
```

Beide laufen grün — der erste, weil es geht, der zweite, weil es nicht geht. Sie
liegen als `TransaktionsklammerTests` in
`dotnet/tests/WorkerTransfer.Identity.Tests/` und bleiben dort, bis das hier
entschieden ist.

**Zugesagt ist nichts**, weder in der README noch in der XML-Dokumentation —
deshalb Lücke und nicht Fehler. Die Stelle, an der die Zusage fehlt, ist
allerdings Girders eigenes Argument. Zur Outbox sagt die README:

> „**No transactional outbox.** Recording an intent in the same transaction as
> the change that caused it is infrastructure and belongs here"

Genau dieses Muster — die Absicht in derselben Transaktion wie die
Domänenänderung — ist es, das am Sitzungsspeicher zerbricht. Und der
Kommentar an `GirderRefreshToken` begründet die Bauform ausdrücklich mit
Atomarität:

> „consuming a token has to be one conditional update, and a join would turn the
> atomicity the contract promises back into an intention."

Das Argument trägt. Es sagt nur nichts darüber, was gilt, wenn der Aufrufer
seinerseits eine Klammer hat.

## Was es uns kostet

Bei WorkerTransfer schreibt jeder sicherheitsrelevante Befehl eine Zeile nach
`audit_events`, und zwar **in derselben Transaktion** wie die Änderung, die sie
festhält (ADR-0012). Ein Eintrag, der getrennt committet, kann ein Rollback
überleben oder nach einem Commit fehlen — beide Male steht im Protokoll etwas,
das so nicht geschehen ist. In Python hält ein `request_scope` je Anfrage das
zusammen, und es gibt einen Integrationstest dafür.

Für neunzehn der einundzwanzig Routen ist das kein Problem. Zwei sind betroffen:

- `POST /auth/refresh` — Rotation plus `token_refresh`, und im Fall einer
  erloschenen Mitgliedschaft zusätzlich `tenant_switch_denied` und das
  Zurücksetzen der Handlungsform.
- `POST /auth/logout` — Sitzungsende plus `token_revoke`.

**Die Löschkaskade ist nicht betroffen**, und das ist die gute Nachricht: sie
ruft `SignOutEverywhereAsync`, das über `ExecuteUpdateAsync` läuft und einer
offenen Transaktion beitritt. Der Vollständigkeitsbeweis aus ADR-0027 steht
also nicht auf dem Spiel.

### Der Umweg, den wir nicht genommen haben

Man könnte `ErneuernBefehl` und `AbmeldenBefehl` von der Transaktionsklammer
ausnehmen. Er kostet zweierlei:

1. Die Prüfspur wäre auf zwei Routen schwächer als auf den anderen neunzehn —
   und zwar unsichtbar, weil der Unterschied nur bei einem Abbruch mitten im
   Befehl auftritt.
2. Die Ausnahme steht als `if` in einem Behavior, das es gerade deshalb gibt,
   weil ein Handler, der die Klammer vergisst, derjenige ist, den niemand
   bemerkt.

## Die Behebung

`TryConsumeAsync` soll die Transaktion nur dann selbst öffnen, wenn keine offen
ist — und andernfalls die vorhandene benutzen. Das ist die übliche Form für
Infrastruktur, die in einer fremden Arbeitseinheit laufen kann:

```csharp
var vorhanden = context.Database.CurrentTransaction;
await using var eigene = vorhanden is null
    ? await context.Database.BeginTransactionAsync(cancellationToken)
    : null;
…
if (eigene is not null)
{
    await eigene.CommitAsync(cancellationToken);
}
```

Der Rollback-Pfad in `TryConsumeAsync` (`claimed == 0`) braucht dabei die
zweite Hälfte der Überlegung: heute rollt er die eigene Transaktion zurück und
liest neu. Innerhalb einer fremden Klammer darf er das nicht — er würde die
Arbeit des Aufrufers mitreißen. Dort ist ein **Savepoint** das richtige
Werkzeug (`CreateSavepointAsync` / `RollbackToSavepointAsync`), den EF Core und
Npgsql beide können.

**Nicht anfassen:** die Bauform der Tabelle. Dass die Sitzungsmerkmale auf jeder
Zeile wiederholt werden statt in einer zweiten Tabelle zu stehen, ist richtig
begründet und steht hier nicht zur Debatte.

**Regressionstests**, dauerhaft in Girders Suite:

- Die zwei Testfälle oben, wobei der zweite sich umdreht: die Erneuerung läuft
  in der offenen Klammer durch, und die Zeile ist nach dem `CommitAsync` des
  Aufrufers da.
- Ein Rollback des **Aufrufers** nach einer erfolgreichen Erneuerung lässt den
  alten Token wieder gelten. Das ist der Test, der beweist, dass wirklich eine
  Klammer und nicht zwei liefen.
- Der Wiederverwendungsfall (`claimed == 0`) innerhalb einer fremden Klammer:
  der Aufrufer behält seine Arbeit, und `ReuseDetected` kommt trotzdem heraus.
- Gegenprobe: nimm die Fallunterscheidung heraus und sieh nach, dass die Tests
  fallen. Achte darauf, dass der Bruch **übersetzt**.

**Danach:** Fassung anheben, veröffentlichen, `GirderVersion` in
`dotnet/Directory.Packages.props` nachziehen — und hier den zweiten Testfall
umdrehen.

**Bauen und Testen in getrennten Aufrufen.**

## Stand

- [x] gemeldet
- [ ] in Girder behoben, Fassung: <…>
- [ ] Umweg hier entfernt — es ist keiner entstanden, die Klammer fehlt
      einstweilen auf zwei Routen
