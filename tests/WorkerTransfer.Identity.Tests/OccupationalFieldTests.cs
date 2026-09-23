using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Identity.Infrastructure.Mail;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Tests;

/// <summary>The occupational field orders what is offered — and nothing else (ADR-0039).</summary>
[Collection(PostgresCollection.Name)]
public class OccupationalFieldTests(Postgres postgres) : IAsyncLifetime
{
    private const string Password = "secret-and-long-enough";

    private WebApplicationFactory<Program> _service = null!;
    private readonly MailRecord _mail = new();

    public async Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:identity", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:PrivateKey", TokenShape.PrivateKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Mail:WebAdresse", "http://localhost:5173");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IMailSender>(_mail)));
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

    /// <summary>The eleven labels as a SET, so a twelfth stands out.</summary>
    [Fact]
    public void The_list_is_closed_and_complete() =>
        OccupationalFieldChoice.Labels.Should().BeEquivalentTo(
        [
            "handwerk", "industrie_technik", "bau", "gesundheit_pflege",
            "logistik_verkehr", "gastronomie_hotel", "handel_verkauf",
            "buero_verwaltung", "it_software", "bildung_soziales", "sonstiges"
        ]);

    /// <summary>
    /// Every label comes back as itself — both directions are written by hand.
    /// </summary>
    [Fact]
    public void Every_label_survives_the_round_trip()
    {
        foreach (var label in OccupationalFieldChoice.Labels)
        {
            OccupationalFieldChoice.Label(OccupationalFieldChoice.From(label)).Should().Be(label);
        }
    }

    /// <summary>"Other" is a choice, "nothing" is none.</summary>
    [Fact]
    public void Other_is_not_the_same_as_nothing()
    {
        OccupationalFieldChoice.From("sonstiges").Should().Be(OccupationalField.Other);
        OccupationalFieldChoice.From(null).Should().BeNull();
        OccupationalFieldChoice.From("  ").Should().BeNull();
    }

    /// <summary>Empty is a permitted value, nonsense is not.</summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("handwerk", true)]
    [InlineData("HANDWERK", true)]
    [InlineData("industrytechnology", false)]
    [InlineData("metalworker", false)]
    public void Known_says_what_may_be_stored(string? label, bool expected) =>
        OccupationalFieldChoice.Known(label).Should().Be(expected);

    /// <summary>
    /// Whoever chooses nothing gets <c>null</c>. Nothing is guessed — a GitHub
    /// connection makes nobody <c>it_software</c> (ADR-0022 §2).
    /// </summary>
    [Fact]
    public async Task Without_a_choice_nothing_stands_there()
    {
        var browser = await SignedInAccount();

        var session = await browser.GetFromJsonAsync<SessionAnswer>("/auth/session");

        session!.User!.OccupationalField.Should().BeNull();
    }

    /// <summary>What was chosen at registration stands there afterwards.</summary>
    [Fact]
    public async Task The_choice_made_at_registration_holds()
    {
        var browser = await SignedInAccount(field: "handwerk");

        var session = await browser.GetFromJsonAsync<SessionAnswer>("/auth/session");

        session!.User!.OccupationalField.Should().Be("handwerk");
    }

    /// <summary>
    /// The choice can be made later and taken back — <c>null</c> means remove,
    /// not "unchanged".
    /// </summary>
    [Fact]
    public async Task Adding_and_taking_back_both_take_effect()
    {
        var browser = await SignedInAccount();

        var set = await browser.PutAsJsonAsync(
            "/account/occupational-field", new { occupational_field = "gesundheit_pflege" });
        set.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterwards = await browser.GetFromJsonAsync<SessionAnswer>("/auth/session");
        afterwards!.User!.OccupationalField.Should().Be("gesundheit_pflege");

        var cleared = await browser.PutAsJsonAsync(
            "/account/occupational-field", new { occupational_field = (string?)null });
        cleared.StatusCode.Should().Be(HttpStatusCode.OK);

        var back = await browser.GetFromJsonAsync<SessionAnswer>("/auth/session");
        back!.User!.OccupationalField.Should().BeNull();
    }

    /// <summary>An unknown label is refused, not silently discarded.</summary>
    [Fact]
    public async Task An_unknown_field_is_refused()
    {
        var browser = await SignedInAccount(field: "handwerk");

        var response = await browser.PutAsJsonAsync(
            "/account/occupational-field", new { occupational_field = "metalworker" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // A refused change must have done nothing.
        var session = await browser.GetFromJsonAsync<SessionAnswer>("/auth/session");
        session!.User!.OccupationalField.Should().Be("handwerk");
    }

    /// <summary>Registration refuses too, instead of discarding the entry.</summary>
    [Fact]
    public async Task A_registration_with_nonsense_is_refused()
    {
        var response = await _service.CreateClient().PostAsJsonAsync("/auth/register", new
        {
            email = NewAddress("nonsense"),
            password = Password,
            display_name = "Nonsense",
            occupational_field = "spaceflight"
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Without a sign-in nobody changes an occupational field.</summary>
    [Fact]
    public async Task Without_a_sign_in_it_does_not_work()
    {
        var response = await _service.CreateClient().PutAsJsonAsync(
            "/account/occupational-field", new { occupational_field = "bau" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string NewAddress(string subject) =>
        $"{subject}-{Guid.NewGuid():N}@company-{Guid.NewGuid():N}.example";

    /// <summary>Registers, confirms and signs in.</summary>
    private async Task<HttpClient> SignedInAccount(string? field = null)
    {
        var address = NewAddress("anna");
        var browser = _service.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });

        await browser.PostAsJsonAsync("/auth/register", new
        {
            email = address,
            password = Password,
            display_name = "Anna",
            occupational_field = field
        });

        var token = Regex.Match(
            _mail.Sent[^1].Text, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;
        await browser.PostAsJsonAsync("/auth/verify-email", new { token });
        await browser.PostAsJsonAsync("/auth/login", new
        {
            email = address, password = Password
        });

        return browser;
    }

    private sealed record SessionAnswer(AccountAnswer? User);

    private sealed record AccountAnswer(
        [property: JsonPropertyName("user_id")] string UserId,
        [property: JsonPropertyName("occupational_field")] string? OccupationalField);
}
