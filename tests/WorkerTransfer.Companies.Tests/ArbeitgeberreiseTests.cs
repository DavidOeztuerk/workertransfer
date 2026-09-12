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
using WorkerTransfer.ServiceDefaults.Rollen;

namespace WorkerTransfer.Companies.Tests;

/// <summary>Schreiben, öffentlich lesen, und was das Kürzel verspricht.</summary>
[Collection(PostgresCollection.Name)]
public class ArbeitgeberreiseTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _dienst = null!;

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:companies", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("environment", "Development");
            // Das Schaufenster zu schreiben verlangt seit PBI-2 einen `admin`.
            // Diese Reihe misst das Profil und nicht die Rolle — also antwortet
            // die Probe wie eine Inhaberin. `RollenTests` dreht sie auf
            // `member`.
            host.ConfigureTestServices(dienste =>
                dienste.Replace(ServiceDescriptor.Scoped<IFirmenrollen>(_ => new Rollenprobe())));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AlsFirma(Guid firma)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Tokenform.Firma(Guid.CreateVersion7(), firma));
        return browser;
    }

    private HttpClient AlsPerson()
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Tokenform.Person(Guid.CreateVersion7()));
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Schreibe(
        HttpClient browser,
        string name = "Muster GmbH",
        string? netzseite = "https://muster.example",
        string[]? orte = null,
        string[]? leistungen = null) =>
        browser.PutAsJsonAsync("/companies/me/profile", new
        {
            display_name = name,
            about = "Wir bauen verteilte Systeme.",
            website = netzseite,
            locations = orte ?? ["Berlin"],
            benefits = leistungen ?? ["Homeoffice"]
        });

    [Fact]
    public async Task Die_Briefanschrift_ueberlebt_die_Runde()
    {
        var firma = Guid.CreateVersion7();
        var browser = AlsFirma(firma);

        var geschrieben = await browser.PutAsJsonAsync("/companies/me/profile", new
        {
            display_name = "Muster GmbH",
            about = "Wir bauen verteilte Systeme.",
            website = "https://muster.example",
            locations = new[] { "Berlin" },
            benefits = new[] { "Homeoffice" },
            line1 = "Musterstraße 1",
            postal_code = "10115",
            city = "Berlin",
            country = "DE",
            phone = "+493012345"
        });

        geschrieben.StatusCode.Should().Be(HttpStatusCode.OK);

        var oeffentlich = await Json(
            await _dienst.CreateClient().GetAsync($"/companies/{firma}/profile"));

        oeffentlich.GetProperty("line1").GetString().Should().Be("Musterstraße 1");
        oeffentlich.GetProperty("postal_code").GetString().Should().Be("10115");
        oeffentlich.GetProperty("city").GetString().Should().Be("Berlin");
        oeffentlich.GetProperty("country").GetString().Should().Be("DE");
        oeffentlich.GetProperty("phone").GetString().Should().Be("+493012345");
        oeffentlich.GetProperty("display_name").GetString().Should().Be("Muster GmbH");
    }

    /// <summary>Der gewöhnliche Weg: einmal schreiben, öffentlich lesbar sein.</summary>
    [Fact]
    public async Task Ein_Profil_ist_ohne_Anmeldung_lesbar()
    {
        var firma = Guid.CreateVersion7();

        var geschrieben = await Schreibe(AlsFirma(firma));

        geschrieben.StatusCode.Should().Be(HttpStatusCode.OK);

        var kuerzel = (await Json(geschrieben)).GetProperty("slug").GetString();

        var ohneAnmeldung = _dienst.CreateClient();

        var ueberKennung = await ohneAnmeldung.GetAsync($"/companies/{firma}/profile");
        var ueberKuerzel = await ohneAnmeldung.GetAsync($"/companies/by-slug/{kuerzel}");

        ueberKennung.StatusCode.Should().Be(HttpStatusCode.OK);
        ueberKuerzel.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(ueberKuerzel)).GetProperty("tenant_id").GetGuid().Should().Be(firma);
    }

    /// <summary>
    /// Das Kürzel ist ein Versprechen: es folgt dem Anzeigenamen nicht.
    /// </summary>
    /// <remarks>
    /// Ein Kürzel, das mitwandert, bricht jeden geteilten Link auf die
    /// Karriere-Seite.
    /// </remarks>
    [Fact]
    public async Task Das_Kuerzel_bleibt_wenn_der_Name_sich_aendert()
    {
        var firma = Guid.CreateVersion7();
        var browser = AlsFirma(firma);

        var zuerst = (await Json(await Schreibe(browser, "Alte Marke")))
            .GetProperty("slug").GetString();

        var danach = (await Json(await Schreibe(browser, "Ganz neue Marke")))
            .GetProperty("slug").GetString();

        danach.Should().Be(zuerst);
        (await _dienst.CreateClient().GetAsync($"/companies/by-slug/{zuerst}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Zwei Unternehmen mit demselben Namen bekommen zwei Adressen.</summary>
    [Fact]
    public async Task Ein_belegtes_Kuerzel_bekommt_einen_Zaehler()
    {
        const string name = "Gleichnamig AG";

        var eine = (await Json(await Schreibe(AlsFirma(Guid.CreateVersion7()), name)))
            .GetProperty("slug").GetString();
        var andere = (await Json(await Schreibe(AlsFirma(Guid.CreateVersion7()), name)))
            .GetProperty("slug").GetString();

        eine.Should().NotBe(andere);
        andere.Should().StartWith(eine);
    }

    /// <summary>
    /// Das Kürzel trägt die Mandanten-Kennung nicht — sie stünde sonst in einer
    /// Adresse, die weitergegeben wird.
    /// </summary>
    [Fact]
    public async Task Das_Kuerzel_verraet_die_Mandantenkennung_nicht()
    {
        var firma = Guid.CreateVersion7();

        var kuerzel = (await Json(await Schreibe(AlsFirma(firma), "Diskret GmbH")))
            .GetProperty("slug").GetString();

        kuerzel.Should().NotContain(firma.ToString());
        kuerzel.Should().NotContain(firma.ToString("N"));
    }

    /// <summary>Umlaute werden zerlegt, ihre Grundbuchstaben bleiben.</summary>
    [Fact]
    public async Task Aus_dem_Namen_wird_eine_Adresse()
    {
        var geschrieben = await Schreibe(AlsFirma(Guid.CreateVersion7()), "Grün & Söhne GmbH");

        (await Json(geschrieben)).GetProperty("slug").GetString()
            .Should().Be("grun-sohne-gmbh");
    }

    /// <summary>
    /// Bleibt vom Namen nichts Verwendbares übrig, ist eine unpersönliche
    /// Adresse besser als gar keine.
    /// </summary>
    [Fact]
    public async Task Ein_Name_ganz_ohne_lateinische_Buchstaben_bekommt_trotzdem_eine_Adresse()
    {
        var geschrieben = await Schreibe(AlsFirma(Guid.CreateVersion7()), "株式会社");

        var kuerzel = (await Json(geschrieben)).GetProperty("slug").GetString();

        kuerzel.Should().StartWith("unternehmen");
        (await _dienst.CreateClient().GetAsync($"/companies/by-slug/{kuerzel}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>„Noch keins" ist ein Zustand, kein Fehler.</summary>
    [Fact]
    public async Task Das_eigene_Profil_ist_null_solange_keins_da_ist()
    {
        var antwort = await AlsFirma(Guid.CreateVersion7()).GetAsync("/companies/me/profile");

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);
        (await antwort.Content.ReadAsStringAsync()).Trim().Should().Be("null");
    }

    /// <summary>
    /// Für die Öffentlichkeit gibt es nichts, solange nichts angelegt wurde —
    /// dann bleibt eine Stelle anonym.
    /// </summary>
    [Fact]
    public async Task Oeffentlich_gibt_es_nichts_solange_nichts_angelegt_ist()
    {
        var antwort = await _dienst.CreateClient()
            .GetAsync($"/companies/{Guid.CreateVersion7()}/profile");

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);
        (await antwort.Content.ReadAsStringAsync()).Trim().Should().Be("null");
    }

    /// <summary>Ein Kürzel, das es nicht gibt, ist 404 und kein Absturz.</summary>
    [Fact]
    public async Task Ein_unbekanntes_Kuerzel_ist_404()
    {
        var antwort = await _dienst.CreateClient().GetAsync("/companies/by-slug/gibt-es-nicht");

        antwort.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Eine Aussage über den Aufrufer, nicht über ein fremdes Unternehmen.
    /// </summary>
    [Fact]
    public async Task Eine_Privatperson_schreibt_kein_Arbeitgeberprofil()
    {
        var antwort = await Schreibe(AlsPerson());

        antwort.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Ohne Anmeldung gibt es kein eigenes Profil.</summary>
    [Fact]
    public async Task Ohne_Token_ist_das_eigene_Profil_401()
    {
        var antwort = await _dienst.CreateClient().GetAsync("/companies/me/profile");

        antwort.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Ein Link wird von fremden Menschen angeklickt (ADR-0021).
    /// </summary>
    /// <remarks>
    /// <c>ftp://</c> steht hier neben <c>javascript:</c>, und es ist der
    /// wichtigere Fall: <c>javascript:alert(1)</c> hat gar keinen Wirt und
    /// fällt schon an der Wirtsprüfung, weshalb es über die Schemaprüfung
    /// nichts aussagt. Eine Gegenprobe hat genau das gezeigt — ohne
    /// Schemaprüfung blieb die Reihe grün.
    /// </remarks>
    [Fact]
    public async Task Nur_http_und_https_kommen_durch()
    {
        var browser = AlsFirma(Guid.CreateVersion7());

        var boese = await Schreibe(browser, netzseite: "javascript:alert(1)");

        boese.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var fremdesSchema = await Schreibe(browser, netzseite: "ftp://muster.example/datei");

        fremdesSchema.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var ohneWirt = await Schreibe(browser, netzseite: "https://");

        ohneWirt.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Leer und „nicht angegeben" sind dasselbe.</summary>
    /// <remarks>
    /// Eine leere Zeichenkette würde als Link dargestellt und führte ins
    /// Nichts.
    /// </remarks>
    [Fact]
    public async Task Ein_leerer_Link_wird_null()
    {
        var geschrieben = await Schreibe(AlsFirma(Guid.CreateVersion7()), netzseite: "   ");

        geschrieben.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(geschrieben)).GetProperty("website").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Erst entdoppeln, dann zählen — sonst würde jemand mit einundzwanzigmal
    /// „Homeoffice" abgewiesen, obwohl daraus ein Eintrag wird.
    /// </summary>
    [Fact]
    public async Task Dubletten_zaehlen_nicht_gegen_die_Grenze()
    {
        var vieleGleiche = Enumerable.Repeat("Homeoffice", 21).ToArray();

        var geschrieben = await Schreibe(
            AlsFirma(Guid.CreateVersion7()), leistungen: vieleGleiche);

        geschrieben.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(geschrieben)).GetProperty("benefits").GetArrayLength().Should().Be(1);
    }

    /// <summary>Einundzwanzig verschiedene Einträge sind zu viele.</summary>
    [Fact]
    public async Task Mehr_als_zwanzig_Eintraege_werden_abgewiesen()
    {
        var vieleVerschiedene = Enumerable.Range(1, 21).Select(n => $"Ort {n}").ToArray();

        var geschrieben = await Schreibe(AlsFirma(Guid.CreateVersion7()), orte: vieleVerschiedene);

        geschrieben.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Ein abgelehntes Formular hinterlässt kein halb geändertes Profil.</summary>
    [Fact]
    public async Task Eine_abgewiesene_Aenderung_aendert_nichts()
    {
        var firma = Guid.CreateVersion7();
        var browser = AlsFirma(firma);
        await Schreibe(browser, "Erste Fassung");

        var abgewiesen = await browser.PutAsJsonAsync("/companies/me/profile", new
        {
            display_name = "Zweite Fassung",
            about = "Geändert.",
            website = "javascript:alert(1)",
            locations = new[] { "Hamburg" },
            benefits = Array.Empty<string>()
        });

        abgewiesen.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var jetzt = await Json(await browser.GetAsync("/companies/me/profile"));

        jetzt.GetProperty("display_name").GetString().Should().Be("Erste Fassung");
        jetzt.GetProperty("locations").EnumerateArray()
            .Select(eintrag => eintrag.GetString()).Should().Equal("Berlin");
    }

    /// <summary>Ein Anzeigename ist Pflicht — ohne ihn zeigt sich niemand.</summary>
    [Fact]
    public async Task Ohne_Anzeigenamen_geht_es_nicht()
    {
        var antwort = await Schreibe(AlsFirma(Guid.CreateVersion7()), "   ");

        antwort.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Ein fremdes Profil lässt sich nicht überschreiben.</summary>
    /// <remarks>
    /// Wem das Profil gehört, steht im geprüften Token und nie im Rumpf — es
    /// gibt kein <c>tenant_id</c>-Feld, in das man eine fremde Kennung
    /// schreiben könnte.
    /// </remarks>
    [Fact]
    public async Task Wem_das_Profil_gehoert_steht_im_Token()
    {
        var eine = Guid.CreateVersion7();
        var andere = Guid.CreateVersion7();
        await Schreibe(AlsFirma(eine), "Die Eine");

        var versuch = await AlsFirma(andere).PutAsJsonAsync("/companies/me/profile", new
        {
            tenant_id = eine,
            display_name = "Die Andere",
            about = string.Empty,
            website = (string?)null,
            locations = Array.Empty<string>(),
            benefits = Array.Empty<string>()
        });

        versuch.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(versuch)).GetProperty("tenant_id").GetGuid().Should().Be(andere);

        var unberuehrt = await Json(
            await _dienst.CreateClient().GetAsync($"/companies/{eine}/profile"));

        unberuehrt.GetProperty("display_name").GetString().Should().Be("Die Eine");
    }

    /// <summary>Zwei Speicherungen legen keine zweite Zeile an.</summary>
    [Fact]
    public async Task Es_gibt_genau_ein_Profil_je_Unternehmen()
    {
        var firma = Guid.CreateVersion7();
        var browser = AlsFirma(firma);

        await Schreibe(browser, "Einmal");
        await Schreibe(browser, "Zweimal");

        await using var kontext = postgres.Kontext();

        (await kontext.Profile.CountAsync(zeile => zeile.Id == firma)).Should().Be(1);
    }
}
