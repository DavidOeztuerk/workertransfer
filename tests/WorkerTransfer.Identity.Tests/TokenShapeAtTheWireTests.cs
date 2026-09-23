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
using WorkerTransfer.Identity.Infrastructure.Mail;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// The claim set as it leaves the ENDPOINT — not as the issuer builds it.
/// </summary>
/// <remarks>
/// <strong>The difference is exactly the gap the ninth claim slipped
/// through.</strong> <see cref="TokenShapeTests"/> calls Noelia's issuer
/// directly and pins its set. That is right and not enough: between the issuer
/// and the wire sit the service's settings, its wiring and the cookie the token
/// finally stands in. A claim added only by that layer would be invisible there
/// — and was: the promise in `CLAUDE.md` read "eight and nothing else",
/// measured against the running stack there were nine.
///
/// <para>
/// So this suite goes the whole way: register, confirm, sign in, switch to a
/// company — and reads the token where the browser gets it, namely out of the
/// <c>Set-Cookie</c>. It is never in the body.
/// </para>
///
/// <para>
/// What is compared is the SET, not single names. A test that lists forbidden
/// names never notices the tenth nobody thought of.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class TokenShapeAtTheWireTests(Postgres postgres) : IAsyncLifetime
{
    private const string Password = "secret-and-long-enough";

    /// <summary>What a person carries who acts for nobody.</summary>
    /// <remarks>
    /// The ninth — the long WS spelling of <c>sub</c> — is Noelia's and stays:
    /// <c>MapInboundClaims = false</c> switches the derivation off, and
    /// seventeen readers fetch the caller over this name, two of them in foreign
    /// packages. Dropping it saves seventy bytes and turns every one of those
    /// readers into a silent <c>null</c>.
    /// </remarks>
    private static readonly string[] AsPerson =
    [
        "sub",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier",
        "email",
        "jti",
        "iat",
        "exp",
        "iss",
        "aud",
        "session_id"
    ];

    private WebApplicationFactory<Program> _service = null!;
    private RecordingMailSender _sender = null!;

    public async Task InitializeAsync()
    {
        _sender = new RecordingMailSender(postgres.ConnectionString);

        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:identity", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:PrivateKey", TokenShape.PrivateKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IMailSender>(_sender)));
        });

        using var scope = _service.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>The whole set, in both capacities (ADR-0017).</summary>
    [Fact]
    public async Task At_the_wire_the_token_carries_exactly_these_claims()
    {
        var domain = $"company-{Guid.NewGuid():N}.example";
        var email = $"anna-{Guid.NewGuid():N}@{domain}";

        var browser = _service.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });

        (await browser.PostAsJsonAsync("/auth/register", new
        {
            email,
            password = Password,
            display_name = "Anna",
            company_name = "Example GmbH"
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        (await browser.PostAsJsonAsync("/auth/verify-email", new
        {
            token = Regex.Match(
                _sender.Sent.Last(mail => mail.Subject.Contains("bestätige", StringComparison.Ordinal)).Text,
                @"token=(?<token>[A-Za-z0-9_-]+)", RegexOptions.None, TimeSpan.FromSeconds(5))
                .Groups["token"].Value
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var signedIn = await browser.PostAsJsonAsync(
            "/auth/login", new { email, password = Password });
        signedIn.StatusCode.Should().Be(HttpStatusCode.OK);

        // AS A PERSON: no tenant. `null` means "acts for themselves" and not
        // "missing" — which is why the claim is not there at all.
        ClaimNames(signedIn).Should().BeEquivalentTo(AsPerson);

        var mine = JsonDocument.Parse(
            await (await browser.GetAsync("/me/companies")).Content.ReadAsStringAsync())
            .RootElement;

        var switched = await browser.PostAsync(
            $"/auth/company/{mine[0].GetProperty("id").GetString()}", null);
        switched.StatusCode.Should().Be(HttpStatusCode.OK);

        // FOR A COMPANY: exactly ONE is added. Not `tenant_id`, not `roles` —
        // the membership decides per request (ADR-0018).
        ClaimNames(switched).Should().BeEquivalentTo([.. AsPerson, "tenant"]);
    }

    /// <summary>The token is never in the BODY.</summary>
    /// <remarks>
    /// It goes out solely as an <c>httpOnly</c> cookie, so no script in the
    /// browser can read it. A field beside it in the body would be a second way
    /// to the same string — and the first one somebody writes into a log by
    /// accident.
    /// </remarks>
    [Fact]
    public async Task The_token_is_not_in_the_body()
    {
        var email = $"bo-{Guid.NewGuid():N}@company-{Guid.NewGuid():N}.example";

        var browser = _service.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email,
            password = Password,
            display_name = "Bo"
        });

        await browser.PostAsJsonAsync("/auth/verify-email", new
        {
            token = Regex.Match(
                _sender.Sent.Last(mail => mail.Subject.Contains("bestätige", StringComparison.Ordinal)).Text,
                @"token=(?<token>[A-Za-z0-9_-]+)", RegexOptions.None, TimeSpan.FromSeconds(5))
                .Groups["token"].Value
        });

        var signedIn = await browser.PostAsJsonAsync(
            "/auth/login", new { email, password = Password });

        var body = await signedIn.Content.ReadAsStringAsync();

        body.Should().NotContain("access_token");
        body.Should().NotContain(AccessCookie(signedIn));
    }

    /// <summary>The claim names out of the answer's <c>access</c> cookie.</summary>
    private static IEnumerable<string> ClaimNames(HttpResponseMessage response)
    {
        var body = AccessCookie(response).Split('.')[1];

        return JsonDocument
            .Parse(Convert.FromBase64String(
                body.Replace('-', '+').Replace('_', '/')
                    .PadRight(body.Length + ((4 - (body.Length % 4)) % 4), '=')))
            .RootElement.EnumerateObject().Select(field => field.Name);
    }

    /// <summary>The token, read the way a browser receives it.</summary>
    private static string AccessCookie(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue(
            "the token leaves the service solely as a cookie");

        var access = cookies!.FirstOrDefault(k =>
            k.StartsWith("access=", StringComparison.Ordinal));

        access.Should().NotBeNull("without `access` there would be no session");

        return access!["access=".Length..].Split(';')[0];
    }
}
