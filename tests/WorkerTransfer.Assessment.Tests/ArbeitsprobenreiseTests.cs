using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Assessment.Application.Ports;
using WorkerTransfer.Assessment.Infrastructure.Persistence;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Assessment.Tests;

/// <summary>Die Aufgabe, die Lösung, die Rückmeldung — über den Draht.</summary>
/// <remarks>
/// Die Reihe geht den ganzen Weg, weil zwischen einem Handler und dem, was ein
/// Browser bekommt, noch die Einstellungen, die Verdrahtung und die Feldnamen
/// liegen. Ein Feld, das in camelCase gebunden wird, kommt nie an — und jede
/// Handlerreihe bliebe grün.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class ArbeitsprobenreiseTests(Postgres postgres) : IAsyncLifetime
{
    private const string Loeschgeheimnis = "loesch-geheimnis";

    private static readonly Guid Firma = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Werber = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Anna = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Probetor _tor = new();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        // Die Reihen teilen sich EINE Datenbank, und xUnit gibt innerhalb einer
        // Sammlung keine Reihenfolge zu. Ohne dieses Leeren waere jede Zusage
        // ueber „genau eine Zeile" davon abhaengig, wer vorher lief.
        await using (var vorlauf = Kontext())
        {
            await vorlauf.Database.ExecuteSqlRawAsync("TRUNCATE TABLE assessments, outbox");
        }

        _tor.Stelle(Anna, Firma, true);

        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:assessment", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", Loeschgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
                dienste.Replace(ServiceDescriptor.Scoped<IEinwilligungstor>(_ => _tor)));
        });
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------------
    // Die Menge der Routen
    // ---------------------------------------------------------------------

    /// <summary>Dieser Dienst hat genau diese Routen.</summary>
    /// <remarks>
    /// <para><strong>Eine geschlossene Menge, und das ist die schärfste Fassung
    /// der ersten Auflage.</strong> Wer eine siebte Route hinzufügt, ändert
    /// diesen Test — und beantwortet dabei die Frage, ob sie eine Aussage über
    /// einen Menschen herausgibt (ADR-0042 §1).</para>
    ///
    /// <para>Und eine, die hier fehlt und fehlen soll: ein Weg, eine Aufgabe
    /// <em>abzulehnen</em>. Wer nicht will, tut nichts (ADR-0042 §3).</para>
    /// </remarks>
    [Fact]
    public void Der_Dienst_hat_genau_diese_Routen() =>
        _dienst.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpunkt =>
                $"{Verben(endpunkt)} /{endpunkt.RoutePattern.RawText?.TrimStart('/').TrimEnd('/')}")
            .Where(zeile => zeile.Contains("assessments", StringComparison.Ordinal)
                            || zeile.Contains("erasure", StringComparison.Ordinal))
            .Should().BeEquivalentTo(
                "POST /assessments",
                "GET /assessments",
                "POST /assessments/{id:guid}/evaluation",
                "GET /assessments/me",
                "POST /assessments/{id:guid}/submission",
                "GET /assessments/{id:guid}",
                "POST /erasure");

    // ---------------------------------------------------------------------
    // Der ganze Weg
    // ---------------------------------------------------------------------

    /// <summary>Stellen, einreichen, bewerten — und beide sehen dasselbe.</summary>
    [Fact]
    public async Task Der_ganze_Weg_von_der_Aufgabe_bis_zur_Rueckmeldung()
    {
        var id = await Stelle();

        var eingereicht = await AlsPerson(Anna).PostAsJsonAsync(
            Ziel($"/assessments/{id}/submission"),
            new Dictionary<string, object?>
            {
                ["text"] = "Liegt im Repository.",
                ["url"] = "https://beispiel.test/loesung"
            });

        eingereicht.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(eingereicht)).GetProperty("state").GetString().Should().Be("submitted");

        var bewertet = await AlsFirma().PostAsJsonAsync(
            Ziel($"/assessments/{id}/evaluation"),
            new Dictionary<string, object?>
            {
                ["outcome"] = "rejected",
                ["text"] = "Sauber gelöst, nur die Fehlerbehandlung fehlt."
            });

        bewertet.StatusCode.Should().Be(HttpStatusCode.OK);

        var sicht = await Json(bewertet);

        sicht.GetProperty("state").GetString().Should().Be("evaluated");
        sicht.GetProperty("evaluation").GetProperty("outcome").GetString().Should().Be("rejected");
        sicht.GetProperty("submission").GetProperty("url").GetString()
            .Should().Be("https://beispiel.test/loesung");

        // Und die Felder reisen in snake_case: ein Feld, das der Server als
        // `dueAt` binden wuerde, kaeme nie an, und jede Handlerreihe bliebe
        // gruen.
        sicht.GetProperty("due_at").ValueKind.Should().Be(JsonValueKind.String);
        sicht.GetProperty("hours").GetInt32().Should().Be(4);
    }

    /// <summary>Beide Seiten bekommen dasselbe Dokument — Byte für Byte.</summary>
    /// <remarks>
    /// <strong>Der Mechanismus hinter „die Person sieht die Bewertung"</strong>
    /// (ADR-0042 §2). Es gibt keine Firmensicht neben einer Personensicht, weil
    /// es nur einen Endpunkt gibt — zwei wären die Stelle, an der die beiden
    /// auseinanderlaufen, und zwar erst Monate später, beim nächsten Feld.
    /// </remarks>
    [Fact]
    public async Task Beide_Seiten_sehen_byte_gleich_dasselbe()
    {
        var id = await Stelle();
        await Reiche_ein(id);
        await Bewerte(id, "accepted", "Passt. Reden wir weiter.");

        var ihres = await Roh(await AlsFirma().GetAsync(Ziel($"/assessments/{id}")));
        var ihrs = await Roh(await AlsPerson(Anna).GetAsync(Ziel($"/assessments/{id}")));

        ihrs.Should().Be(ihres);
        ihrs.Should().Contain("Passt. Reden wir weiter.");
    }

    /// <summary>Eine Absage ohne Text ist 422 — und die Absage bleibt aus.</summary>
    /// <remarks>
    /// Ablehnen und Begründen sind ein Schritt. Ohne diesen Test wäre die
    /// Zusage ein Absatz: der Server nähme die Absage an und liesse den Text
    /// weg, und die Person läse eine Ablehnung ohne ein Wort dazu.
    /// </remarks>
    [Fact]
    public async Task Eine_Absage_ohne_Text_wird_abgewiesen()
    {
        var id = await Stelle();
        await Reiche_ein(id);

        var antwort = await AlsFirma().PostAsJsonAsync(
            Ziel($"/assessments/{id}/evaluation"),
            new Dictionary<string, object?> { ["outcome"] = "rejected" });

        antwort.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Und der Vorgang steht unveraendert da: eine halb geschriebene Absage
        // waere schlimmer als keine.
        (await Json(await AlsPerson(Anna).GetAsync(Ziel($"/assessments/{id}"))))
            .GetProperty("state").GetString().Should().Be("submitted");
    }

    /// <summary>Nach einem Widerruf liest die Person weiter, das Unternehmen nicht.</summary>
    [Fact]
    public async Task Nach_einem_Widerruf_liest_nur_noch_die_Person()
    {
        var id = await Stelle();
        await Reiche_ein(id);
        await Bewerte(id, "rejected", "Zu knapp an der Aufgabe vorbei.");

        _tor.Stelle(Anna, Firma, false);

        var ihrs = await AlsPerson(Anna).GetAsync(Ziel($"/assessments/{id}"));
        ihrs.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(ihrs)).GetProperty("evaluation").GetProperty("text").GetString()
            .Should().Be("Zu knapp an der Aufgabe vorbei.");

        // Dieselbe 404 wie „gibt es nicht": vier Lagen, eine Antwort.
        (await AlsFirma().GetAsync(Ziel($"/assessments/{id}"))).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        var liste = await Json(await AlsFirma().GetAsync(Ziel("/assessments")));
        liste.GetProperty("items").GetArrayLength().Should().Be(0);

        // Und keine Gesamtzahl daneben, die die Differenz verriete (ADR-0026).
        liste.TryGetProperty("total", out _).Should().BeFalse();
    }

    /// <summary>Ohne Freigabe gibt es nichts — dieselbe 404 wie für einen Unbekannten.</summary>
    [Fact]
    public async Task Ohne_Freigabe_entsteht_keine_Aufgabe()
    {
        _tor.Stelle(Anna, Firma, false);

        var antwort = await AlsFirma().PostAsJsonAsync(Ziel("/assessments"), Aufgabe(Anna));

        antwort.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Und fuer eine Kennung, die es gar nicht gibt, dasselbe.
        (await AlsFirma().PostAsJsonAsync(Ziel("/assessments"), Aufgabe(Guid.NewGuid())))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Der Ledger wird VOR dem Rumpf gefragt.</summary>
    /// <remarks>
    /// Andersherum lernte ein Fremder aus dem Unterschied zwischen 422 und 404,
    /// dass seine Angaben in Ordnung waren — und damit, dass es diesen Menschen
    /// gibt.
    /// </remarks>
    [Fact]
    public async Task Der_Ledger_steht_vor_der_Rumpfpruefung()
    {
        _tor.Stelle(Anna, Firma, false);

        var kaputt = Aufgabe(Anna);
        kaputt["hours"] = 99;
        kaputt["title"] = "";

        (await AlsFirma().PostAsJsonAsync(Ziel("/assessments"), kaputt))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Ein Umfang ausserhalb der Grenzen kommt nicht durch.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(40)]
    public async Task Ein_unmoeglicher_Umfang_wird_abgesagt(int stunden)
    {
        var koerper = Aufgabe(Anna);
        koerper["hours"] = stunden;

        var antwort = await AlsFirma().PostAsJsonAsync(Ziel("/assessments"), koerper);

        antwort.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Und ganz ohne Umfang gibt es keine Aufgabe.</summary>
    /// <remarks>
    /// Der Server denkt sich keine Zahl aus: eine erfundene hielte die Person
    /// für eine Angabe des Unternehmens (ADR-0042 §3).
    /// </remarks>
    [Fact]
    public async Task Ohne_Umfang_gibt_es_keine_Aufgabe()
    {
        var koerper = Aufgabe(Anna);
        koerper.Remove("hours");

        var antwort = await AlsFirma().PostAsJsonAsync(Ziel("/assessments"), koerper);

        antwort.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Json(antwort)).GetProperty("detail").GetString().Should().Contain("hours");
    }

    /// <summary>Wer für sich selbst handelt, stellt keine Aufgaben.</summary>
    [Fact]
    public async Task Nur_ein_Unternehmen_stellt_eine_Aufgabe()
    {
        (await _dienst.CreateClient().PostAsJsonAsync(Ziel("/assessments"), Aufgabe(Anna)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 403 und nicht 404: eine Aussage ueber den AUFRUFER verraet nichts
        // ueber den Menschen, nach dem er fragt.
        (await AlsPerson(Werber).PostAsJsonAsync(Ziel("/assessments"), Aufgabe(Anna)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Ein fremder Vorgang antwortet wie ein nicht vorhandener.</summary>
    [Fact]
    public async Task Ein_fremder_Vorgang_ist_nicht_vorhanden()
    {
        var id = await Stelle();

        (await AlsPerson(Werber).GetAsync(Ziel($"/assessments/{id}")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await AlsPerson(Werber).PostAsJsonAsync(
                Ziel($"/assessments/{id}/submission"),
                new Dictionary<string, object?> { ["text"] = "Fremde Arbeit." }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Schweigt der Ledger, ist es 503 — und nie eine leere Liste.</summary>
    /// <remarks>
    /// Weder 404 noch ein leeres Ergebnis: beides wäre eine Aussage über einen
    /// Menschen, die aus unserem Ausfall stammt (ADR-0020 §3).
    /// </remarks>
    [Fact]
    public async Task Ein_schweigender_Ledger_antwortet_503()
    {
        await Stelle();
        _tor.Schweigt = true;

        (await AlsFirma().GetAsync(Ziel("/assessments"))).StatusCode
            .Should().Be(HttpStatusCode.ServiceUnavailable);

        // Die Personenseite bleibt erreichbar: sie fragt den Ledger nicht.
        (await AlsPerson(Anna).GetAsync(Ziel("/assessments/me"))).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Zweimal einreichen geht nicht, zweimal bewerten auch nicht.</summary>
    [Fact]
    public async Task Eingereicht_und_bewertet_wird_je_einmal()
    {
        var id = await Stelle();
        await Reiche_ein(id);

        (await AlsPerson(Anna).PostAsJsonAsync(
                Ziel($"/assessments/{id}/submission"),
                new Dictionary<string, object?> { ["text"] = "Und noch einmal." }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        await Bewerte(id, "accepted", "Gut.");

        (await AlsFirma().PostAsJsonAsync(
                Ziel($"/assessments/{id}/evaluation"),
                new Dictionary<string, object?>
                {
                    ["outcome"] = "rejected",
                    ["text"] = "Doch nicht."
                }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Die Aufgabe erzeugt einen Vermerk, die Bewertung auch.</summary>
    /// <remarks>
    /// Zwei Vermerke, beide an die Person, beide inhaltsfrei: eine Kennung und
    /// eine Art (ADR-0025). Kein Firmenname, kein Aufgabentext, keine Bewertung
    /// — eine Mail kann im Postfach beim jetzigen Arbeitgeber landen.
    /// </remarks>
    [Fact]
    public async Task Die_Person_wird_zweimal_in_Kenntnis_gesetzt()
    {
        var id = await Stelle();
        await Reiche_ein(id);
        await Bewerte(id, "accepted", "Passt.");

        await using var kontext = Kontext();

        var vermerke = await kontext.Set<OutboxZeile>().ToListAsync();

        vermerke.Should().HaveCount(2);
        vermerke.Should().OnlyContain(
            zeile => zeile.UserId == Anna && zeile.Kind == "assessment_update");
    }

    /// <summary>Die Löschung nimmt Vorgänge und Vermerke — und behält nichts.</summary>
    /// <remarks>
    /// Kein Aufbewahrungsfall, auch nicht für die Bewertung: sie ist kein Beleg,
    /// der jemand anderem gehört. Eine, die die Löschung überlebte, wäre das
    /// Zeugnis, das ADR-0042 ausschliesst.
    /// </remarks>
    [Fact]
    public async Task Die_Loeschung_nimmt_Vorgaenge_und_Vermerke()
    {
        var id = await Stelle();
        await Reiche_ein(id);
        await Bewerte(id, "rejected", "Leider nicht.");

        var client = _dienst.CreateClient();
        client.DefaultRequestHeaders.Add("X-Erasure-Secret", Loeschgeheimnis);

        var antwort = await client.PostAsJsonAsync(
            Ziel("/erasure"), new Dictionary<string, object?> { ["user_id"] = Anna });

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(antwort)).GetProperty("retained").GetInt32().Should().Be(0);

        await using var kontext = Kontext();

        (await kontext.Vorgaenge.CountAsync()).Should().Be(0);
        (await kontext.Set<OutboxZeile>().CountAsync()).Should().Be(0);
    }

    /// <summary>Ohne das Geheimnis löscht niemand.</summary>
    [Fact]
    public async Task Ohne_Geheimnis_loescht_niemand()
    {
        await Stelle();

        var antwort = await _dienst.CreateClient().PostAsJsonAsync(
            Ziel("/erasure"), new Dictionary<string, object?> { ["user_id"] = Anna });

        antwort.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var kontext = Kontext();

        (await kontext.Vorgaenge.CountAsync()).Should().Be(1);
    }

    // ---------------------------------------------------------------------

    private static Uri Ziel(string pfad) => new(pfad, UriKind.Relative);

    private static string Verben(RouteEndpoint endpunkt) =>
        string.Join(
            ",",
            endpunkt.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()
                ?.HttpMethods ?? ["?"]);

    private static Dictionary<string, object?> Aufgabe(Guid wer) => new()
    {
        ["subject_id"] = wer,
        ["title"] = "Kleiner Dienst",
        ["task"] = "Bau einen kleinen Dienst, der eine Liste ausliefert.",
        ["hours"] = 4,
        ["due_at"] = DateTimeOffset.UtcNow.AddDays(7)
    };

    private async Task<Guid> Stelle()
    {
        var antwort = await AlsFirma().PostAsJsonAsync(Ziel("/assessments"), Aufgabe(Anna));

        antwort.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await Json(antwort)).GetProperty("id").GetGuid();
    }

    private async Task Reiche_ein(Guid id) =>
        (await AlsPerson(Anna).PostAsJsonAsync(
            Ziel($"/assessments/{id}/submission"),
            new Dictionary<string, object?> { ["text"] = "Liegt im Repository." }))
        .EnsureSuccessStatusCode();

    private async Task Bewerte(Guid id, string ausgang, string text) =>
        (await AlsFirma().PostAsJsonAsync(
            Ziel($"/assessments/{id}/evaluation"),
            new Dictionary<string, object?> { ["outcome"] = ausgang, ["text"] = text }))
        .EnsureSuccessStatusCode();

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

    private AssessmentDbContext Kontext()
    {
        var bauer = new DbContextOptionsBuilder<AssessmentDbContext>();
        AssessmentDbContextFactory.ZurEntwurfszeit(bauer, postgres.ConnectionString);

        return new AssessmentDbContext(bauer.Options);
    }
}
