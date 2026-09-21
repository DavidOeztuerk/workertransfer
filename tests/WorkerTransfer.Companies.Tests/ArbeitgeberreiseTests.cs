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
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.ServiceDefaults;

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
            host.UseSetting("Jwt:PublicKey", Tokenform.OeffentlichSchluessel);
            host.UseSetting("Jwt:KeyId", Tokenform.Kennung);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("environment", "Development");
            // Das Schaufenster zu schreiben verlangt seit PBI-2 einen `admin`.
            // Diese Reihe misst das Profile und nicht die Rolle — also antwortet
            // die Probe wie eine Inhaberin. `RollenTests` dreht sie auf
            // `member`.
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<ICompanyRoles>(_ => new Rollenprobe())));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AlsFirma(Guid company)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Tokenform.Firma(Guid.CreateVersion7(), company));
        return browser;
    }

    private HttpClient AlsPerson()
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Tokenform.Person(Guid.CreateVersion7()));
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Write(
        HttpClient browser,
        string name = "Muster GmbH",
        string? website = "https://muster.example",
        string[]? locations = null,
        string[]? benefits = null) =>
        browser.PutAsJsonAsync("/companies/me/profile", new
        {
            display_name = name,
            about = "Wir bauen verteilte Systeme.",
            website = website,
            locations = locations ?? ["Berlin"],
            benefits = benefits ?? ["Homeoffice"]
        });

    [Fact]
    public async Task The_postal_address_survives_the_round_trip()
    {
        var company = Guid.CreateVersion7();
        var browser = AlsFirma(company);

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
            await _dienst.CreateClient().GetAsync($"/companies/{company}/profile"));

        oeffentlich.GetProperty("line1").GetString().Should().Be("Musterstraße 1");
        oeffentlich.GetProperty("postal_code").GetString().Should().Be("10115");
        oeffentlich.GetProperty("city").GetString().Should().Be("Berlin");
        oeffentlich.GetProperty("country").GetString().Should().Be("DE");
        oeffentlich.GetProperty("phone").GetString().Should().Be("+493012345");
        oeffentlich.GetProperty("display_name").GetString().Should().Be("Muster GmbH");
    }

    /// <summary>Der gewöhnliche Weg: einmal schreiben, öffentlich lesbar sein.</summary>
    [Fact]
    public async Task A_profile_is_readable_without_signing_in()
    {
        var company = Guid.CreateVersion7();

        var geschrieben = await Write(AlsFirma(company));

        geschrieben.StatusCode.Should().Be(HttpStatusCode.OK);

        var slug = (await Json(geschrieben)).GetProperty("slug").GetString();

        var ohneAnmeldung = _dienst.CreateClient();

        var ueberKennung = await ohneAnmeldung.GetAsync($"/companies/{company}/profile");
        var ueberKuerzel = await ohneAnmeldung.GetAsync($"/companies/by-slug/{slug}");

        ueberKennung.StatusCode.Should().Be(HttpStatusCode.OK);
        ueberKuerzel.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(ueberKuerzel)).GetProperty("tenant_id").GetGuid().Should().Be(company);
    }

    /// <summary>
    /// Das Kürzel ist ein Versprechen: es folgt dem Anzeigenamen nicht.
    /// </summary>
    /// <remarks>
    /// Ein Kürzel, das mitwandert, bricht jeden geteilten Link auf die
    /// Karriere-Seite.
    /// </remarks>
    [Fact]
    public async Task The_slug_stays_when_the_name_changes()
    {
        var company = Guid.CreateVersion7();
        var browser = AlsFirma(company);

        var zuerst = (await Json(await Write(browser, "Alte Marke")))
            .GetProperty("slug").GetString();

        var danach = (await Json(await Write(browser, "Ganz neue Marke")))
            .GetProperty("slug").GetString();

        danach.Should().Be(zuerst);
        (await _dienst.CreateClient().GetAsync($"/companies/by-slug/{zuerst}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Zwei Unternehmen mit demselben Namen bekommen zwei Adressen.</summary>
    [Fact]
    public async Task A_taken_slug_gets_a_counter()
    {
        const string name = "Gleichnamig AG";

        var eine = (await Json(await Write(AlsFirma(Guid.CreateVersion7()), name)))
            .GetProperty("slug").GetString();
        var andere = (await Json(await Write(AlsFirma(Guid.CreateVersion7()), name)))
            .GetProperty("slug").GetString();

        eine.Should().NotBe(andere);
        andere.Should().StartWith(eine);
    }

    /// <summary>
    /// Das Kürzel trägt die Mandanten-Kennung nicht — sie stünde sonst in einer
    /// Adresse, die weitergegeben wird.
    /// </summary>
    [Fact]
    public async Task The_slug_does_not_reveal_the_tenant_id()
    {
        var company = Guid.CreateVersion7();

        var slug = (await Json(await Write(AlsFirma(company), "Diskret GmbH")))
            .GetProperty("slug").GetString();

        slug.Should().NotContain(company.ToString());
        slug.Should().NotContain(company.ToString("N"));
    }

    /// <summary>Umlaute werden zerlegt, ihre Grundbuchstaben bleiben.</summary>
    [Fact]
    public async Task A_name_becomes_an_address()
    {
        var geschrieben = await Write(AlsFirma(Guid.CreateVersion7()), "Grün & Söhne GmbH");

        (await Json(geschrieben)).GetProperty("slug").GetString()
            .Should().Be("grun-sohne-gmbh");
    }

    /// <summary>
    /// Bleibt vom Namen nichts Verwendbares übrig, ist eine unpersönliche
    /// Adresse besser als gar keine.
    /// </summary>
    [Fact]
    public async Task A_name_with_no_latin_letters_at_all_still_gets_an_address()
    {
        var geschrieben = await Write(AlsFirma(Guid.CreateVersion7()), "株式会社");

        var slug = (await Json(geschrieben)).GetProperty("slug").GetString();

        slug.Should().StartWith("unternehmen");
        (await _dienst.CreateClient().GetAsync($"/companies/by-slug/{slug}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>„Noch keins" ist ein Zustand, kein Fehler.</summary>
    [Fact]
    public async Task Ones_own_profile_is_null_until_one_exists()
    {
        var response = await AlsFirma(Guid.CreateVersion7()).GetAsync("/companies/me/profile");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Trim().Should().Be("null");
    }

    /// <summary>
    /// Für die Öffentlichkeit gibt es nichts, solange nichts angelegt wurde —
    /// dann bleibt eine Job anonym.
    /// </summary>
    [Fact]
    public async Task Publicly_there_is_nothing_until_something_is_created()
    {
        var response = await _dienst.CreateClient()
            .GetAsync($"/companies/{Guid.CreateVersion7()}/profile");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Trim().Should().Be("null");
    }

    /// <summary>Ein Kürzel, das es nicht gibt, ist 404 und kein Absturz.</summary>
    [Fact]
    public async Task An_unknown_slug_is_404()
    {
        var response = await _dienst.CreateClient().GetAsync("/companies/by-slug/gibt-es-nicht");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Eine Aussage über den Aufrufer, nicht über ein fremdes Unternehmen.
    /// </summary>
    [Fact]
    public async Task A_private_person_writes_no_employer_profile()
    {
        var response = await Write(AlsPerson());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Ohne Anmeldung gibt es kein eigenes Profile.</summary>
    [Fact]
    public async Task Without_a_token_ones_own_profile_is_401()
    {
        var response = await _dienst.CreateClient().GetAsync("/companies/me/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
    public async Task Only_http_and_https_get_through()
    {
        var browser = AlsFirma(Guid.CreateVersion7());

        var boese = await Write(browser, website: "javascript:alert(1)");

        boese.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var fremdesSchema = await Write(browser, website: "ftp://muster.example/datei");

        fremdesSchema.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var ohneWirt = await Write(browser, website: "https://");

        ohneWirt.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Leer und „nicht angegeben" sind dasselbe.</summary>
    /// <remarks>
    /// Eine leere Zeichenkette würde als Link dargestellt und führte ins
    /// Nichts.
    /// </remarks>
    [Fact]
    public async Task An_empty_link_becomes_null()
    {
        var geschrieben = await Write(AlsFirma(Guid.CreateVersion7()), website: "   ");

        geschrieben.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(geschrieben)).GetProperty("website").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Erst entdoppeln, dann zählen — sonst würde jemand mit einundzwanzigmal
    /// „Homeoffice" abgewiesen, obwohl daraus ein Eintrag wird.
    /// </summary>
    [Fact]
    public async Task Duplicates_do_not_count_against_the_limit()
    {
        var vieleGleiche = Enumerable.Repeat("Homeoffice", 21).ToArray();

        var geschrieben = await Write(
            AlsFirma(Guid.CreateVersion7()), benefits: vieleGleiche);

        geschrieben.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(geschrieben)).GetProperty("benefits").GetArrayLength().Should().Be(1);
    }

    /// <summary>Einundzwanzig verschiedene Einträge sind zu viele.</summary>
    [Fact]
    public async Task More_than_twenty_entries_are_refused()
    {
        var vieleVerschiedene = Enumerable.Range(1, 21).Select(n => $"Ort {n}").ToArray();

        var geschrieben = await Write(AlsFirma(Guid.CreateVersion7()), locations: vieleVerschiedene);

        geschrieben.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Ein abgelehntes Formular hinterlässt kein halb geändertes Profile.</summary>
    [Fact]
    public async Task A_refused_change_changes_nothing()
    {
        var company = Guid.CreateVersion7();
        var browser = AlsFirma(company);
        await Write(browser, "Erste Fassung");

        var abgewiesen = await browser.PutAsJsonAsync("/companies/me/profile", new
        {
            display_name = "Zweite Fassung",
            about = "Geändert.",
            website = "javascript:alert(1)",
            locations = new[] { "Hamburg" },
            benefits = Array.Empty<string>()
        });

        abgewiesen.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var now = await Json(await browser.GetAsync("/companies/me/profile"));

        now.GetProperty("display_name").GetString().Should().Be("Erste Fassung");
        now.GetProperty("locations").EnumerateArray()
            .Select(entry => entry.GetString()).Should().Equal("Berlin");
    }

    /// <summary>Ein DisplayName ist Pflicht — ohne ihn zeigt sich niemand.</summary>
    [Fact]
    public async Task Without_a_display_name_it_does_not_work()
    {
        var response = await Write(AlsFirma(Guid.CreateVersion7()), "   ");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Ein fremdes Profile lässt sich nicht überschreiben.</summary>
    /// <remarks>
    /// Wem das Profile gehört, steht im geprüften Token und nie im Rumpf — es
    /// gibt kein <c>tenant_id</c>-Feld, in das man eine fremde Kennung
    /// schreiben könnte.
    /// </remarks>
    [Fact]
    public async Task Whose_profile_it_is_stands_in_the_token()
    {
        var eine = Guid.CreateVersion7();
        var andere = Guid.CreateVersion7();
        await Write(AlsFirma(eine), "Die Eine");

        var attempt = await AlsFirma(andere).PutAsJsonAsync("/companies/me/profile", new
        {
            tenant_id = eine,
            display_name = "Die Andere",
            about = string.Empty,
            website = (string?)null,
            locations = Array.Empty<string>(),
            benefits = Array.Empty<string>()
        });

        attempt.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(attempt)).GetProperty("tenant_id").GetGuid().Should().Be(andere);

        var unberuehrt = await Json(
            await _dienst.CreateClient().GetAsync($"/companies/{eine}/profile"));

        unberuehrt.GetProperty("display_name").GetString().Should().Be("Die Eine");
    }

    /// <summary>Zwei Speicherungen legen keine zweite Zeile an.</summary>
    [Fact]
    public async Task There_is_exactly_one_profile_per_company()
    {
        var company = Guid.CreateVersion7();
        var browser = AlsFirma(company);

        await Write(browser, "Einmal");
        await Write(browser, "Zweimal");

        await using var context = postgres.Kontext();

        (await context.Profile.CountAsync(row => row.Id == company)).Should().Be(1);
    }
}
