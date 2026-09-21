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

/// <summary>Writing, reading publicly, and what the slug promises.</summary>
[Collection(PostgresCollection.Name)]
public class EmployerJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _service = null!;

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:companies", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("environment", "Development");
            // Writing the shop window has demanded an `admin` since PBI-2.
            // This suite measures the profile and not the role — so the probe
            // answers like an owner. `CompanyRoleTests` turns it down to
            // `member`.
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<ICompanyRoles>(_ => new RoleProbe())));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AsCompany(Guid company)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TokenShape.Company(Guid.CreateVersion7(), company));
        return browser;
    }

    private HttpClient AsPerson()
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TokenShape.Person(Guid.CreateVersion7()));
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
        var browser = AsCompany(company);

        var written = await browser.PutAsJsonAsync("/companies/me/profile", new
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

        written.StatusCode.Should().Be(HttpStatusCode.OK);

        var publicRead = await Json(
            await _service.CreateClient().GetAsync($"/companies/{company}/profile"));

        publicRead.GetProperty("line1").GetString().Should().Be("Musterstraße 1");
        publicRead.GetProperty("postal_code").GetString().Should().Be("10115");
        publicRead.GetProperty("city").GetString().Should().Be("Berlin");
        publicRead.GetProperty("country").GetString().Should().Be("DE");
        publicRead.GetProperty("phone").GetString().Should().Be("+493012345");
        publicRead.GetProperty("display_name").GetString().Should().Be("Muster GmbH");
    }

    /// <summary>The ordinary path: write once, be publicly readable.</summary>
    [Fact]
    public async Task A_profile_is_readable_without_signing_in()
    {
        var company = Guid.CreateVersion7();

        var written = await Write(AsCompany(company));

        written.StatusCode.Should().Be(HttpStatusCode.OK);

        var slug = (await Json(written)).GetProperty("slug").GetString();

        var withoutSignIn = _service.CreateClient();

        var byTenantId = await withoutSignIn.GetAsync($"/companies/{company}/profile");
        var bySlug = await withoutSignIn.GetAsync($"/companies/by-slug/{slug}");

        byTenantId.StatusCode.Should().Be(HttpStatusCode.OK);
        bySlug.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(bySlug)).GetProperty("tenant_id").GetGuid().Should().Be(company);
    }

    /// <summary>
    /// The slug is a promise: it does not follow the display name.
    /// </summary>
    /// <remarks>
    /// A slug that travels along breaks every shared link to the careers page.
    /// </remarks>
    [Fact]
    public async Task The_slug_stays_when_the_name_changes()
    {
        var company = Guid.CreateVersion7();
        var browser = AsCompany(company);

        var first = (await Json(await Write(browser, "Alte Marke")))
            .GetProperty("slug").GetString();

        var after = (await Json(await Write(browser, "Ganz neue Marke")))
            .GetProperty("slug").GetString();

        after.Should().Be(first);
        (await _service.CreateClient().GetAsync($"/companies/by-slug/{first}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Two companies with the same name get two addresses.</summary>
    [Fact]
    public async Task A_taken_slug_gets_a_counter()
    {
        const string name = "Gleichnamig AG";

        var one = (await Json(await Write(AsCompany(Guid.CreateVersion7()), name)))
            .GetProperty("slug").GetString();
        var other = (await Json(await Write(AsCompany(Guid.CreateVersion7()), name)))
            .GetProperty("slug").GetString();

        one.Should().NotBe(other);
        other.Should().StartWith(one);
    }

    /// <summary>
    /// The slug does not carry the tenant id — it would otherwise sit in an
    /// address that people pass around.
    /// </summary>
    [Fact]
    public async Task The_slug_does_not_reveal_the_tenant_id()
    {
        var company = Guid.CreateVersion7();

        var slug = (await Json(await Write(AsCompany(company), "Diskret GmbH")))
            .GetProperty("slug").GetString();

        slug.Should().NotContain(company.ToString());
        slug.Should().NotContain(company.ToString("N"));
    }

    /// <summary>Umlauts are decomposed, their base letters stay.</summary>
    [Fact]
    public async Task A_name_becomes_an_address()
    {
        var written = await Write(AsCompany(Guid.CreateVersion7()), "Grün & Söhne GmbH");

        (await Json(written)).GetProperty("slug").GetString()
            .Should().Be("grun-sohne-gmbh");
    }

    /// <summary>
    /// When nothing usable is left of the name, an impersonal address is
    /// better than none at all.
    /// </summary>
    [Fact]
    public async Task A_name_with_no_latin_letters_at_all_still_gets_an_address()
    {
        var written = await Write(AsCompany(Guid.CreateVersion7()), "株式会社");

        var slug = (await Json(written)).GetProperty("slug").GetString();

        slug.Should().StartWith("unternehmen");
        (await _service.CreateClient().GetAsync($"/companies/by-slug/{slug}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>"None yet" is a state, not an error.</summary>
    [Fact]
    public async Task Ones_own_profile_is_null_until_one_exists()
    {
        var response = await AsCompany(Guid.CreateVersion7()).GetAsync("/companies/me/profile");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Trim().Should().Be("null");
    }

    /// <summary>
    /// For the public there is nothing for as long as nothing was created —
    /// then a company stays anonymous.
    /// </summary>
    [Fact]
    public async Task Publicly_there_is_nothing_until_something_is_created()
    {
        var response = await _service.CreateClient()
            .GetAsync($"/companies/{Guid.CreateVersion7()}/profile");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Trim().Should().Be("null");
    }

    /// <summary>A slug that does not exist is 404 and not a crash.</summary>
    [Fact]
    public async Task An_unknown_slug_is_404()
    {
        var response = await _service.CreateClient().GetAsync("/companies/by-slug/gibt-es-nicht");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A statement about the caller, not about somebody else's company.
    /// </summary>
    [Fact]
    public async Task A_private_person_writes_no_employer_profile()
    {
        var response = await Write(AsPerson());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Without signing in there is no profile of one's own.</summary>
    [Fact]
    public async Task Without_a_token_ones_own_profile_is_401()
    {
        var response = await _service.CreateClient().GetAsync("/companies/me/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A link gets clicked by strangers (ADR-0021).
    /// </summary>
    /// <remarks>
    /// <c>ftp://</c> stands here beside <c>javascript:</c>, and it is the
    /// more important case: <c>javascript:alert(1)</c> has no host at all and
    /// already falls at the host check, which is why it says nothing about the
    /// scheme check. A counter-probe showed exactly that — without the scheme
    /// check the suite stayed green.
    /// </remarks>
    [Fact]
    public async Task Only_http_and_https_get_through()
    {
        var browser = AsCompany(Guid.CreateVersion7());

        var boese = await Write(browser, website: "javascript:alert(1)");

        boese.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var fremdesSchema = await Write(browser, website: "ftp://muster.example/datei");

        fremdesSchema.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var ohneWirt = await Write(browser, website: "https://");

        ohneWirt.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Empty and "not given" are the same thing.</summary>
    /// <remarks>
    /// An empty string would be rendered as a link and lead nowhere.
    /// </remarks>
    [Fact]
    public async Task An_empty_link_becomes_null()
    {
        var written = await Write(AsCompany(Guid.CreateVersion7()), website: "   ");

        written.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(written)).GetProperty("website").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Deduplicate first, then count — otherwise somebody writing
    /// "Homeoffice" twenty-one times would be refused, although it collapses
    /// into one entry.
    /// </summary>
    [Fact]
    public async Task Duplicates_do_not_count_against_the_limit()
    {
        var vieleGleiche = Enumerable.Repeat("Homeoffice", 21).ToArray();

        var written = await Write(
            AsCompany(Guid.CreateVersion7()), benefits: vieleGleiche);

        written.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(written)).GetProperty("benefits").GetArrayLength().Should().Be(1);
    }

    /// <summary>Twenty-one different entries are too many.</summary>
    [Fact]
    public async Task More_than_twenty_entries_are_refused()
    {
        var vieleVerschiedene = Enumerable.Range(1, 21).Select(n => $"Ort {n}").ToArray();

        var written = await Write(AsCompany(Guid.CreateVersion7()), locations: vieleVerschiedene);

        written.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>A refused form leaves no half-changed profile behind.</summary>
    [Fact]
    public async Task A_refused_change_changes_nothing()
    {
        var company = Guid.CreateVersion7();
        var browser = AsCompany(company);
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

    /// <summary>A display name is required — without it nobody shows themselves.</summary>
    [Fact]
    public async Task Without_a_display_name_it_does_not_work()
    {
        var response = await Write(AsCompany(Guid.CreateVersion7()), "   ");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Somebody else's profile cannot be overwritten.</summary>
    /// <remarks>
    /// Whom the profile belongs to stands in the verified token and never in
    /// the body — there is no <c>tenant_id</c> field into which somebody could
    /// write a foreign id.
    /// </remarks>
    [Fact]
    public async Task Whose_profile_it_is_stands_in_the_token()
    {
        var one = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        await Write(AsCompany(one), "Die Eine");

        var attempt = await AsCompany(other).PutAsJsonAsync("/companies/me/profile", new
        {
            tenant_id = one,
            display_name = "Die Andere",
            about = string.Empty,
            website = (string?)null,
            locations = Array.Empty<string>(),
            benefits = Array.Empty<string>()
        });

        attempt.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(attempt)).GetProperty("tenant_id").GetGuid().Should().Be(other);

        var unberuehrt = await Json(
            await _service.CreateClient().GetAsync($"/companies/{one}/profile"));

        unberuehrt.GetProperty("display_name").GetString().Should().Be("Die Eine");
    }

    /// <summary>Two saves create no second row.</summary>
    [Fact]
    public async Task There_is_exactly_one_profile_per_company()
    {
        var company = Guid.CreateVersion7();
        var browser = AsCompany(company);

        await Write(browser, "Einmal");
        await Write(browser, "Zweimal");

        await using var context = postgres.Context();

        (await context.Profile.CountAsync(row => row.Id == company)).Should().Be(1);
    }
}
