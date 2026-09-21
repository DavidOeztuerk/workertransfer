using System.Net;
using System.Net.Http.Json;
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

/// <summary>
/// The field names on the wire are snake_case — the compound ones too.
/// </summary>
/// <remarks>
/// <para><strong>The fault this suite prevents was already there and nobody
/// saw it.</strong> <c>RegisterBody</c> was the only body on the platform
/// without a <c>[JsonPropertyName]</c> and thereby fell back to camelCase out of
/// <c>NoeliaModule.JsonOptions</c>. The interface sends <c>display_name</c>,
/// what was bound was <c>displayName</c> — the value never arrived, the column
/// is <c>NOT NULL</c>, and through the interface <strong>nobody could
/// register</strong>.</para>
///
/// <para><strong>Why 191 green tests did not notice.</strong> They sent
/// camelCase themselves. A suite written against the server instead of against
/// the contract confirms every dialect the server happens to speak — including
/// one nobody else speaks. That is why the name stands here literally in the
/// JSON and not as a C# property: what is checked here is the wire, and the wire
/// knows no renaming.</para>
///
/// <para><strong>Why snake_case and not camelCase.</strong> Not taste but a
/// deliberate majority: nine services set their contract names expressly with
/// <c>[JsonPropertyName]</c> in snake_case (<c>job_id</c>,
/// <c>shares_resume</c>, <c>created_at</c> …), and the interface reads and
/// writes accordingly. Identity was the exception, not the rule.</para>
///
/// <para><strong>Why it surfaced here of all places and nowhere else.</strong>
/// Every other body in identity has nothing but single-word fields —
/// <c>token</c>, <c>email</c>, <c>name</c>, <c>role</c>. There camelCase and
/// snake_case are the same word. Only a compound name makes the difference
/// visible, and <c>display_name</c> is the only one on a mandatory
/// field.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class WireNameTests(Postgres postgres) : IAsyncLifetime
{

    private WebApplicationFactory<Program> _dienst = null!;

    /// <summary>The sending goes into a list, not into a real mailbox.</summary>
    /// <remarks>
    /// Without a stand-in the real <c>SmtpMailSender</c> runs against the default
    /// <c>localhost:1025</c> — that is, against the developer's Mailpit. A test
    /// suite must write nothing into an environment a human being looks at.
    /// </remarks>
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

    private HttpClient Browser() => _dienst.CreateClient();

    private static string NewAddress() =>
        $"draht-{Guid.NewGuid():N}@firma-{Guid.NewGuid():N}.example";

    /// <summary>
    /// Exactly the JSON <c>apps/web/src/auth/client.ts</c> sends is accepted.
    /// </summary>
    /// <remarks>
    /// As raw text and not as an object: an anonymous C# object would run
    /// through the same naming policy as the server and therefore could not
    /// contradict it at all. That is exactly what it slipped past.
    /// </remarks>
    [Fact]
    public async Task The_body_the_interface_sends_is_accepted()
    {
        using var content = new StringContent(
            $$"""
            {"email":"{{NewAddress()}}","password":"geheim-und-lang-genug",
             "display_name":"Anna"}
            """,
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await Browser().PostAsync("/auth/register", content);

        response.StatusCode.Should().Be(
            HttpStatusCode.Created,
            "the interface sends display_name, and this platform's wire "
            + "is snake_case");
    }

    /// <summary>The company name too, or founding breaks just as silently.</summary>
    [Fact]
    public async Task The_company_name_arrives_too()
    {
        using var content = new StringContent(
            $$"""
            {"email":"{{NewAddress()}}","password":"geheim-und-lang-genug",
             "display_name":"Bea","company_name":"Beispiel GmbH"}
            """,
            System.Text.Encoding.UTF8,
            "application/json");

        (await Browser().PostAsync("/auth/register", content))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>The given and family name too, or camelCase binds nothing.</summary>
    [Fact]
    public async Task The_civil_name_arrives_in_snake_case_too()
    {
        using var content = new StringContent(
            $$"""
            {"email":"{{NewAddress()}}","password":"geheim-und-lang-genug",
             "display_name":"Anna","given_name":"Anna","family_name":"Beispiel"}
            """,
            System.Text.Encoding.UTF8,
            "application/json");

        (await Browser().PostAsync("/auth/register", content))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// camelCase is NOT accepted — otherwise the service would take both and the
    /// contract would be no contract again.
    /// </summary>
    /// <remarks>
    /// The counter-probe to the first test. Without it that one would stay green
    /// if somebody set the naming policy to „either" — and then the platform
    /// would have two dialects again, one of which disappears sooner or later.
    /// </remarks>
    [Fact]
    public async Task camelCase_is_refused()
    {
        using var content = new StringContent(
            $$"""
            {"email":"{{NewAddress()}}","password":"geheim-und-lang-genug",
             "displayName":"Cara"}
            """,
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await Browser().PostAsync("/auth/register", content);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();

        body!["detail"].ToString().Should().Contain(
            "display_name",
            "the message names the name the caller should have sent");
    }
}
