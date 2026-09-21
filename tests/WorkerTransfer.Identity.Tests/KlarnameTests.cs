using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Identity.Infrastructure.Post;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Tests;

/// <summary>Vorname, Nachname, PostalAddress — ADR-0038.</summary>
[Collection(PostgresCollection.Name)]
public class FullNameTests(Postgres postgres) : IAsyncLifetime
{
    private const string Password = "geheim-und-lang-genug";

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly MailRecord _post = new();

    public async Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:identity", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:PrivateKey", TokenShape.PrivateKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Mail:WebAdresse", "http://localhost:5173");
            host.UseSetting("Notify:Geheimnis", "melde-geheimnis");
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IMailSender>(_post)));
        });

        using var scope = _dienst.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Browser() =>
        _dienst.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private async Task<HttpClient> SignedIn(string? givenName = null, string? familyName = null)
    {
        var address = $"anna-{Guid.NewGuid():N}@firma-{Guid.NewGuid():N}.example";
        var browser = Browser();

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email = address,
            password = Password,
            display_name = "Anna",
            given_name = givenName,
            family_name = familyName
        });

        var token = Regex.Match(
            _post.Post[^1].Text, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;
        await browser.PostAsJsonAsync("/auth/verify-email", new { token });
        await browser.PostAsJsonAsync("/auth/login", new { email = address, password = Password });
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Registration_without_a_given_name_is_fine()
    {
        var browser = await SignedIn();
        var session = await Json(await browser.GetAsync("/auth/session"));

        session.GetProperty("user").GetProperty("display_name").GetString().Should().Be("Anna");
        session.GetProperty("user").GetProperty("given_name").ValueKind
            .Should().Be(JsonValueKind.Null);
        session.GetProperty("user").TryGetProperty("line1", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Registration_takes_civil_names_in_snake_case()
    {
        var browser = await SignedIn("Anna", "Beispiel");
        var session = await Json(await browser.GetAsync("/auth/session"));
        var subject = session.GetProperty("user");

        subject.GetProperty("given_name").GetString().Should().Be("Anna");
        subject.GetProperty("family_name").GetString().Should().Be("Beispiel");
        subject.GetProperty("display_name").GetString().Should().Be("Anna");
    }

    [Fact]
    public async Task The_civil_name_can_be_set_and_cleared()
    {
        var browser = await SignedIn();

        (await browser.PutAsJsonAsync("/account/name", new
        {
            given_name = "Bea",
            family_name = "Muster"
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var session = await Json(await browser.GetAsync("/auth/session"));
        session.GetProperty("user").GetProperty("given_name").GetString().Should().Be("Bea");

        (await browser.PutAsJsonAsync("/account/name", new
        {
            given_name = "",
            family_name = ""
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        session = await Json(await browser.GetAsync("/me"));
        session.GetProperty("given_name").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_address_is_empty_until_somebody_sets_it_and_is_not_in_the_session()
    {
        var browser = await SignedIn();

        var leer = await Json(await browser.GetAsync("/account/address"));
        leer.GetProperty("line1").GetString().Should().Be("");
        leer.GetProperty("country").GetString().Should().Be("DE");

        (await browser.PutAsJsonAsync("/account/address", new
        {
            line1 = "Musterstraße 1",
            line2 = "",
            postal_code = "10115",
            city = "Berlin",
            country = "de",
            phone = "+49 30 123"
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var address = await Json(await browser.GetAsync("/account/address"));
        address.GetProperty("line1").GetString().Should().Be("Musterstraße 1");
        address.GetProperty("country").GetString().Should().Be("DE");
        address.GetProperty("phone").GetString().Should().Be("+49 30 123");

        var session = await Json(await browser.GetAsync("/auth/session"));
        session.GetProperty("user").TryGetProperty("line1", out _).Should().BeFalse();
        session.GetProperty("user").TryGetProperty("phone", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Without_a_token_the_address_stays_shut()
    {
        (await Browser().GetAsync("/account/address"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Browser().PutAsJsonAsync("/account/name", new { given_name = "X" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
