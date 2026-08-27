using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Girder.Core.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Loeschung;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Vorgaenge;
using WorkerTransfer.Transfer.Infrastructure.Loeschung;
using WorkerTransfer.Transfer.Infrastructure.Persistence;

namespace WorkerTransfer.Transfer.Tests;

/// <summary>Die Löschung — und der zweite Schalter, der auf aus steht.</summary>
[Collection(PostgresCollection.Name)]
public class LoeschungTests(Postgres postgres) : IAsyncLifetime
{
    private const string Loeschgeheimnis = "loesch-geheimnis";

    private WebApplicationFactory<Program> _dienst = null!;

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:transfer", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", Loeschgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
            {
                dienste.Replace(ServiceDescriptor.Scoped<IEinwilligungstor, Probeledger>());
                dienste.Replace(ServiceDescriptor.Scoped<IZustellung, Probezustellung>());
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    /// <summary><strong>Die zweite tragende Zusage des Systems.</strong></summary>
    /// <remarks>
    /// Ein Betrag, auf den sich zwei Unternehmen geeinigt haben, macht die
    /// Zeile nicht zur Unterlage eines Vermittlers: die Plattform führt kein
    /// Geld. Fällt dieser Test, ist die Voreinstellung gekippt.
    /// </remarks>
    [Fact]
    public void Der_Schalter_steht_auf_aus()
    {
        Aufbewahrung.BezahlteBehalten.Should().BeFalse();
    }

    /// <summary>
    /// Die Abgrenzung ist ausgeschrieben: <c>declined</c> und <c>withdrawn</c>
    /// gehören <em>nicht</em> dazu.
    /// </summary>
    /// <remarks>
    /// Ein abgesagter Vorgang begründet nichts. Diese Liste ist deshalb
    /// bewusst nicht die Endzustandsmenge des Aggregats.
    /// </remarks>
    [Fact]
    public void Abgeschlossen_heisst_accepted_oder_completed()
    {
        Aufbewahrung.Abgeschlossene.Should().Equal(
            Transferstand.Accepted, Transferstand.Completed);
    }

    /// <summary>„Erledigt" heißt erledigt — auch der bezahlte Transfer.</summary>
    [Fact]
    public async Task Die_Voreinstellung_loescht_auch_den_bezahlten_Transfer()
    {
        var anna = Guid.CreateVersion7();
        var firma = Guid.CreateVersion7();
        var bezahlt = await Vorgang(anna, firma, "completed", gebuehr: 500_000);
        var laufend = await Vorgang(anna, Guid.CreateVersion7(), "talking");
        await Status(anna);

        var antwort = await Loesche(anna);

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);

        var quittung = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;
        quittung.GetProperty("retained").GetInt32().Should().Be(0);

        await using var kontext = postgres.Kontext();
        (await kontext.Vorgaenge.AnyAsync(zeile => zeile.Id == bezahlt)).Should().BeFalse();
        (await kontext.Vorgaenge.AnyAsync(zeile => zeile.Id == laufend)).Should().BeFalse();
        (await kontext.Marktstatus.AnyAsync(zeile => zeile.Id == anna)).Should().BeFalse();
    }

    /// <summary>
    /// Der umgelegte Schalter deckt <strong>genau eine</strong> Zeilenklasse
    /// ab: ein abgeschlossener Handel <em>mit</em> Vergütung.
    /// </summary>
    /// <remarks>
    /// Keine Ausdehnung auf <c>interested</c>/<c>talking</c>/<c>offered</c> —
    /// ein Gespräch ist kein Vertrag — und ohne Vergütung ist kein
    /// Handelsvorgang entstanden, an dem etwas hängen könnte.
    /// </remarks>
    [Fact]
    public async Task Der_umgelegte_Schalter_deckt_genau_den_bezahlten_Abschluss_ab()
    {
        var anna = Guid.CreateVersion7();
        var abgeschlossenBezahlt = await Vorgang(
            anna, Guid.CreateVersion7(), "completed", gebuehr: 500_000);
        var angenommenBezahlt = await Vorgang(
            anna, Guid.CreateVersion7(), "accepted", gebuehr: 1);
        var abgeschlossenUnbezahlt = await Vorgang(anna, Guid.CreateVersion7(), "completed");
        var angebotBezahlt = await Vorgang(
            anna, Guid.CreateVersion7(), "offered", gebuehr: 500_000);
        var abgesagtBezahlt = await Vorgang(
            anna, Guid.CreateVersion7(), "declined", gebuehr: 500_000);

        await using var kontext = postgres.Kontext();
        var bestand = new EfLoeschbestand(kontext);

        var behalten = await bestand.LoescheAsync(
            new SubjectId(anna), bezahlteBehalten: true);

        behalten.Should().Be(2);

        var uebrig = await kontext.Vorgaenge
            .Where(zeile => zeile.SubjectId == anna)
            .Select(zeile => zeile.Id)
            .ToListAsync();

        uebrig.Should().BeEquivalentTo([abgeschlossenBezahlt, angenommenBezahlt]);
        uebrig.Should().NotContain(
            [abgeschlossenUnbezahlt, angebotBezahlt, abgesagtBezahlt]);
    }

    /// <summary>
    /// Was ÜBER die Person gesagt wurde, fällt. Was sie FÜR ihr Unternehmen
    /// tat, bleibt — ohne ihren Namen.
    /// </summary>
    /// <remarks>
    /// Ohne diese Unterscheidung löschte ein Recruiter, der sein privates Konto
    /// aufgibt, Vorgänge seines Arbeitgebers, die von jemand ganz anderem
    /// handeln.
    /// </remarks>
    [Fact]
    public async Task Gefragt_worden_faellt_gefragt_haben_bleibt_ohne_Namen()
    {
        var recruiter = Guid.CreateVersion7();
        var ueberIhn = await Anfrage(recruiter, Guid.CreateVersion7(), Guid.CreateVersion7());
        var vonIhm = await Anfrage(Guid.CreateVersion7(), Guid.CreateVersion7(), recruiter);

        await Loesche(recruiter);

        await using var kontext = postgres.Kontext();

        (await kontext.Anfragen.AnyAsync(zeile => zeile.Id == ueberIhn)).Should().BeFalse();

        var geblieben = await kontext.Anfragen.SingleAsync(zeile => zeile.Id == vonIhm);
        geblieben.RequestedBy.Should().BeNull("der Vorgang gehört dem Unternehmen");
    }

    /// <summary>Ein ausstehender Vermerk an ein Konto, das es nicht mehr gibt.</summary>
    [Fact]
    public async Task Die_Loeschung_raeumt_die_Outbox_dieses_Menschen()
    {
        var anna = Guid.CreateVersion7();

        await using (var vorher = postgres.Kontext())
        {
            vorher.Set<OutboxZeile>().Add(new OutboxZeile
            {
                Id = Guid.CreateVersion7(),
                UserId = anna,
                Kind = "transfer_update",
                CreatedAt = DateTime.UtcNow
            });

            await vorher.SaveChangesAsync();
        }

        await Loesche(anna);

        await using var nachher = postgres.Kontext();
        (await nachher.Set<OutboxZeile>().AnyAsync(zeile => zeile.UserId == anna))
            .Should().BeFalse();
    }

    /// <summary>Die Löschung eines Menschen fasst niemand anderen an.</summary>
    [Fact]
    public async Task Die_Loeschung_trifft_nur_diesen_Menschen()
    {
        var anna = Guid.CreateVersion7();
        var jemand = Guid.CreateVersion7();
        await Status(anna);
        await Status(jemand);
        var fremder = await Vorgang(jemand, Guid.CreateVersion7(), "talking");

        await Loesche(anna);

        await using var kontext = postgres.Kontext();
        (await kontext.Marktstatus.AnyAsync(zeile => zeile.Id == jemand)).Should().BeTrue();
        (await kontext.Vorgaenge.AnyAsync(zeile => zeile.Id == fremder)).Should().BeTrue();
    }

    /// <summary>Ohne das Geheimnis passiert nichts.</summary>
    [Fact]
    public async Task Ohne_das_richtige_Geheimnis_wird_nichts_geloescht()
    {
        var anna = Guid.CreateVersion7();
        await Status(anna);

        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", "geraten");

        var antwort = await browser.PostAsJsonAsync("/erasure", new { userId = anna });

        antwort.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var kontext = postgres.Kontext();
        (await kontext.Marktstatus.AnyAsync(zeile => zeile.Id == anna)).Should().BeTrue();
    }

    /// <summary>Ein zweiter Aufruf ist kein Fehlschlag.</summary>
    /// <remarks>
    /// Ein 404 für „schon gelöscht" sähe für den Zusteller wie ein Fehlschlag
    /// aus, und er würde ewig wiederholen, was längst erledigt ist
    /// (ADR-0027 §4.2).
    /// </remarks>
    [Fact]
    public async Task Ein_zweites_Mal_loeschen_ist_wieder_2xx()
    {
        var anna = Guid.CreateVersion7();

        (await Loesche(anna)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Loesche(anna)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> Loesche(Guid wer)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", Loeschgeheimnis);
        return browser.PostAsJsonAsync("/erasure", new { userId = wer });
    }

    private async Task Status(Guid wer)
    {
        await using var kontext = postgres.Kontext();
        var jetzt = DateTime.UtcNow;

        kontext.Marktstatus.Add(new MarktZeile
        {
            Id = wer,
            Availability = "listening",
            Employed = false,
            Note = "Nur Remote.",
            CreatedAt = jetzt,
            UpdatedAt = jetzt
        });

        await kontext.SaveChangesAsync();
    }

    /// <summary>Legt einen Vorgang in einem bestimmten Stand an.</summary>
    /// <remarks>
    /// Direkt in die Tabelle: einen bezahlten Abschluss über die Routen zu
    /// erreichen kostet fünf Aufrufe und prüft hier nichts, was nicht anderswo
    /// schon geprüft ist.
    /// </remarks>
    private async Task<Guid> Vorgang(Guid wer, Guid firma, string stand, long? gebuehr = null)
    {
        await using var kontext = postgres.Kontext();
        var jetzt = DateTime.UtcNow;
        var zeile = new VorgangsZeile
        {
            Id = Guid.CreateVersion7(),
            SubjectId = wer,
            TenantId = firma,
            Status = stand,
            Message = "Wir hätten da etwas.",
            OfferNote = gebuehr is null ? string.Empty : "Angebot.",
            OfferFeeCents = gebuehr,
            CreatedAt = jetzt,
            UpdatedAt = jetzt
        };

        kontext.Vorgaenge.Add(zeile);
        await kontext.SaveChangesAsync();

        return zeile.Id;
    }

    private async Task<Guid> Anfrage(Guid ueberWen, Guid firma, Guid frager)
    {
        await using var kontext = postgres.Kontext();
        var zeile = new AnfrageZeile
        {
            Id = Guid.CreateVersion7(),
            SubjectId = ueberWen,
            TenantId = firma,
            RequestedBy = frager,
            Status = "PENDING",
            CreatedAt = DateTime.UtcNow
        };

        kontext.Anfragen.Add(zeile);
        await kontext.SaveChangesAsync();

        return zeile.Id;
    }
}
