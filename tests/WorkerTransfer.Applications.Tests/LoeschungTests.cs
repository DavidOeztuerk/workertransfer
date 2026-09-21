using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Applications.Application.Erasure;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Infrastructure.Erasure;
using WorkerTransfer.Applications.Infrastructure.Persistence;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Applications.Tests;

/// <summary>Die Löschung — und der Schalter, der auf aus steht.</summary>
[Collection(PostgresCollection.Name)]
public class LoeschungTests(Postgres postgres) : IAsyncLifetime
{
    private const string Loeschgeheimnis = "loesch-geheimnis";

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Probeledger _ledger = new();
    private readonly Probestellen _stellen = new();
    private readonly Probemitglieder _mitglieder = new();

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:applications", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", Tokenform.OeffentlichSchluessel);
            host.UseSetting("Jwt:KeyId", Tokenform.Kennung);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", Loeschgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(
                    ServiceDescriptor.Scoped<IConsentWriter>(_ => _ledger));
                services.Replace(ServiceDescriptor.Scoped<IJobLookup>(_ => _stellen));
                services.Replace(ServiceDescriptor.Scoped<IDelivery, Probezustellung>());
                services.Replace(ServiceDescriptor.Scoped<ICompanyMemberLookup>(_ => _mitglieder));
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
        Retention.EingestellteBehalten.Should().BeFalse();
    }

    /// <summary>
    /// „Erledigt" heißt erledigt — auch die JobApplication, über die jemand
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
        var subject = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        var eingestellt = await JobApplication(subject, company, "hired");
        var laufend = await JobApplication(subject, company, "submitted");

        var response = await Loesche(subject);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var quittung = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        quittung.GetProperty("retained").GetInt32().Should().Be(0);

        await using var context = postgres.Kontext();
        (await context.Applications.AnyAsync(row => row.Id == eingestellt))
            .Should().BeFalse();
        (await context.Applications.AnyAsync(row => row.Id == laufend))
            .Should().BeFalse();
    }

    /// <summary>
    /// Der umgelegte Schalter deckt <strong>genau eine</strong> Zeilenklasse
    /// ab: <c>status = 'hired'</c>.
    /// </summary>
    /// <remarks>
    /// None Ausdehnung auf <c>rejected</c> — eine abgelehnte JobApplication
    /// begründet nichts — und kein „laufender Case" als Gummiwort. Der
    /// Schalter reist als Parameter herein, damit sich genau das prüfen lässt,
    /// ohne die Voreinstellung anzufassen.
    /// </remarks>
    [Fact]
    public async Task Der_umgelegte_Schalter_deckt_genau_hired_ab()
    {
        var subject = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        var eingestellt = await JobApplication(subject, company, "hired");
        var abgelehnt = await JobApplication(subject, company, "rejected");
        var zurueckgezogen = await JobApplication(subject, company, "withdrawn");
        var laufend = await JobApplication(subject, company, "submitted");

        await using var context = postgres.Kontext();
        var bestand = new EfErasureLedger(context);

        var behalten = await bestand.DeleteAsync(
            new SubjectId(subject), eingestellteBehalten: true);

        behalten.Should().Be(1);

        var uebrig = await context.Applications
            .Where(row => row.SubjectId == subject)
            .Select(row => row.Id)
            .ToListAsync();

        uebrig.Should().Equal(eingestellt);
        uebrig.Should().NotContain([abgelehnt, zurueckgezogen, laufend]);
    }

    /// <summary>Eine ausstehende Benachrichtigung an ein Konto, das es nicht mehr gibt.</summary>
    [Fact]
    public async Task Die_Loeschung_raeumt_die_Outbox_dieses_Menschen()
    {
        var subject = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        var job = _stellen.Oeffentlich(company);

        _mitglieder.Fuer(new TenantId(company), new SubjectId(Guid.CreateVersion7()));

        var person = Mit(Tokenform.Person(subject));

        var angelegt = JsonDocument
            .Parse(await (await person.PostAsJsonAsync("/applications", new
            {
                job_id = job, message = "Hallo.", shares_resume = false,
                shares_portfolio = false
            })).Content.ReadAsStringAsync()).RootElement;

        await Mit(Tokenform.Firma(Guid.CreateVersion7(), company)).PostAsJsonAsync(
            $"/applications/{angelegt.GetProperty("id").GetGuid()}/status",
            new { status = "reviewing" });

        await using (var vorher = postgres.Kontext())
        {
            (await vorher.Set<OutboxRow>().AnyAsync(row => row.UserId == subject))
                .Should().BeTrue();
        }

        await Loesche(subject);

        await using var nachher = postgres.Kontext();
        (await nachher.Set<OutboxRow>().AnyAsync(row => row.UserId == subject))
            .Should().BeFalse();
    }

    /// <summary>Die Löschung eines Menschen fasst niemand anderen an.</summary>
    [Fact]
    public async Task Die_Loeschung_trifft_nur_diesen_Menschen()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var jemand = Guid.CreateVersion7();
        await JobApplication(subject, company, "submitted");
        var fremde = await JobApplication(jemand, company, "submitted");

        await Loesche(subject);

        await using var context = postgres.Kontext();
        (await context.Applications.AnyAsync(row => row.Id == fremde)).Should().BeTrue();
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
        var subject = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        var meine = await JobApplication(subject, company, "submitted");

        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", "geraten");

        var response = await browser.PostAsJsonAsync("/erasure", new { user_id = subject });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var context = postgres.Kontext();
        (await context.Applications.AnyAsync(row => row.Id == meine)).Should().BeTrue();
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
        var subject = Guid.CreateVersion7();

        (await Loesche(subject)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Loesche(subject)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient Mit(string token)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private Task<HttpResponseMessage> Loesche(Guid subject)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", Loeschgeheimnis);
        return browser.PostAsJsonAsync("/erasure", new { user_id = subject });
    }

    /// <summary>Legt eine Zeile in einem bestimmten State an.</summary>
    /// <remarks>
    /// Direkt in die Tabelle: <c>hired</c> über die Routen zu erreichen kostet
    /// drei Aufrufe und prüft hier nichts, was nicht anderswo schon geprüft
    /// ist.
    /// </remarks>
    private async Task<Guid> JobApplication(Guid subject, Guid company, string state)
    {
        await using var context = postgres.Kontext();
        var now = DateTime.UtcNow;
        var row = new ApplicationRow
        {
            Id = Guid.CreateVersion7(),
            JobId = Guid.CreateVersion7(),
            TenantId = company,
            SubjectId = subject,
            Message = "Hallo.",
            Status = state,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.Applications.Add(row);
        await context.SaveChangesAsync();

        return row.Id;
    }
}
