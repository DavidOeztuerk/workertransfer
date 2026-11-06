using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Noelia.Abstractions.Security.Checks;
using Noelia.Infrastructure.Security.ServiceTokens;
using WorkerTransfer.Identity.Infrastructure.Mail;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Tests.Shared;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// The doors of identity-service: every door names who may open it, and to
/// everyone else it looks like a door that is not there (ADR-0057).
/// </summary>
/// <remarks>
/// Real tokens from the real <c>/m2m/token</c>, through the real chain with
/// <c>UseTokenRevocation</c> and <c>UseNoeliaPrincipal</c>: in Noelia 7.1.0 that
/// chain refused every entitled caller, and only a host like this one shows it.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class InternalDoorsTests(Postgres postgres) : IAsyncLifetime
{
    private const string Password = "secret-and-long-enough";
    private const string PersonalKey = "sk-ant-personal-secret-1a2b";
    private const string CompanyKey = "sk-ant-company-secret-3c4d";

    private WebApplicationFactory<Program> _service = null!;
    private RecordingMailSender _sender = null!;

    /// <summary>Which client may open which door. The table of ADR-0057.</summary>
    private static readonly Dictionary<string, string[]> Doors = new()
    {
        ["D1"] = ["notification"],
        ["D2"] = ["advisor", "jobs", "transfer"],
        ["D3"] = ["applications", "messages", "transfer"],
        ["D4"] = ["advisor", "companies", "jobs", "messages", "resume", "transfer"],
        ["D5"] = ["advisor", "transfer"],
        ["D6"] = ["applications", "messages", "profile"],
        ["D7"] = ["jobs", "messages", "scout"],
    };

    public static TheoryData<string, string> EveryDoorAndClient()
    {
        var rows = new TheoryData<string, string>();

        foreach (var door in Doors.Keys)
        {
            foreach (var client in ServiceCredentials.Clients.Keys)
            {
                rows.Add(door, client);
            }
        }

        return rows;
    }

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
            host.UseSetting(KeyRingSecret.Variable, "test-ring-key-for-the-suite");
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

    /// <summary>The boss of Q-0043 (finding WT-1).</summary>
    /// <remarks>
    /// What used to open every door was one shared value. Neither that value
    /// nor the mail service's own token reaches a name, an address or a key.
    /// </remarks>
    [Fact]
    public async Task InternalEndpoints_NotifySecret_CannotReadIdentityOrAiKey()
    {
        var (founder, company, subject) = await Founder();
        await founder.PutAsJsonAsync("/account/settings", new
        {
            delete_after_months = (int?)null, ai_provider = "anthropic", ai_model = "", ai_audit_log = false
        });
        await founder.PutAsJsonAsync("/account/ai-key", new { key = PersonalKey });
        await founder.PutAsJsonAsync($"/companies/{company}/ai", new { ai_provider = "anthropic" });
        await founder.PutAsJsonAsync($"/companies/{company}/ai/key", new { key = CompanyKey });

        var oldHeader = _service.CreateClient();
        oldHeader.DefaultRequestHeaders.Add("X-Notify-Secret", "notify-secret");
        var mailService = await As("notification");

        foreach (var caller in new[] { oldHeader, mailService })
        {
            foreach (var path in new[]
            {
                $"/internal/account/{subject}/identity",
                $"/internal/account/{subject}/ai",
                $"/internal/companies/{company}/ai",
            })
            {
                var response = await caller.GetAsync(path);
                var body = await response.Content.ReadAsStringAsync();

                response.StatusCode.Should().Be(HttpStatusCode.NotFound, path);
                body.Should().NotContain("sk-ant").And.NotContain("@");
            }
        }

        (await (await As("advisor")).GetAsync($"/internal/account/{subject}/identity"))
            .StatusCode.Should().Be(HttpStatusCode.OK, "the counter-probe: the door opens for its caller");

        var key = await (await As("profile")).GetAsync($"/internal/account/{subject}/ai");
        (await key.Content.ReadAsStringAsync()).Should().Contain(PersonalKey);
    }

    /// <summary>Every door against every client: the table holds in both directions.</summary>
    [Theory]
    [MemberData(nameof(EveryDoorAndClient))]
    public async Task A_door_opens_for_the_clients_it_names_and_for_nobody_else(string door, string client)
    {
        var (_, company, subject) = await Founder();

        var response = await Knock(await As(client), door, company, subject);

        if (Doors[door].Contains(client))
        {
            response.IsSuccessStatusCode.Should().BeTrue($"{client} stands on the list of {door}");
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{client} is not on the list of {door}");
        }
    }

    [Fact]
    public async Task A_correctly_signed_token_of_an_unknown_service_opens_nothing()
    {
        var (_, company, subject) = await Founder();
        var stranger = Bearer(TokenShape.ServiceToken("stranger", "identity"));

        foreach (var door in Doors.Keys)
        {
            (await Knock(stranger, door, company, subject))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, door);
        }
    }

    [Fact]
    public async Task A_token_for_another_audience_opens_nothing()
    {
        var (_, company, subject) = await Founder();
        var elsewhere = Bearer(TokenShape.ServiceToken("advisor", "notification"));

        (await Knock(elsewhere, "D5", company, subject))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_persons_token_opens_no_door()
    {
        var (founder, company, subject) = await Founder();

        foreach (var door in Doors.Keys)
        {
            (await Knock(founder, door, company, subject))
                .StatusCode.Should().Be(HttpStatusCode.NotFound, door);
        }
    }

    /// <summary>
    /// A closed door, a wrong method and a path that does not exist answer with
    /// the same bytes: nothing tells them apart from the outside.
    /// </summary>
    [Fact]
    public async Task A_closed_door_a_wrong_method_and_an_unknown_path_look_the_same()
    {
        var (_, company, subject) = await Founder();
        var outsider = _service.CreateClient();
        var door = $"/internal/account/{subject}/identity";

        var reference = await Print(await outsider.GetAsync("/internal/no-such-door"));

        (await Print(await outsider.GetAsync(door))).Should().Be(reference);
        (await Print(await outsider.DeleteAsync(door))).Should().Be(reference);
        (await Print(await outsider.GetAsync("/internal/notify"))).Should().Be(reference);
        (await Print(await outsider.PostAsync("/internal/notify", null))).Should().Be(reference);
        (await Print(await outsider.PostAsync(
            "/internal/notify", new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"))))
            .Should().Be(reference);
        (await Print(await outsider.GetAsync($"/internal/companies/{company}/no-such-door"))).Should().Be(reference);
    }

    /// <summary>An entitled caller on a path that does not exist is no exception.</summary>
    [Fact]
    public async Task A_path_that_does_not_exist_stays_shut_for_an_entitled_caller()
    {
        var advisor = await As("advisor");

        (await advisor.GetAsync("/internal/no-such-door"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_wrong_secret_gets_no_token()
    {
        var response = await Fetch("advisor", ServiceCredentials.Clients["jobs"].Secret, "identity");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task There_is_no_token_for_an_audience_nobody_owns()
    {
        var response = await Fetch("advisor", ServiceCredentials.Clients["advisor"].Secret, "workertransfer");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A flood gets 429 and everything else goes on answering. The check in
    /// Noelia reads metadata only and would stay green without the middleware.
    /// </summary>
    [Fact]
    public async Task The_token_endpoint_is_braked_and_nothing_else_is()
    {
        for (var attempt = 0; attempt < 600; attempt++)
        {
            (await Fetch("advisor", "not-a-secret", "identity"))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await Fetch("advisor", "not-a-secret", "identity"))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        (await _service.CreateClient().GetAsync("/auth/session"))
            .StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// Route and table in both directions. The fallback is the one route under
    /// <c>/internal</c> that is no door.
    /// </summary>
    [Fact]
    public void Every_route_under_internal_is_a_door_of_the_table()
    {
        var routes = _service.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(route => route.RoutePattern.RawText!.StartsWith("/internal/", StringComparison.Ordinal)
                            && !route.RoutePattern.RawText.Contains("**", StringComparison.Ordinal))
            .ToList();

        routes.Should().OnlyContain(route => route.Metadata.GetMetadata<ServiceCallerAttribute>() != null);

        routes.Select(route => route.RoutePattern.RawText).Should().BeEquivalentTo(
            "/internal/notify",
            "/internal/companies/{tenantId:guid}",
            "/internal/companies/{tenantId:guid}/members",
            "/internal/companies/{tenantId:guid}/members/{subjectId:guid}/role",
            "/internal/account/{subjectId:guid}/identity",
            "/internal/account/{subjectId:guid}/ai",
            "/internal/companies/{tenantId:guid}/ai");
    }

    /// <summary>
    /// Noelia's own check over this composition: the 404 handler stands, every
    /// door has its fallback, and the token endpoint has a brake.
    /// </summary>
    /// <remarks>
    /// Run here, after the start, and not read from the report: the report holds
    /// the run from before the routes exist (<c>bugs/pruefung-der-tueren-laeuft-vor-den-routen.md</c>).
    /// </remarks>
    [Fact]
    public async Task The_self_check_of_the_doors_passes()
    {
        var results = await _service.Services.GetRequiredService<ISecurityCheckRunner>().RunAsync();

        var check = results.Single(result => result.Id == "noelia.m2m.caller-policy");

        check.Status.Should().Be(SecurityCheckStatus.Pass, check.Summary);
        check.Summary.Should().StartWith("7 service door(s)");
    }

    private async Task<HttpResponseMessage> Knock(HttpClient caller, string door, string company, string subject) =>
        door switch
        {
            "D1" => await caller.PostAsJsonAsync("/internal/notify", new { user_id = subject }),
            "D2" => await caller.GetAsync($"/internal/companies/{company}"),
            "D3" => await caller.GetAsync($"/internal/companies/{company}/members"),
            "D4" => await caller.GetAsync($"/internal/companies/{company}/members/{subject}/role"),
            "D5" => await caller.GetAsync($"/internal/account/{subject}/identity"),
            "D6" => await caller.GetAsync($"/internal/account/{subject}/ai"),
            "D7" => await caller.GetAsync($"/internal/companies/{company}/ai"),
            _ => throw new ArgumentOutOfRangeException(nameof(door), door, "not a door of this service"),
        };

    /// <summary>A caller holding a real token from the real endpoint.</summary>
    private async Task<HttpClient> As(string client)
    {
        var response = await Fetch(client, ServiceCredentials.Clients[client].Secret, "identity");
        response.StatusCode.Should().Be(HttpStatusCode.OK, "a known client fetches its token");

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        return Bearer(body.GetProperty("access_token").GetString()!);
    }

    private Task<HttpResponseMessage> Fetch(string client, string secret, string audience) =>
        _service.CreateClient().PostAsync("/m2m/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = client,
                ["client_secret"] = secret,
                ["audience"] = audience,
            }));

    private HttpClient Bearer(string token)
    {
        var client = _service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Status, headers and body — all a caller can compare.</summary>
    private static async Task<string> Print(HttpResponseMessage response)
    {
        var headers = response.Headers.Concat(response.Content.Headers)
            .Where(header => !header.Key.Equals("Date", StringComparison.OrdinalIgnoreCase)
                             && !header.Key.Contains("Correlation", StringComparison.OrdinalIgnoreCase))
            .OrderBy(header => header.Key, StringComparer.Ordinal)
            .Select(header => $"{header.Key}={string.Join(',', header.Value)}");

        return $"{(int)response.StatusCode}|{string.Join(';', headers)}|{await response.Content.ReadAsStringAsync()}";
    }

    private async Task<(HttpClient Browser, string Company, string Subject)> Founder()
    {
        var domain = $"company-{Guid.NewGuid():N}.example";
        var address = $"anna-{Guid.NewGuid():N}@{domain}";
        var browser = _service.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email = address,
            password = Password,
            display_name = "Anna",
            company_name = "Example GmbH"
        });

        var token = Regex.Match(
            _sender.Sent[^1].Text, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;
        await browser.PostAsJsonAsync("/auth/verify-email", new { token });
        await browser.PostAsJsonAsync("/auth/login", new { email = address, password = Password });

        var me = JsonDocument.Parse(await (await browser.GetAsync("/me")).Content.ReadAsStringAsync()).RootElement;
        var mine = JsonDocument.Parse(await (await browser.GetAsync("/me/companies")).Content.ReadAsStringAsync()).RootElement;

        return (browser, mine[0].GetProperty("id").GetString()!, me.GetProperty("user_id").GetString()!);
    }
}
