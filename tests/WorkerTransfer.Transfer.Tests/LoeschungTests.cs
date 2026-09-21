using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Erasure;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Vorgaenge;
using WorkerTransfer.Transfer.Infrastructure.Erasure;
using WorkerTransfer.Transfer.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

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
            host.UseSetting("Jwt:PublicKey", Tokenform.OeffentlichSchluessel);
            host.UseSetting("Jwt:KeyId", Tokenform.Kennung);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", Loeschgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IConsentGate, Probeledger>());
                services.Replace(ServiceDescriptor.Scoped<IDelivery, Probezustellung>());
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
    /// Zeile nicht zur DocumentRecord eines Vermittlers: die Plattform führt kein
    /// Geld. Fällt dieser Test, ist die Voreinstellung gekippt.
    /// </remarks>
    [Fact]
    public void Der_Schalter_steht_auf_aus()
    {
        Retention.BezahlteBehalten.Should().BeFalse();
    }

    /// <summary>
    /// Die Abgrenzung ist ausgeschrieben: <c>declined</c> und <c>withdrawn</c>
    /// gehören <em>nicht</em> dazu.
    /// </summary>
    /// <remarks>
    /// Ein abgesagter Case begründet nichts. Diese Liste ist deshalb
    /// bewusst nicht die Endzustandsmenge des Aggregats.
    /// </remarks>
    [Fact]
    public void Abgeschlossen_heisst_accepted_oder_completed()
    {
        Retention.Abgeschlossene.Should().Equal(
            TransferState.Accepted, TransferState.Completed);
    }

    /// <summary>„Erledigt" heißt erledigt — auch der bezahlte Transfer.</summary>
    [Fact]
    public async Task Die_Voreinstellung_loescht_auch_den_bezahlten_Transfer()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        var bezahlt = await Case(anna, company, "completed", gebuehr: 500_000);
        var laufend = await Case(anna, Guid.CreateVersion7(), "talking");
        await Status(anna);

        var response = await Loesche(anna);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var quittung = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        quittung.GetProperty("retained").GetInt32().Should().Be(0);

        await using var context = postgres.Kontext();
        (await context.Vorgaenge.AnyAsync(row => row.Id == bezahlt)).Should().BeFalse();
        (await context.Vorgaenge.AnyAsync(row => row.Id == laufend)).Should().BeFalse();
        (await context.MarketStatus.AnyAsync(row => row.Id == anna)).Should().BeFalse();
    }

    /// <summary>
    /// Der umgelegte Schalter deckt <strong>genau eine</strong> Zeilenklasse
    /// ab: ein abgeschlossener Handel <em>mit</em> Vergütung.
    /// </summary>
    /// <remarks>
    /// None Ausdehnung auf <c>interested</c>/<c>talking</c>/<c>offered</c> —
    /// ein Gespräch ist kein Vertrag — und ohne Vergütung ist kein
    /// Handelsvorgang entstanden, an dem etwas hängen könnte.
    /// </remarks>
    [Fact]
    public async Task Der_umgelegte_Schalter_deckt_genau_den_bezahlten_Abschluss_ab()
    {
        var anna = Guid.CreateVersion7();
        var abgeschlossenBezahlt = await Case(
            anna, Guid.CreateVersion7(), "completed", gebuehr: 500_000);
        var angenommenBezahlt = await Case(
            anna, Guid.CreateVersion7(), "accepted", gebuehr: 1);
        var abgeschlossenUnbezahlt = await Case(anna, Guid.CreateVersion7(), "completed");
        var angebotBezahlt = await Case(
            anna, Guid.CreateVersion7(), "offered", gebuehr: 500_000);
        var abgesagtBezahlt = await Case(
            anna, Guid.CreateVersion7(), "declined", gebuehr: 500_000);

        await using var context = postgres.Kontext();
        var bestand = new EfErasureLedger(context);

        var behalten = await bestand.DeleteAsync(
            new SubjectId(anna), bezahlteBehalten: true);

        behalten.Should().Be(2);

        var uebrig = await context.Vorgaenge
            .Where(row => row.SubjectId == anna)
            .Select(row => row.Id)
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
        var ueberIhn = await ResumeRequest(recruiter, Guid.CreateVersion7(), Guid.CreateVersion7());
        var vonIhm = await ResumeRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), recruiter);

        await Loesche(recruiter);

        await using var context = postgres.Kontext();

        (await context.Anfragen.AnyAsync(row => row.Id == ueberIhn)).Should().BeFalse();

        var geblieben = await context.Anfragen.SingleAsync(row => row.Id == vonIhm);
        geblieben.RequestedBy.Should().BeNull("der Vorgang gehört dem Unternehmen");
    }

    /// <summary>Ein ausstehender Vermerk an ein Konto, das es nicht mehr gibt.</summary>
    [Fact]
    public async Task Die_Loeschung_raeumt_die_Outbox_dieses_Menschen()
    {
        var anna = Guid.CreateVersion7();

        await using (var vorher = postgres.Kontext())
        {
            vorher.Set<OutboxRow>().Add(new OutboxRow
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
        (await nachher.Set<OutboxRow>().AnyAsync(row => row.UserId == anna))
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
        var fremder = await Case(jemand, Guid.CreateVersion7(), "talking");

        await Loesche(anna);

        await using var context = postgres.Kontext();
        (await context.MarketStatus.AnyAsync(row => row.Id == jemand)).Should().BeTrue();
        (await context.Vorgaenge.AnyAsync(row => row.Id == fremder)).Should().BeTrue();
    }

    /// <summary>Ohne das Geheimnis passiert nichts.</summary>
    [Fact]
    public async Task Ohne_das_richtige_Geheimnis_wird_nichts_geloescht()
    {
        var anna = Guid.CreateVersion7();
        await Status(anna);

        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", "geraten");

        var response = await browser.PostAsJsonAsync("/erasure", new { user_id = anna });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var context = postgres.Kontext();
        (await context.MarketStatus.AnyAsync(row => row.Id == anna)).Should().BeTrue();
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

    private Task<HttpResponseMessage> Loesche(Guid subject)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", Loeschgeheimnis);
        return browser.PostAsJsonAsync("/erasure", new { user_id = subject });
    }

    private async Task Status(Guid subject)
    {
        await using var context = postgres.Kontext();
        var now = DateTime.UtcNow;

        context.MarketStatus.Add(new MarketRow
        {
            Id = subject,
            Availability = "listening",
            Employed = false,
            Note = "Nur Remote.",
            CreatedAt = now,
            UpdatedAt = now
        });

        await context.SaveChangesAsync();
    }

    /// <summary>Legt einen Case in einem bestimmten State an.</summary>
    /// <remarks>
    /// Direkt in die Tabelle: einen bezahlten Abschluss über die Routen zu
    /// erreichen kostet fünf Aufrufe und prüft hier nichts, was nicht anderswo
    /// schon geprüft ist.
    /// </remarks>
    private async Task<Guid> Case(Guid subject, Guid company, string state, long? gebuehr = null)
    {
        await using var context = postgres.Kontext();
        var now = DateTime.UtcNow;
        var row = new CaseRow
        {
            Id = Guid.CreateVersion7(),
            SubjectId = subject,
            TenantId = company,
            Status = state,
            Message = "Wir hätten da etwas.",
            OfferNote = gebuehr is null ? string.Empty : "Angebot.",
            OfferFeeCents = gebuehr,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.Vorgaenge.Add(row);
        await context.SaveChangesAsync();

        return row.Id;
    }

    private async Task<Guid> ResumeRequest(Guid ueberWen, Guid company, Guid frager)
    {
        await using var context = postgres.Kontext();
        var row = new RequestRow
        {
            Id = Guid.CreateVersion7(),
            SubjectId = ueberWen,
            TenantId = company,
            RequestedBy = frager,
            Status = "PENDING",
            CreatedAt = DateTime.UtcNow
        };

        context.Anfragen.Add(row);
        await context.SaveChangesAsync();

        return row.Id;
    }
}
