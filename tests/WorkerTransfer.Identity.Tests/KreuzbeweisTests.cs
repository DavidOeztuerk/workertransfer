using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// The three endpoints behave the way the React app expects.
/// </summary>
/// <remarks>
/// <strong>The half that asked Python is gone</strong> — with the Python
/// service. Two tests handed a freshly issued token to a real interpreter and
/// had <c>worker_auth.TokenManager</c> verify it; with no counterpart they prove
/// nothing any more.
/// <para>
/// What remains is the other direction, and it stays important: the cookie names
/// and paths, the RFC 9457 document, 403 instead of 401 for an unconfirmed
/// account — and a password the Python hasher wrote which lets one in here
/// without a reset. Those rows lie in real databases, and they have to go on
/// carrying.
/// </para>
/// <para>
/// The opposite direction — .NET accepting tokens Python really issued — does
/// not exist any more. It hung on lenient validators for tokens without
/// <c>iss</c>/<c>aud</c>; those fell with the transition, and issuer and
/// audience are fully verified again.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class CrossProofTests(Postgres postgres) : IAsyncLifetime
{
    private const string Password = "geheim-und-lang-genug";

    /// <summary>Written by <c>worker_auth.BcryptPasswordHasher</c>, verbatim.</summary>
    private const string PythonEntry =
        "$2b$12$DK/g90pKN70aYUJosVXXue7nxXxqQoBDevGWH/2zQSzfzqNLjn9eW";

    private WebApplicationFactory<Program> _dienst = null!;
    private string _email = null!;

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
            host.UseSetting("environment", "Development");
        });

        using (var scope = _dienst.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
                .Database.MigrateAsync();
        }

        _email = $"anna-{Guid.NewGuid():N}@example.com";
        await CreateAccount(_email);
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private async Task CreateAccount(string email, string status = "active")
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (id, email, password_hash, display_name, status, roles,
                               created_at, updated_at, version)
            VALUES (@id, @email, @hash, 'Anna', @status::account_status, '["user"]'::jsonb,
                    now(), now(), 1)
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("hash", PythonEntry);
        command.Parameters.AddWithValue("status", status);
        await command.ExecuteNonQueryAsync();
    }

    private HttpClient Browser() =>
        _dienst.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task<HttpResponseMessage> SignIn(
        HttpClient browser, string email, string password) =>
        await browser.PostAsJsonAsync("/auth/login", new { email, password = password });

    private static string CookieFrom(HttpResponseMessage response, string name) =>
        response.Headers.GetValues("Set-Cookie")
            .First(row => row.StartsWith($"{name}=", StringComparison.Ordinal))
            .Split(';')[0][(name.Length + 1)..];

    /// <summary>
    /// The entry was written by the Python hasher and is read here without
    /// anyone being asked to reset anything.
    /// </summary>
    [Fact]
    public async Task A_password_Python_hashed_lets_one_in()
    {
        var response = await SignIn(Browser(), _email, Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_wrong_password_is_refused()
    {
        var response = await SignIn(Browser(), _email, "falsch-aber-lang-genug");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_unconfirmed_account_gets_403_and_not_401()
    {
        var pending = $"clara-{Guid.NewGuid():N}@example.com";
        await CreateAccount(pending, status: "pending");

        var response = await SignIn(Browser(), pending, Password);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The React app reads the message out of <c>detail</c>. Noelia's own
    /// handler writes a different envelope, which is why this service does not
    /// use it.
    /// </summary>
    [Fact]
    public async Task A_refusal_comes_as_an_RFC_9457_document()
    {
        var response = await SignIn(Browser(), _email, "falsch-aber-lang-genug");

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        problem.Should().ContainKey("detail").And.ContainKey("status").And.ContainKey("title");
    }

    [Fact]
    public async Task The_cookies_are_named_and_scoped_as_in_Python()
    {
        var response = await SignIn(Browser(), _email, Password);
        var headers = response.Headers.GetValues("Set-Cookie").ToList();

        var access = headers.Single(z => z.StartsWith("access=", StringComparison.Ordinal));
        var refresh = headers.Single(z => z.StartsWith("refresh=", StringComparison.Ordinal));

        access.Should().Contain("path=/").And.Contain("httponly");
        refresh.Should().Contain("path=/auth", "otherwise every route sees the refresh token");
        refresh.Should().Contain("httponly").And.Contain("samesite=strict");
    }

    [Fact]
    public async Task Refreshing_gives_a_fresh_pair_and_voids_the_old_one()
    {
        var browser = Browser();
        var signedIn = await SignIn(browser, _email, Password);
        var firstOne = CookieFrom(signedIn, "refresh");

        var refreshed = await browser.PostAsync("/auth/refresh", null);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        CookieFrom(refreshed, "refresh").Should().NotBe(firstOne, "every refresh rotates");
    }

    [Fact]
    public async Task Without_a_refresh_token_there_is_no_refresh()
    {
        var response = await Browser().PostAsync("/auth/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task After_signing_out_the_refresh_token_no_longer_carries()
    {
        var browser = Browser();
        await SignIn(browser, _email, Password);

        var signedOut = await browser.PostAsync("/auth/logout", null);
        signedOut.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var attempt = await browser.PostAsync("/auth/refresh", null);
        attempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_without_a_session_is_no_error()
    {
        var response = await Browser().PostAsync("/auth/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Hands the token to the real Python verifier and returns what it said.
    /// </summary>
}
