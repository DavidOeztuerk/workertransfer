using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Gespraeche;
using WorkerTransfer.Advisor.Infrastructure.Persistence;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Advisor.Tests;

/// <summary>Das Mandat, drei Stufen, eine Einigung — und gelöscht werden.</summary>
/// <remarks>
/// Die Reihe geht den ganzen Weg über den Draht, weil zwischen einem Handler
/// und dem, was ein Browser bekommt, noch die Einstellungen, die Verdrahtung
/// und die Feldnamen liegen. Ein Feld, das in camelCase gebunden wird, kommt
/// nie an — und jede Handlerreihe bliebe grün.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class BeraterreiseTests(Postgres postgres) : IAsyncLifetime
{
    private const string Loeschgeheimnis = "loesch-geheimnis";

    private static readonly Guid Firma = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Werber = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Anna = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Probetor _tor = new();
    private readonly Probefirmen _firmen = new();
    private readonly Probepersonen _personen = new();
    private readonly Probeuebergabe _uebergabe = new();

    public async Task InitializeAsync()
    {
        // Die Reihen teilen sich EINE Datenbank, und xUnit gibt innerhalb einer
        // Sammlung keine Reihenfolge zu. Ohne dieses Leeren waere jede Zusage
        // ueber „genau eine Zeile" davon abhaengig, wer vorher lief.
        await using (var vorlauf = Kontext())
        {
            await vorlauf.Database.ExecuteSqlRawAsync(
                "TRUNCATE TABLE mandates, conversations, outbox");
        }

        _tor.Selbst = Anna;

        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:advisor", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", Loeschgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
            {
                dienste.Replace(ServiceDescriptor.Scoped<IEinwilligungstor>(_ => _tor));
                dienste.Replace(ServiceDescriptor.Scoped<IFirmenauskunft>(_ => _firmen));
                dienste.Replace(ServiceDescriptor.Scoped<IPersonenauskunft>(_ => _personen));
                dienste.Replace(ServiceDescriptor.Scoped<IVorgangsuebergabe>(_ => _uebergabe));
            });
        });
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------------
    // Das Mandat
    // ---------------------------------------------------------------------

    /// <summary>Ein leeres Mandat ist vollständig — und antwortet 200, nicht 404.</summary>
    /// <remarks>
    /// „Nichts gesagt" IST ein Zustand. Ein 404 zwänge die Oberfläche, sich eine
    /// Vorgabe auszudenken, und die Gefahr ist, dass sie sich die falsche
    /// ausdenkt.
    /// </remarks>
    [Fact]
    public async Task Wer_nichts_gesagt_hat_bekommt_ein_leeres_Mandat()
    {
        var antwort = await AlsPerson(Anna).GetAsync(Ziel("/advisor/me/mandate"));

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);

        var mandat = await Json(antwort);

        mandat.GetProperty("entry_month").ValueKind.Should().Be(JsonValueKind.Null);
        mandat.GetProperty("excluded_domains").GetArrayLength().Should().Be(0);
    }

    /// <summary>Die vier Werte reisen hin und zurück — in snake_case.</summary>
    /// <remarks>
    /// Der Grund für diese Reihe: ein Feld, das der Server als <c>salaryMin</c>
    /// bindet, während die Oberfläche <c>salary_min</c> schickt, kommt nie an —
    /// und jede Handlerreihe bliebe grün.
    /// </remarks>
    [Fact]
    public async Task Die_vier_Werte_reisen_hin_und_zurueck()
    {
        var geschrieben = await AlsPerson(Anna).PutAsJsonAsync(
            Ziel("/advisor/me/mandate"),
            new Dictionary<string, object?>
            {
                ["entry_month"] = "2026-11",
                ["salary_min"] = 4000,
                ["salary_max"] = 5200,
                ["workload_percent"] = 80,
                ["excluded_domains"] = new[] { "Arbeitgeber.TEST" }
            });

        geschrieben.StatusCode.Should().Be(HttpStatusCode.OK);

        var gelesen = await Json(await AlsPerson(Anna).GetAsync(Ziel("/advisor/me/mandate")));

        gelesen.GetProperty("entry_month").GetString().Should().Be("2026-11");
        gelesen.GetProperty("salary_min").GetInt32().Should().Be(4000);
        gelesen.GetProperty("salary_max").GetInt32().Should().Be(5200);
        gelesen.GetProperty("workload_percent").GetInt32().Should().Be(80);

        // Kleingeschrieben abgelegt: eine Domain ist keine Zeichenkette mit
        // Gross- und Kleinschreibung, und ein Vergleich, der das nicht wuesste,
        // liesse einen Ausschluss ins Leere laufen.
        gelesen.GetProperty("excluded_domains")[0].GetString().Should().Be("arbeitgeber.test");
    }

    /// <summary>Ein unmöglicher Wert wird benannt, nicht gespeichert.</summary>
    [Theory]
    [InlineData("workload_percent", 5)]
    [InlineData("workload_percent", 120)]
    public async Task Ein_unmoegliches_Pensum_wird_abgesagt(string feld, int wert)
    {
        var antwort = await AlsPerson(Anna).PutAsJsonAsync(
            Ziel("/advisor/me/mandate"),
            new Dictionary<string, object?> { [feld] = wert });

        antwort.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Die REGEL, nie der Wert: ein Gehalt oder ein Pensum in einer Meldung
        // stuende am Ende im Protokoll.
        (await Json(antwort)).GetProperty("detail").GetString()
            .Should().NotContain(wert.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // ---------------------------------------------------------------------
    // Die drei Stufen
    // ---------------------------------------------------------------------

    /// <summary>Eröffnen darf nur, wer für eine Firma handelt — und nur bei Freigabe.</summary>
    [Fact]
    public async Task Eroeffnen_darf_nur_eine_Firma_mit_Freigabe()
    {
        var ohne = await _dienst.CreateClient().PostAsJsonAsync(
            Ziel("/advisor/conversations"), Eroeffnung());
        ohne.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var person = await AlsPerson(Werber).PostAsJsonAsync(
            Ziel("/advisor/conversations"), Eroeffnung());
        person.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Firma, aber nichts freigegeben: dieselbe 404 wie „gibt es nicht".
        var ohneFreigabe = await AlsFirma().PostAsJsonAsync(
            Ziel("/advisor/conversations"), Eroeffnung());
        ohneFreigabe.StatusCode.Should().Be(HttpStatusCode.NotFound);

        _tor.Stelle(Anna, Firma, Stufe.Profil);

        var mitFreigabe = await AlsFirma().PostAsJsonAsync(
            Ziel("/advisor/conversations"), Eroeffnung());
        mitFreigabe.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>Die Person erfährt davon — inhaltsfrei, über den Postausgang.</summary>
    /// <remarks>
    /// Eine Kennung und eine Art, mehr passt nicht hinein (ADR-0025). Kein
    /// Firmenname: eine Mail landet in einem Postfach, und dieses Postfach kann
    /// das des jetzigen Arbeitgebers sein.
    /// </remarks>
    [Fact]
    public async Task Ein_eroeffnetes_Gespraech_hinterlaesst_einen_Vermerk()
    {
        _tor.Stelle(Anna, Firma, Stufe.Profil);

        await AlsFirma().PostAsJsonAsync(Ziel("/advisor/conversations"), Eroeffnung());

        await using var kontext = Kontext();

        var zeilen = await kontext.Set<OutboxZeile>().ToListAsync();

        zeilen.Should().ContainSingle()
            .Which.Should().Match<OutboxZeile>(
                zeile => zeile.UserId == Anna && zeile.Kind == "advisor_conversation");
    }

    /// <summary>Ein zweites Eröffnen legt kein zweites Gespräch an.</summary>
    [Fact]
    public async Task Ein_zweites_Eroeffnen_gibt_dasselbe_Gespraech()
    {
        _tor.Stelle(Anna, Firma, Stufe.Profil);

        var erstes = await AlsFirma().PostAsJsonAsync(
            Ziel("/advisor/conversations"), Eroeffnung());
        erstes.StatusCode.Should().Be(HttpStatusCode.Created);

        var zweites = await AlsFirma().PostAsJsonAsync(
            Ziel("/advisor/conversations"), Eroeffnung());
        zweites.StatusCode.Should().Be(HttpStatusCode.OK);

        (await Json(zweites)).GetProperty("id").GetGuid()
            .Should().Be((await Json(erstes)).GetProperty("id").GetGuid());
    }

    /// <summary>
    /// Die ganze Reise: Stufe 1, Stufe 2, Stufe 3 — und bei jeder Stufe sieht
    /// das Unternehmen genau das, was freigegeben ist, und kein Feld mehr.
    /// </summary>
    /// <remarks>
    /// <strong>Am JSON gemessen und nicht am Objekt.</strong> Das ist die
    /// Zusage: nicht <c>"salary_min": null</c>, sondern gar kein Schlüssel. Ein
    /// genulltes Feld sagt „es gibt hier ein Gehalt, du siehst es nur nicht" —
    /// und damit, dass es etwas zu sehen gäbe.
    /// </remarks>
    [Fact]
    public async Task Jede_Stufe_zeigt_genau_das_was_freigegeben_ist()
    {
        await Mandat();
        _personen.Bestand[Anna] = new Personenbild("Anna Beispiel", "anna@beispiel.test");
        _tor.Stelle(Anna, Firma, Stufe.Profil);

        var gespraech = await Eroeffne();

        var stufe1 = await Roh(await AlsFirma().GetAsync(Ziel($"/advisor/conversations/{gespraech}")));
        stufe1.Should().Contain("\"entry_month\"");
        stufe1.Should().NotContain("salary_min");
        stufe1.Should().NotContain("\"name\"");

        await AlsPerson(Anna).PostAsJsonAsync(
            Ziel($"/advisor/me/conversations/{gespraech}/advance"),
            new Dictionary<string, object?> { ["stage"] = 2 });

        var stufe2 = await Roh(await AlsFirma().GetAsync(Ziel($"/advisor/conversations/{gespraech}")));
        stufe2.Should().Contain("\"salary_min\"");
        stufe2.Should().NotContain("\"name\"");

        await AlsPerson(Anna).PostAsJsonAsync(
            Ziel($"/advisor/me/conversations/{gespraech}/advance"),
            new Dictionary<string, object?> { ["stage"] = 3 });

        var stufe3 = await Roh(await AlsFirma().GetAsync(Ziel($"/advisor/conversations/{gespraech}")));
        stufe3.Should().Contain("\"name\"");
        stufe3.Should().Contain("anna@beispiel.test");
    }

    /// <summary>Ein Widerruf wirkt bei der nächsten Anfrage — und zwar ganz.</summary>
    /// <remarks>
    /// Auf Stufe 0 fällt das Gespräch aus der Liste des Unternehmens und
    /// antwortet auf direktem Weg 404. Eine leere Zeile darin wäre der
    /// „gesperrt"-Hinweis in Listenform: sie sagte, dass es diesen Menschen
    /// gibt und dass er zurückgezogen hat.
    /// </remarks>
    [Fact]
    public async Task Ein_Widerruf_wirkt_bei_der_naechsten_Anfrage()
    {
        _tor.Stelle(Anna, Firma, Stufe.Person);

        var gespraech = await Eroeffne();

        (await AlsFirma().GetAsync(Ziel($"/advisor/conversations/{gespraech}")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var zurueck = await AlsPerson(Anna).PostAsJsonAsync(
            Ziel($"/advisor/me/conversations/{gespraech}/withdraw"),
            new Dictionary<string, object?> { ["stage"] = 1 });

        zurueck.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(zurueck)).GetProperty("stage").GetInt32().Should().Be(0);

        (await AlsFirma().GetAsync(Ziel($"/advisor/conversations/{gespraech}")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var liste = await Json(await AlsFirma().GetAsync(Ziel("/advisor/conversations")));
        liste.GetProperty("items").GetArrayLength().Should().Be(0);

        // Die Person behaelt es und kann wieder freigeben — ein Widerruf, der
        // die Zeile mit wegnaehme, waere eine Einbahnstrasse.
        var meine = await Json(await AlsPerson(Anna).GetAsync(Ziel("/advisor/me/conversations")));
        meine.GetProperty("items").GetArrayLength().Should().Be(1);
        meine.GetProperty("items")[0].GetProperty("stage").GetInt32().Should().Be(0);
    }

    /// <summary>Eine Stufe des Gesprächs eines anderen Menschen gibt es nicht.</summary>
    [Fact]
    public async Task Fremde_Gespraeche_gibt_es_nicht()
    {
        _tor.Stelle(Anna, Firma, Stufe.Profil);

        var gespraech = await Eroeffne();

        var antwort = await AlsPerson(Werber).PostAsJsonAsync(
            Ziel($"/advisor/me/conversations/{gespraech}/advance"),
            new Dictionary<string, object?> { ["stage"] = 1 });

        antwort.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Ein schweigender Ledger ist weder ein Ja noch ein Nein: 503.</summary>
    [Fact]
    public async Task Ein_schweigender_Ledger_gibt_503()
    {
        _tor.Stelle(Anna, Firma, Stufe.Profil);
        await Eroeffne();

        _tor.Schweigt = true;

        var antwort = await AlsFirma().GetAsync(Ziel("/advisor/conversations"));

        antwort.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(antwort)).GetProperty("correlationId").GetString()
            .Should().NotBeNullOrEmpty();
    }

    // ---------------------------------------------------------------------
    // Der jetzige Arbeitgeber
    // ---------------------------------------------------------------------

    /// <summary>
    /// Ein ausgeschlossenes Unternehmen bekommt dieselbe 404 wie ein Fremder —
    /// und die Person verliert dabei „für alle Unternehmen".
    /// </summary>
    /// <remarks>
    /// <para><strong>Die Abnahme aus PBI-4, über den Draht.</strong> Der Ledger
    /// kennt keine Verneinung, also gibt es den Modus „alle" nicht mehr, sobald
    /// jemand ein Unternehmen nennt. Danach findet der Scout diese Person nur
    /// noch für Unternehmen, denen sie einzeln freigegeben hat.</para>
    /// </remarks>
    [Fact]
    public async Task Ein_ausgeschlossenes_Unternehmen_kommt_nicht_ins_Gespraech()
    {
        _firmen.Domains[Firma] = "arbeitgeber.test";
        _tor.Oeffentlich(Anna, true);

        var geschrieben = await AlsPerson(Anna).PutAsJsonAsync(
            Ziel("/advisor/me/mandate"),
            new Dictionary<string, object?>
            {
                ["excluded_domains"] = new[] { "arbeitgeber.test" }
            });

        geschrieben.StatusCode.Should().Be(HttpStatusCode.OK);

        _tor.Widerrufe.Should().ContainSingle()
            .Which.Should().Be(Stufenfaehigkeiten.ProfilOeffentlich);

        // Auch mit einer eigenen Freigabe kommt dieses Unternehmen nicht
        // hinein: der Ausschluss ist das ausdrueckliche Veto der Person.
        _tor.Stelle(Anna, Firma, Stufe.Profil);

        var antwort = await AlsFirma().PostAsJsonAsync(
            Ziel("/advisor/conversations"), Eroeffnung());

        antwort.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------------
    // Die Einigung
    // ---------------------------------------------------------------------

    /// <summary>Zustimmen, übergeben — und der Vorgang entsteht anderswo.</summary>
    [Fact]
    public async Task Eine_Einigung_wird_ein_Vorgang()
    {
        _tor.Stelle(Anna, Firma, Stufe.Person);

        var gespraech = await Eroeffne();

        var zuFrueh = await AlsFirma().PostAsync(
            Ziel($"/advisor/conversations/{gespraech}/hand-over"), null);
        zuFrueh.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var zustimmung = await AlsPerson(Anna).PostAsync(
            Ziel($"/advisor/me/conversations/{gespraech}/agree"), null);
        zustimmung.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(zustimmung)).GetProperty("state").GetString().Should().Be("agreed");

        var uebergabe = await AlsFirma().PostAsync(
            Ziel($"/advisor/conversations/{gespraech}/hand-over"), null);
        uebergabe.StatusCode.Should().Be(HttpStatusCode.OK);

        (await Json(uebergabe)).GetProperty("transfer_id").GetGuid()
            .Should().Be(_uebergabe.Vorgang);

        _uebergabe.Uebergeben.Should().Equal(Anna);
    }

    /// <summary>Lehnt transfer-service ab, ist das 409 — und kein 500.</summary>
    /// <remarks>
    /// Eine Ablehnung von dort ist eine Aussage über die Bedingungen dieses
    /// Vorgangs (Marktfreigabe, Ansprechbarkeit) und keine Störung. Ein
    /// Schweigen dagegen wäre 503: „geht nicht" und „wir wissen es nicht" sind
    /// zweierlei.
    /// </remarks>
    [Fact]
    public async Task Eine_abgelehnte_Uebergabe_ist_409()
    {
        _tor.Stelle(Anna, Firma, Stufe.Person);
        _uebergabe.LehntAb = true;

        var gespraech = await Eroeffne();

        await AlsPerson(Anna).PostAsync(
            Ziel($"/advisor/me/conversations/{gespraech}/agree"), null);

        var antwort = await AlsFirma().PostAsync(
            Ziel($"/advisor/conversations/{gespraech}/hand-over"), null);

        antwort.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Beide Seiten dürfen beenden.</summary>
    [Fact]
    public async Task Beide_Seiten_duerfen_beenden()
    {
        _tor.Stelle(Anna, Firma, Stufe.Profil);

        var gespraech = await Eroeffne();

        var beendet = await AlsPerson(Anna).PostAsync(
            Ziel($"/advisor/me/conversations/{gespraech}/end"), null);

        beendet.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(beendet)).GetProperty("state").GetString().Should().Be("ended");

        var nochmal = await AlsFirma().PostAsync(
            Ziel($"/advisor/conversations/{gespraech}/end"), null);

        nochmal.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---------------------------------------------------------------------
    // Die Löschung
    // ---------------------------------------------------------------------

    /// <summary>Ohne das Geheimnis wird nichts gelöscht.</summary>
    [Fact]
    public async Task Ohne_das_Geheimnis_loescht_niemand()
    {
        var antwort = await _dienst.CreateClient().PostAsJsonAsync(
            Ziel("/erasure"), new Dictionary<string, object?> { ["user_id"] = Anna });

        antwort.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Mandat, Gespräche und Vermerke fallen gemeinsam.</summary>
    /// <remarks>
    /// Alle drei: wer nur die Gespräche löscht, hinterlässt das Mandat mit
    /// Eintrittstermin und Gehaltsspanne — und die Zusage aus ADR-0027 ist dann
    /// zur Hälfte eingelöst, ohne dass es jemandem auffällt.
    /// </remarks>
    [Fact]
    public async Task Die_Loeschung_nimmt_Mandat_Gespraeche_und_Vermerke()
    {
        await Mandat();
        _tor.Stelle(Anna, Firma, Stufe.Profil);
        await Eroeffne();

        var client = _dienst.CreateClient();
        client.DefaultRequestHeaders.Add("X-Erasure-Secret", Loeschgeheimnis);

        var antwort = await client.PostAsJsonAsync(
            Ziel("/erasure"), new Dictionary<string, object?> { ["user_id"] = Anna });

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);

        // Kein Aufbewahrungsfall: hier steht nichts, was jemand anderem gehoert.
        (await Json(antwort)).GetProperty("retained").GetInt32().Should().Be(0);

        await using var kontext = Kontext();

        (await kontext.Mandate.CountAsync()).Should().Be(0);
        (await kontext.Gespraeche.CountAsync()).Should().Be(0);
        (await kontext.Set<OutboxZeile>().CountAsync()).Should().Be(0);
    }

    // ---------------------------------------------------------------------

    private static Uri Ziel(string pfad) => new(pfad, UriKind.Relative);

    private static Dictionary<string, object?> Eroeffnung() => new()
    {
        ["subject_id"] = Anna,
        ["note"] = "Wir suchen jemanden für verteilte Systeme."
    };

    private async Task<Guid> Eroeffne()
    {
        var antwort = await AlsFirma().PostAsJsonAsync(
            Ziel("/advisor/conversations"), Eroeffnung());

        antwort.EnsureSuccessStatusCode();

        return (await Json(antwort)).GetProperty("id").GetGuid();
    }

    private async Task Mandat() =>
        (await AlsPerson(Anna).PutAsJsonAsync(
            Ziel("/advisor/me/mandate"),
            new Dictionary<string, object?>
            {
                ["entry_month"] = "2026-11",
                ["salary_min"] = 4000,
                ["salary_max"] = 5200,
                ["workload_percent"] = 80
            })).EnsureSuccessStatusCode();

    private HttpClient AlsPerson(Guid wer) => Mit(Tokenform.Person(wer));

    private HttpClient AlsFirma() => Mit(Tokenform.Firma(Werber, Firma));

    private HttpClient Mit(string token)
    {
        var client = _dienst.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    private static Task<string> Roh(HttpResponseMessage antwort) =>
        antwort.Content.ReadAsStringAsync();

    private AdvisorDbContext Kontext()
    {
        var bauer = new DbContextOptionsBuilder<AdvisorDbContext>();
        AdvisorDbContextFactory.ZurEntwurfszeit(bauer, postgres.ConnectionString);

        return new AdvisorDbContext(bauer.Options);
    }
}
