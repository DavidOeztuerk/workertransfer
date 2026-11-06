using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Noelia.Abstractions.Security.Checks;
using Noelia.Infrastructure.Security.ServiceTokens;
using WorkerTransfer.Notification.Application.Ports;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Tests.Shared;

namespace WorkerTransfer.Notification.Tests;

/// <summary>
/// <c>POST /internal/notifications</c> names its seven senders (ADR-0057): the
/// decision of 05.11.2026 that this door gets a list and not a second shared
/// secret, which would let any sender pose as any other.
/// </summary>
[Collection(PostgresCollection.Name)]
public class InternalDoorTests(Postgres postgres) : IAsyncLifetime
{
    private static readonly string[] Senders =
        ["advisor", "applications", "assessment", "messages", "resume", "scout", "transfer"];

    private WebApplicationFactory<Program> _service = null!;

    public static TheoryData<string> EveryClient() => new(ServiceCredentials.Clients.Keys);

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:notification", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(KeyRingSecret.Variable, "test-ring-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Identity:BaseUrl", "http://identity.test");
            host.UseSetting("Erasure:Secret", "erasure-secret");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<IMailer>(_ => new ProbeMailer())));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient With(string token)
    {
        var client = _service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> Send(HttpClient client) =>
        client.PostAsJsonAsync(
            new Uri("/internal/notifications", UriKind.Relative),
            new { user_id = Guid.CreateVersion7(), kind = "market_request" });

    [Theory]
    [MemberData(nameof(EveryClient))]
    public async Task The_door_opens_for_its_senders_and_for_nobody_else(string client)
    {
        var response = await Send(With(TokenShape.Service(client, ServiceAudiences.Notification)));

        response.StatusCode.Should().Be(
            Senders.Contains(client) ? HttpStatusCode.Accepted : HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_correctly_signed_token_of_an_unknown_service_opens_nothing() =>
        (await Send(With(TokenShape.Service("stranger", ServiceAudiences.Notification))))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

    [Fact]
    public async Task A_token_for_another_audience_opens_nothing() =>
        (await Send(With(TokenShape.Service("transfer", ServiceAudiences.Identity))))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

    [Fact]
    public async Task A_persons_token_opens_nothing() =>
        (await Send(With(TokenShape.Person(Guid.CreateVersion7()))))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

    /// <summary>A closed door, a wrong method and an unknown path answer the same.</summary>
    [Fact]
    public async Task A_closed_door_a_wrong_method_and_an_unknown_path_look_the_same()
    {
        var outsider = _service.CreateClient();
        var door = new Uri("/internal/notifications", UriKind.Relative);

        var reference = await Print(await outsider.GetAsync(new Uri("/internal/no-such-door", UriKind.Relative)));

        (await Print(await Send(outsider))).Should().Be(reference);
        (await Print(await outsider.GetAsync(door))).Should().Be(reference);
        (await Print(await outsider.DeleteAsync(door))).Should().Be(reference);
    }

    /// <summary>Noelia's own check, run after the start: the report holds the run from before the routes.</summary>
    [Fact]
    public async Task The_self_check_of_the_door_passes()
    {
        var results = await _service.Services.GetRequiredService<ISecurityCheckRunner>().RunAsync();

        var check = results.Single(result => result.Id == "noelia.m2m.caller-policy");

        check.Status.Should().Be(SecurityCheckStatus.Pass, check.Summary);
        check.Summary.Should().StartWith("1 service door(s)");
    }

    /// <summary>The fallback is the one route under <c>/internal</c> that is no door.</summary>
    [Fact]
    public void Every_route_under_internal_is_a_door()
    {
        var routes = _service.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(route => route.RoutePattern.RawText!.StartsWith("/internal/", StringComparison.Ordinal)
                            && !route.RoutePattern.RawText.Contains("**", StringComparison.Ordinal))
            .ToList();

        routes.Should().OnlyContain(route => route.Metadata.GetMetadata<ServiceCallerAttribute>() != null);
        routes.Select(route => route.RoutePattern.RawText).Should().BeEquivalentTo("/internal/notifications");
    }

    private static async Task<string> Print(HttpResponseMessage response)
    {
        var headers = response.Headers.Concat(response.Content.Headers)
            .Where(header => !header.Key.Equals("Date", StringComparison.OrdinalIgnoreCase)
                             && !header.Key.Contains("Correlation", StringComparison.OrdinalIgnoreCase))
            .OrderBy(header => header.Key, StringComparer.Ordinal)
            .Select(header => $"{header.Key}={string.Join(',', header.Value)}");

        return $"{(int)response.StatusCode}|{string.Join(';', headers)}|{await response.Content.ReadAsStringAsync()}";
    }
}
