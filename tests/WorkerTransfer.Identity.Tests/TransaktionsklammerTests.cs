using FluentAssertions;
using Noelia.Abstractions.Security.Sessions;
using Noelia.Core.Identity;
using Noelia.Data.EntityFrameworkCore;
using Noelia.Data.EntityFrameworkCore.Sessions;
using Noelia.Infrastructure.Security.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WorkerTransfer.Identity.Tests;

/// <summary>A context that holds nothing but Noelia's own table.</summary>
public sealed class NurNoeliaKontext(DbContextOptions<NurNoeliaKontext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        // Its own name, so this probe cannot collide with the table the
        // service's own migration creates in the same database.
        modelBuilder.ConfigureNoeliaRefreshTokens("probe_refresh_tokens");
    }
}

/// <summary>
/// Whether Noelia's session store can run inside a transaction the application
/// opened.
/// </summary>
/// <remarks>
/// The audit trail has to commit together with the change it records, which
/// means one transaction per command. Noelia's refresh-token store manages its
/// own atomicity and opens a transaction of its own while consuming a token —
/// so whether the two compose decides how the sign-in commands are cut.
/// <para>
/// Deliberately built out of Noelia alone. It began as the reproduction for a
/// Noelia bug — the store opened a second transaction and threw
/// <c>"The connection is already in a transaction"</c> — fixed in 3.0.1. The
/// ticket is gone; the test stays, because what it pins is ours: that the two
/// sign-in routes really do commit their audit row together with the change.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class TransaktionsklammerTests(Postgres postgres) : IAsyncLifetime
{
    private ServiceProvider _anbieter = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.Configure<TokenSessionOptions>(_ => { });
        services.AddDbContext<NurNoeliaKontext>(
            options => options.UseNpgsql(postgres.ConnectionString));
        services.AddEntityFrameworkRefreshTokens<NurNoeliaKontext>();

        // `SessionObservations` ist seit 5.1.0 ein Singleton und der fuenfte
        // Konstruktorparameter von `TokenSessionService` (MIGRATION.md:451).
        // Vorher war die Menge der beobachteten Subjekte ein Instanzfeld an
        // einem `AddScoped`-Dienst — je Anfrage ein neues, leeres Woerterbuch,
        // weshalb die Sitzungsuebersicht dauerhaft „0 active sessions" meldete.
        //
        // Diese Reihe baut den Dienst selbst, statt das Modul zu fahren; sie
        // muss das Singleton deshalb selbst stellen. Eine Ueberladung mit vier
        // Parametern gibt es bewusst nicht — sie legte sich ihre eigene Menge an
        // und stellte damit genau den Defekt lautlos wieder her.
        services.AddSingleton<SessionObservations>();
        services.AddScoped<ITokenSessionService, TokenSessionService>();

        _anbieter = services.BuildServiceProvider();

        using var bereich = _anbieter.CreateScope();
        var kontext = bereich.ServiceProvider.GetRequiredService<NurNoeliaKontext>();

        // Not EnsureCreated: the database already carries the Alembic schema,
        // so it would decide there is nothing to do. xunit builds one
        // instance per test, hence the check.
        var vorhanden = await kontext.Database
            .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM pg_class WHERE relname = 'probe_refresh_tokens'")
            .AnyAsync();

        if (!vorhanden)
        {
            await ((RelationalDatabaseCreator)kontext.Database.GetService<IDatabaseCreator>())
                .CreateTablesAsync();
        }
    }

    public async Task DisposeAsync() => await _anbieter.DisposeAsync();

    /// <summary>
    /// Starting a sign-in composes: <c>CreateAsync</c> only saves, and a save
    /// joins whatever transaction is open.
    /// </summary>
    [Fact]
    public async Task Eine_Anmeldung_laeuft_in_einer_offenen_Transaktion()
    {
        using var bereich = _anbieter.CreateScope();
        var kontext = bereich.ServiceProvider.GetRequiredService<NurNoeliaKontext>();
        var sitzungen = bereich.ServiceProvider.GetRequiredService<ITokenSessionService>();

        await using var klammer = await kontext.Database.BeginTransactionAsync();

        var angemeldet = await sitzungen.SignInAsync(SubjectId.New());
        await klammer.CommitAsync();

        angemeldet.RefreshToken.Should().NotBeEmpty();
    }

    /// <summary>
    /// Und eine Erneuerung ebenso: sie tritt der offenen Klammer bei, statt eine
    /// zweite zu öffnen, und die Rotation wird mit der Arbeit des Aufrufers
    /// dauerhaft.
    /// </summary>
    /// <remarks>
    /// Bis Girder 3.0.1 warf diese Stelle
    /// <c>"The connection is already in a transaction"</c> — der Grund, warum
    /// <c>POST /auth/refresh</c> und <c>/auth/logout</c> eine Weile rot standen.
    /// </remarks>
    [Fact]
    public async Task Eine_Erneuerung_laeuft_in_einer_offenen_Transaktion()
    {
        using var bereich = _anbieter.CreateScope();
        var kontext = bereich.ServiceProvider.GetRequiredService<NurNoeliaKontext>();
        var sitzungen = bereich.ServiceProvider.GetRequiredService<ITokenSessionService>();

        var angemeldet = await sitzungen.SignInAsync(SubjectId.New());

        await using var klammer = await kontext.Database.BeginTransactionAsync();
        var erneuert = await sitzungen.RefreshAsync(angemeldet.RefreshToken);
        await klammer.CommitAsync();

        erneuert.Succeeded.Should().BeTrue();
        erneuert.RefreshToken.Should().NotBeNullOrEmpty()
            .And.NotBe(angemeldet.RefreshToken, "eine Erneuerung rotiert den Token");

        // Nach dem Commit des Aufrufers: der alte Token ist wirklich verbraucht.
        // Ihn erneut vorzulegen nimmt jetzt den Zweig für eine bereits gedrehte
        // Zeile — innerhalb des Kulanzfensters also ein Geschwister, kein
        // Diebstahl. Das beweist, dass die Rotation an der Klammer des Aufrufers
        // hing und dessen Commit überlebt hat; ohne sie stünde hier Rotated.
        var nochmal = await sitzungen.RefreshAsync(angemeldet.RefreshToken);
        nochmal.Outcome.Should().Be(ConsumeOutcome.RotatedWithinGrace);
    }

    /// <summary>
    /// Und eine Abmeldung ebenso — die zweite Route aus demselben Ticket.
    /// </summary>
    /// <remarks>
    /// <c>POST /auth/logout</c> ist zusammengesetzt: erst den Token verbrauchen,
    /// um die Sitzung zu kennen, dann die Sitzung schliessen. Beide Schritte
    /// liegen in der Klammer des Aufrufers, weil daneben die Pruefspurzeile
    /// <c>token_revoke</c> faellt — und ein Eintrag, der ohne die Abmeldung
    /// committet, behauptet etwas, das nicht geschehen ist.
    /// <para>
    /// Bis Girder 3.0.1 stand hier derselbe Fehler wie eine Zeile hoeher. Der
    /// Test steht trotzdem eigens da: die Erneuerung deckt nur den ersten der
    /// beiden Schritte ab, und <c>SignOutAsync</c> ist ein anderer Aufruf.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Eine_Abmeldung_laeuft_in_einer_offenen_Transaktion()
    {
        using var bereich = _anbieter.CreateScope();
        var kontext = bereich.ServiceProvider.GetRequiredService<NurNoeliaKontext>();
        var sitzungen = bereich.ServiceProvider.GetRequiredService<ITokenSessionService>();

        var angemeldet = await sitzungen.SignInAsync(SubjectId.New());

        await using var klammer = await kontext.Database.BeginTransactionAsync();

        var verbraucht = await sitzungen.RefreshAsync(angemeldet.RefreshToken);

        if (verbraucht is not { Session: { } sitzung })
        {
            throw new InvalidOperationException(
                "die Erneuerung nannte keine Sitzung — der Test prueft dann nichts");
        }

        await sitzungen.SignOutAsync(sitzung);
        await klammer.CommitAsync();

        // Nach dem Commit des Aufrufers ist die Sitzung wirklich zu. Ohne die
        // Abmeldung traege der eben gedrehte Token noch das Kulanzfenster und
        // wuerde erneuern — deshalb ist genau er die Probe.
        var danach = await sitzungen.RefreshAsync(verbraucht.RefreshToken!);
        danach.Succeeded.Should().BeFalse(
            "die Abmeldung lag in derselben Klammer und hat deren Commit ueberlebt");
    }
}
