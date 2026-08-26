using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Girder.Core.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Applications.Application.Loeschung;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Infrastructure.Loeschung;
using WorkerTransfer.Applications.Infrastructure.Persistence;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Applications.Tests;

/// <summary>Die Löschung — und der Schalter, der auf aus steht.</summary>
[Collection(PostgresCollection.Name)]
public class LoeschungTests(Postgres postgres) : IAsyncLifetime
{
    private const string Loeschgeheimnis = "loesch-geheimnis";

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Probeledger _ledger = new();
    private readonly Probestellen _stellen = new();

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:applications", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", Loeschgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
            {
                dienste.Replace(
                    ServiceDescriptor.Scoped<IEinwilligungsschreiber>(_ => _ledger));
                dienste.Replace(ServiceDescriptor.Scoped<IStellenauskunft>(_ => _stellen));
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

    /// <summary>
    /// <strong>Die tragende Zusage dieses Dienstes.</strong>
    /// </summary>
    /// <remarks>
    /// Nicht die Löschung muss sich rechtfertigen, sondern das Behalten. Fällt
    /// dieser Test, ist die Voreinstellung gekippt — und niemandem ist eine
    /// vollständige Löschung mehr versprochen.
    /// </remarks>
    [Fact]
    public void Der_Schalter_steht_auf_aus()
    {
        Aufbewahrung.EingestellteBehalten.Should().BeFalse();
    }

    /// <summary>
    /// „Erledigt" heißt erledigt — auch die Bewerbung, über die jemand
    /// eingestellt wurde.
    /// </summary>
    /// <remarks>
    /// Offen gesagt heißt das: sie verschwindet auch aus der Liste des
    /// Unternehmens. Das ist gewollt, und <c>/delete-account</c> sagt es vor
    /// dem Knopf.
    /// </remarks>
    [Fact]
    public async Task Die_Voreinstellung_loescht_auch_die_eingestellte_Bewerbung()
    {
        var wer = Guid.CreateVersion7();
        var firma = Guid.CreateVersion7();
        var eingestellt = await Bewerbung(wer, firma, "hired");
        var laufend = await Bewerbung(wer, firma, "submitted");

        var antwort = await Loesche(wer);

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);

        var quittung = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;
        quittung.GetProperty("retained").GetInt32().Should().Be(0);

        await using var kontext = postgres.Kontext();
        (await kontext.Bewerbungen.AnyAsync(zeile => zeile.Id == eingestellt))
            .Should().BeFalse();
        (await kontext.Bewerbungen.AnyAsync(zeile => zeile.Id == laufend))
            .Should().BeFalse();
    }

    /// <summary>
    /// Der umgelegte Schalter deckt <strong>genau eine</strong> Zeilenklasse
    /// ab: <c>status = 'hired'</c>.
    /// </summary>
    /// <remarks>
    /// Keine Ausdehnung auf <c>rejected</c> — eine abgelehnte Bewerbung
    /// begründet nichts — und kein „laufender Vorgang" als Gummiwort. Der
    /// Schalter reist als Parameter herein, damit sich genau das prüfen lässt,
    /// ohne die Voreinstellung anzufassen.
    /// </remarks>
    [Fact]
    public async Task Der_umgelegte_Schalter_deckt_genau_hired_ab()
    {
        var wer = Guid.CreateVersion7();
        var firma = Guid.CreateVersion7();
        var eingestellt = await Bewerbung(wer, firma, "hired");
        var abgelehnt = await Bewerbung(wer, firma, "rejected");
        var zurueckgezogen = await Bewerbung(wer, firma, "withdrawn");
        var laufend = await Bewerbung(wer, firma, "submitted");

        await using var kontext = postgres.Kontext();
        var bestand = new EfLoeschbestand(kontext);

        var behalten = await bestand.LoescheAsync(
            new SubjectId(wer), eingestellteBehalten: true);

        behalten.Should().Be(1);

        var uebrig = await kontext.Bewerbungen
            .Where(zeile => zeile.SubjectId == wer)
            .Select(zeile => zeile.Id)
            .ToListAsync();

        uebrig.Should().Equal(eingestellt);
        uebrig.Should().NotContain([abgelehnt, zurueckgezogen, laufend]);
    }

    /// <summary>Eine ausstehende Benachrichtigung an ein Konto, das es nicht mehr gibt.</summary>
    [Fact]
    public async Task Die_Loeschung_raeumt_die_Outbox_dieses_Menschen()
    {
        var wer = Guid.CreateVersion7();
        var firma = Guid.CreateVersion7();
        var stelle = _stellen.Oeffentlich(firma);
        var person = Mit(Tokenform.Person(wer));

        var angelegt = JsonDocument
            .Parse(await (await person.PostAsJsonAsync("/applications", new
            {
                job_id = stelle, message = "Hallo.", shares_resume = false,
                shares_portfolio = false
            })).Content.ReadAsStringAsync()).RootElement;

        await Mit(Tokenform.Firma(Guid.CreateVersion7(), firma)).PostAsJsonAsync(
            $"/applications/{angelegt.GetProperty("id").GetGuid()}/status",
            new { status = "reviewing" });

        await using (var vorher = postgres.Kontext())
        {
            (await vorher.Set<OutboxZeile>().AnyAsync(zeile => zeile.UserId == wer))
                .Should().BeTrue();
        }

        await Loesche(wer);

        await using var nachher = postgres.Kontext();
        (await nachher.Set<OutboxZeile>().AnyAsync(zeile => zeile.UserId == wer))
            .Should().BeFalse();
    }

    /// <summary>Die Löschung eines Menschen fasst niemand anderen an.</summary>
    [Fact]
    public async Task Die_Loeschung_trifft_nur_diesen_Menschen()
    {
        var firma = Guid.CreateVersion7();
        var wer = Guid.CreateVersion7();
        var jemand = Guid.CreateVersion7();
        await Bewerbung(wer, firma, "submitted");
        var fremde = await Bewerbung(jemand, firma, "submitted");

        await Loesche(wer);

        await using var kontext = postgres.Kontext();
        (await kontext.Bewerbungen.AnyAsync(zeile => zeile.Id == fremde)).Should().BeTrue();
    }

    /// <summary>Ohne das Geheimnis passiert nichts.</summary>
    /// <remarks>
    /// Dieselbe Antwort für ein falsches und ein nicht gesetztes Geheimnis: ein
    /// Aufrufer darf „falsch geraten" nicht von „hier ist noch nichts
    /// verdrahtet" unterscheiden können.
    /// </remarks>
    [Fact]
    public async Task Ohne_das_richtige_Geheimnis_wird_nichts_geloescht()
    {
        var wer = Guid.CreateVersion7();
        var firma = Guid.CreateVersion7();
        var meine = await Bewerbung(wer, firma, "submitted");

        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", "geraten");

        var antwort = await browser.PostAsJsonAsync("/erasure", new { userId = wer });

        antwort.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var kontext = postgres.Kontext();
        (await kontext.Bewerbungen.AnyAsync(zeile => zeile.Id == meine)).Should().BeTrue();
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
        var wer = Guid.CreateVersion7();

        (await Loesche(wer)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Loesche(wer)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient Mit(string token)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private Task<HttpResponseMessage> Loesche(Guid wer)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", Loeschgeheimnis);
        return browser.PostAsJsonAsync("/erasure", new { userId = wer });
    }

    /// <summary>Legt eine Zeile in einem bestimmten Stand an.</summary>
    /// <remarks>
    /// Direkt in die Tabelle: <c>hired</c> über die Routen zu erreichen kostet
    /// drei Aufrufe und prüft hier nichts, was nicht anderswo schon geprüft
    /// ist.
    /// </remarks>
    private async Task<Guid> Bewerbung(Guid wer, Guid firma, string stand)
    {
        await using var kontext = postgres.Kontext();
        var jetzt = DateTime.UtcNow;
        var zeile = new BewerbungsZeile
        {
            Id = Guid.CreateVersion7(),
            JobId = Guid.CreateVersion7(),
            TenantId = firma,
            SubjectId = wer,
            Message = "Hallo.",
            Status = stand,
            CreatedAt = jetzt,
            UpdatedAt = jetzt
        };

        kontext.Bewerbungen.Add(zeile);
        await kontext.SaveChangesAsync();

        return zeile.Id;
    }
}
