using FluentAssertions;
using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace WorkerTransfer.Outbox.Tests;

/// <summary>A delivery a test can steer.</summary>
public sealed class Probezustellung : IZustellung
{
    private readonly Func<SubjectId, string, Task> _verhalten;

    public Probezustellung(Func<SubjectId, string, Task>? verhalten = null) =>
        _verhalten = verhalten ?? ((_, _) => Task.CompletedTask);

    public List<(SubjectId Empfaenger, string Art)> Zugestellt { get; } = [];

    public async Task ZustelleAsync(
        SubjectId empfaenger, string art, CancellationToken cancellationToken = default)
    {
        await _verhalten(empfaenger, art);
        Zugestellt.Add((empfaenger, art));
    }
}

/// <summary>The outbox, against a real database.</summary>
[Collection(PostgresCollection.Name)]
public class OutboxTests(Postgres postgres)
{
    private Kontextfabrik Fabrik() => new(postgres.ConnectionString);

    private OutboxZusteller<ProbeKontext> Zusteller(
        IZustellung zustellung, OutboxEinstellungen? einstellungen = null) =>
        new(Fabrik(), zustellung, TimeProvider.System,
            NullLogger<OutboxZusteller<ProbeKontext>>.Instance, einstellungen);

    private async Task<SubjectId> Vermerke(string art, int wie_viele = 1)
    {
        var wer = SubjectId.New();

        await using var kontext = Fabrik().CreateDbContext();
        var outbox = new EfOutbox<ProbeKontext>(kontext, TimeProvider.System);

        for (var i = 0; i < wie_viele; i++)
        {
            await outbox.VermerkeAsync(wer, art);
        }

        await kontext.SaveChangesAsync();
        return wer;
    }

    private async Task<OutboxZeile> Zeile(SubjectId wer)
    {
        await using var kontext = Fabrik().CreateDbContext();
        return await kontext.Set<OutboxZeile>().SingleAsync(z => z.UserId == wer.Value);
    }

    /// <summary>
    /// Written independently, the notification could exist while the change was
    /// rolled back — somebody would be told their transfer was accepted and the
    /// database would hold nothing of the sort.
    /// </summary>
    [Fact]
    public async Task Ein_Rollback_nimmt_die_Absicht_mit()
    {
        var wer = SubjectId.New();

        await using (var kontext = Fabrik().CreateDbContext())
        {
            await using var klammer = await kontext.Database.BeginTransactionAsync();
            await new EfOutbox<ProbeKontext>(kontext, TimeProvider.System)
                .VermerkeAsync(wer, "transfer.accepted");
            await kontext.SaveChangesAsync();
            await klammer.RollbackAsync();
        }

        await using var gelesen = Fabrik().CreateDbContext();
        (await gelesen.Set<OutboxZeile>().CountAsync(z => z.UserId == wer.Value))
            .Should().Be(0);
    }

    [Fact]
    public async Task Eine_vermerkte_Absicht_wird_zugestellt_und_abgehakt()
    {
        var wer = await Vermerke("transfer.accepted");
        var zustellung = new Probezustellung();

        var (faellig, zugestellt) = await Zusteller(zustellung).DurchlaufAsync();

        faellig.Should().BeGreaterThan(0);
        zugestellt.Should().BeGreaterThan(0);
        zustellung.Zugestellt.Should().Contain((wer, "transfer.accepted"));
        (await Zeile(wer)).DeliveredAt.Should().NotBeNull();
    }

    /// <summary>
    /// Only the kind of failure, never the other side's answer — a recipient
    /// that echoes a request body would otherwise write it into a table that
    /// ends up in every backup.
    /// </summary>
    [Fact]
    public async Task Ein_Fehlschlag_zaehlt_hoch_und_speichert_nur_die_Fehlerart()
    {
        var wer = await Vermerke("transfer.accepted");
        var zustellung = new Probezustellung((_, _) =>
            throw new HttpRequestException("Anna Müller, geboren 1984, wohnhaft ..."));

        await Zusteller(zustellung).DurchlaufAsync();

        var zeile = await Zeile(wer);
        zeile.Attempts.Should().Be(1);
        zeile.DeliveredAt.Should().BeNull();
        zeile.LastError.Should().Be("HttpRequestException");
        zeile.LastError.Should().NotContain("Anna");
    }

    /// <summary>
    /// Not a failure, so no attempt and no trace. Offering the row again on the
    /// next tick IS how the order of ADR-0027 §6 is kept.
    /// </summary>
    [Fact]
    public async Task Noch_nicht_verbraucht_keinen_Versuch()
    {
        var wer = await Vermerke("erasure:final");
        var zustellung = new Probezustellung(
            (_, _) => throw new NochNichtException("erst quittieren lassen"));

        var (faellig, zugestellt) = await Zusteller(zustellung).DurchlaufAsync();

        faellig.Should().BeGreaterThan(0);
        zugestellt.Should().Be(0);

        var zeile = await Zeile(wer);
        zeile.Attempts.Should().Be(0, "geordnetes Warten ist kein Fehlschlag");
        zeile.LastError.Should().BeEmpty();
        zeile.DeliveredAt.Should().BeNull();
    }

    /// <summary>
    /// Giving up is not deleting. A row that vanishes quietly is exactly the
    /// state this table abolishes.
    /// </summary>
    [Fact]
    public async Task Nach_der_Obergrenze_bleibt_die_Zeile_liegen_statt_zu_verschwinden()
    {
        var wer = await Vermerke("transfer.accepted");
        var zustellung = new Probezustellung((_, _) => throw new HttpRequestException("weg"));
        var einstellungen = new OutboxEinstellungen { HoechsteVersuche = 2 };

        for (var i = 0; i < 5; i++)
        {
            await Zusteller(zustellung, einstellungen).DurchlaufAsync();
        }

        var zeile = await Zeile(wer);
        zeile.Attempts.Should().Be(2, "danach ist sie nicht mehr fällig");
        zeile.DeliveredAt.Should().BeNull();
    }

    /// <summary>
    /// For an erasure, "give up after ten" would be the silent failure ADR-0027
    /// exists against: a promise nobody redeems, and nobody sees it.
    /// </summary>
    [Fact]
    public async Task Ohne_Obergrenze_bleibt_die_Zeile_faellig()
    {
        var wer = await Vermerke("erasure:consent");
        var zustellung = new Probezustellung((_, _) => throw new HttpRequestException("tot"));
        var einstellungen = new OutboxEinstellungen { HoechsteVersuche = null };

        for (var i = 0; i < 12; i++)
        {
            await Zusteller(zustellung, einstellungen).DurchlaufAsync();
        }

        var zeile = await Zeile(wer);
        zeile.Attempts.Should().Be(12, "wer nie aufgibt, bleibt fällig");
        zeile.DeliveredAt.Should().BeNull();
    }

    /// <summary>
    /// Two dispatchers, one table. Without the lock both grab the same rows and
    /// every intent goes out twice — which is why the Python chart is pinned to
    /// a single replica.
    /// </summary>
    /// <remarks>
    /// The competitor is a plain transaction holding the same lock, not a second
    /// dispatcher waiting on a signal: a test that coordinates two background
    /// passes can hang instead of failing, and a hanging test says nothing.
    /// </remarks>
    [Fact]
    public async Task Gesperrte_Zeilen_werden_uebersprungen_statt_doppelt_zugestellt()
    {
        await Vermerke("transfer.accepted", wie_viele: 5);

        await using var konkurrent = Fabrik().CreateDbContext();
        await using var klammer = await konkurrent.Database.BeginTransactionAsync();

        var gesperrt = await konkurrent.Set<OutboxZeile>()
            .FromSqlRaw(
                "SELECT * FROM \"outbox\" WHERE delivered_at IS NULL "
                + "ORDER BY created_at FOR UPDATE SKIP LOCKED")
            .ToListAsync();

        gesperrt.Should().NotBeEmpty("sonst prüft der Test nichts");

        var zustellung = new Probezustellung();
        var (faellig, zugestellt) = await Zusteller(zustellung).DurchlaufAsync();

        faellig.Should().Be(0, "die gesperrten Zeilen gehören dem anderen Durchlauf");
        zugestellt.Should().Be(0);
        zustellung.Zugestellt.Should().BeEmpty();

        await klammer.RollbackAsync();
    }
}
