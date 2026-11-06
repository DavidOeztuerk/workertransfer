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
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Tests.Shared;

namespace WorkerTransfer.Profile.Tests;

/// <summary>
/// The two doors of profile-service open for scout-service alone (ADR-0057).
/// </summary>
[Collection(PostgresCollection.Name)]
public class InternalDoorTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _service = null!;

    public static TheoryData<string> EveryClient() => new(ServiceCredentials.Clients.Keys);

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:profile", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(KeyRingSecret.Variable, "test-ring-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Secret", "erasure-secret");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => new ProbeGate())));
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

    private async Task<Guid> SomeProfile()
    {
        var subject = Guid.CreateVersion7();

        var response = await With(TokenShape.Person(subject)).PutAsJsonAsync(
            new Uri("/profiles/me", UriKind.Relative),
            new { headline = "Developer", bio = "", location = "Berlin", remote_ok = true, skills = Array.Empty<string>() });

        response.IsSuccessStatusCode.Should().BeTrue();

        return subject;
    }

    /// <summary>The search answers an entitled caller with a page.</summary>
    [Theory]
    [MemberData(nameof(EveryClient))]
    public async Task The_search_opens_for_scout_and_for_nobody_else(string client)
    {
        var response = await With(TokenShape.Service(client, ServiceAudiences.Profile))
            .GetAsync(new Uri("/internal/profiles/search", UriKind.Relative));

        response.StatusCode.Should().Be(
            client == ServiceClients.Scout ? HttpStatusCode.OK : HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The profile has to exist: for an absent one scout gets a 404 too, and a door
    /// that answers 404 to everybody would pass whoever stands on its list.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryClient))]
    public async Task The_single_profile_opens_for_scout_and_for_nobody_else(string client)
    {
        var subject = await SomeProfile();

        var response = await With(TokenShape.Service(client, ServiceAudiences.Profile))
            .GetAsync(new Uri($"/internal/profiles/{subject}", UriKind.Relative));

        response.StatusCode.Should().Be(
            client == ServiceClients.Scout ? HttpStatusCode.OK : HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_correctly_signed_token_of_an_unknown_service_opens_nothing()
    {
        var subject = await SomeProfile();
        var stranger = With(TokenShape.Service("stranger", ServiceAudiences.Profile));

        (await stranger.GetAsync(new Uri("/internal/profiles/search", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.GetAsync(new Uri($"/internal/profiles/{subject}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_token_for_another_audience_opens_nothing() =>
        (await With(TokenShape.Service("scout", ServiceAudiences.Identity))
            .GetAsync(new Uri("/internal/profiles/search", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

    [Fact]
    public async Task A_persons_token_opens_nothing() =>
        (await With(TokenShape.Person(Guid.CreateVersion7()))
            .GetAsync(new Uri("/internal/profiles/search", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

    /// <summary>A closed door, a wrong method and an unknown path answer the same.</summary>
    [Fact]
    public async Task A_closed_door_a_wrong_method_and_an_unknown_path_look_the_same()
    {
        var outsider = _service.CreateClient();
        var search = new Uri("/internal/profiles/search", UriKind.Relative);

        var reference = await Print(await outsider.GetAsync(new Uri("/internal/no-such-door", UriKind.Relative)));

        (await Print(await outsider.GetAsync(search))).Should().Be(reference);
        (await Print(await outsider.DeleteAsync(search))).Should().Be(reference);
        (await Print(await outsider.GetAsync(new Uri($"/internal/profiles/{Guid.NewGuid()}", UriKind.Relative))))
            .Should().Be(reference);
    }

    /// <summary>Noelia's own check, run after the start: the report holds the run from before the routes.</summary>
    [Fact]
    public async Task The_self_check_of_the_doors_passes()
    {
        var results = await _service.Services.GetRequiredService<ISecurityCheckRunner>().RunAsync();

        var check = results.Single(result => result.Id == "noelia.m2m.caller-policy");

        check.Status.Should().Be(SecurityCheckStatus.Pass, check.Summary);
        check.Summary.Should().StartWith("2 service door(s)");
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
        routes.Select(route => route.RoutePattern.RawText).Should().BeEquivalentTo(
            "/internal/profiles/search", "/internal/profiles/{subjectId:guid}");
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
