using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Notification.Application.Ports;
using WorkerTransfer.Notification.Domain.Notifications;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Notification.Tests;

/// <summary>
/// "Your profile was discovered" — at most one per person per day.
/// </summary>
/// <remarks>
/// <para>The disclosure from ADR-0033, and the only kind with a cap of its
/// <em>own</em>. With every other one the inbox entry always comes into being
/// and only the mail is throttled: the inbox lies behind the sign-in and gives
/// nobody anything away.</para>
///
/// <para>Here that is different, and in it lies the whole reason for this
/// suite: one entry per hit would be a <strong>counter over one's own
/// visibility</strong> — by a detour exactly the register "who looked at whom"
/// that must not come into being. So the cap takes the entry with it, not only
/// the mail.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class DiscoveryTests(Postgres postgres) : IAsyncLifetime
{
    private const string Meldegeheimnis = "melde-geheimnis";

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly ProbeMailer _postbote = new();
    private readonly ProbeClock _uhr = new(new DateTimeOffset(2026, 9, 11, 9, 0, 0, TimeSpan.Zero));

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:notification", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Identity:Adresse", "http://identity.test");
            host.UseSetting("Identity:Geheimnis", Meldegeheimnis);
            host.UseSetting("Erasure:Geheimnis", "loesch-geheimnis");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IMailer>(_ => _postbote));
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_uhr));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>The kind exists, and it arrives.</summary>
    [Fact]
    public async Task The_kind_exists()
    {
        var anna = Guid.CreateVersion7();

        (await Melde(anna)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var inbox = await Inbox(anna);

        inbox.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("kind").GetString().Should().Be("profile_discovered");
    }

    /// <summary>Ten hits in one day yield ONE entry.</summary>
    /// <remarks>
    /// Not ten, and not "one entry but only one mail" either: ten entries would
    /// be ten lines about how often this person was seen. That is exactly the
    /// counter ADR-0033 does not let come into being.
    /// </remarks>
    [Fact]
    public async Task Ten_hits_in_one_day_yield_one_entry()
    {
        var anna = Guid.CreateVersion7();

        for (var lauf = 0; lauf < 10; lauf++)
        {
            await Melde(anna);
            _uhr.Weiter(TimeSpan.FromMinutes(30));
        }

        (await Inbox(anna)).GetArrayLength().Should().Be(1);
        _postbote.Gebeten.Should().Equal(anna);
    }

    /// <summary>The next day again — otherwise it would be no disclosure any more.</summary>
    [Fact]
    public async Task The_next_day_again()
    {
        var anna = Guid.CreateVersion7();

        await Melde(anna);
        _uhr.Weiter(TimeSpan.FromDays(1));
        await Melde(anna);

        (await Inbox(anna)).GetArrayLength().Should().Be(2);
    }

    /// <summary>Just before the day, not yet.</summary>
    /// <remarks>
    /// The counter-probe to the line above: without it the test would be green
    /// too if the cap did not bite at all and every notification went through.
    /// </remarks>
    [Fact]
    public async Task Just_before_the_day_not_yet()
    {
        var anna = Guid.CreateVersion7();

        await Melde(anna);
        _uhr.Weiter(TimeSpan.FromDays(1) - TimeSpan.FromMinutes(1));
        await Melde(anna);

        (await Inbox(anna)).GetArrayLength().Should().Be(1);
    }

    /// <summary>The cap applies per kind, not across all kinds.</summary>
    /// <remarks>
    /// A market request on the same day has to arrive. Were the cap global, a
    /// discovery in the morning would swallow the CV request in the afternoon —
    /// and that is something somebody is meant to answer.
    /// </remarks>
    [Fact]
    public async Task The_cap_applies_per_kind()
    {
        var anna = Guid.CreateVersion7();

        await Melde(anna);
        await Melde(anna, "market_request");
        await Melde(anna);

        var kinds = (await Inbox(anna)).EnumerateArray()
            .Select(entry => entry.GetProperty("kind").GetString())
            .ToArray();

        kinds.Should().BeEquivalentTo(["profile_discovered", "market_request"]);
    }

    /// <summary>The cap applies per person, not to everybody together.</summary>
    [Fact]
    public async Task The_cap_applies_per_person()
    {
        var anna = Guid.CreateVersion7();
        var bea = Guid.CreateVersion7();

        await Melde(anna);
        await Melde(bea);

        (await Inbox(anna)).GetArrayLength().Should().Be(1);
        (await Inbox(bea)).GetArrayLength().Should().Be(1);
    }

    /// <summary>It can be switched off on its own — and switching off works.</summary>
    /// <remarks>
    /// ADR-0033 demands a kind of its <em>own</em> beside the others, precisely
    /// so that it can be switched off singly: it is the only disclosure a
    /// person gets about their own visibility, and whoever does not want it
    /// should not have to switch everything else off with it.
    /// </remarks>
    [Fact]
    public async Task It_can_be_switched_off_on_its_own()
    {
        var anna = Guid.CreateVersion7();

        var vorher = await Json(
            await AlsPerson(anna).GetAsync(new Uri("/me/notification-preferences", UriKind.Relative)));

        vorher.GetProperty("profile_discovered").GetBoolean().Should().BeTrue(
            "eine neue Art darf nicht stillschweigend ausbleiben");

        var abbestellt = await AlsPerson(anna).PutAsJsonAsync(
            new Uri("/me/notification-preferences", UriKind.Relative),
            new
            {
                resume_request = true,
                market_request = true,
                application_update = true,
                transfer_update = true,
                application_received = true,
                profile_discovered = false
            });

        abbestellt.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(abbestellt)).GetProperty("profile_discovered").GetBoolean()
            .Should().BeFalse();

        await Melde(anna);

        _postbote.Gebeten.Should().BeEmpty("abbestellt heisst abbestellt");

        // The ENTRY comes into being all the same: it lies behind the sign-in,
        // and leaving it out would mean concealing from somebody that something
        // happened. What is switched off is the MAIL.
        (await Inbox(anna)).GetArrayLength().Should().Be(1);
    }

    /// <summary>The kind is one of eight, and all eight are settable.</summary>
    /// <remarks>
    /// The eighth came with assessment-service (ADR-0042):
    /// <c>assessment_update</c>. This number stands here so that a new kind does
    /// not arrive silently — every one is a switch a human being gets to see,
    /// and a kind added unnoticed would be a mail nobody asked for.
    /// </remarks>
    [Fact]
    public void There_are_eight_kinds() =>
        NotificationKinds.All.Should().HaveCount(8)
            .And.Contain(NotificationKind.ProfileDiscovered)
            .And.Contain(NotificationKind.AssessmentUpdate);

    /// <summary>Only this one kind carries a daily cap.</summary>
    /// <remarks>
    /// The counter-probe to the cap itself: whoever put it on a further kind by
    /// accident would take the inbox entry with it there — and an application
    /// that moves twice in a day would half disappear.
    /// </remarks>
    [Fact]
    public void Only_discovery_carries_a_daily_cap()
    {
        NotificationKinds.DailyCap(NotificationKind.ProfileDiscovered)
            .Should().Be(TimeSpan.FromDays(1));

        foreach (var kind in NotificationKinds.All
                     .Where(kind => kind != NotificationKind.ProfileDiscovered))
        {
            NotificationKinds.DailyCap(kind).Should().BeNull(
                $"{NotificationKinds.Word(kind)} darf so oft kommen, wie es passiert");
        }
    }

    // ---------------------------------------------------------------------

    private HttpClient AlsPerson(Guid subject)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenShape.Person(subject));
        return browser;
    }

    private Task<HttpResponseMessage> Melde(Guid subject, string kind = "profile_discovered")
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Notify-Secret", Meldegeheimnis);

        return browser.PostAsJsonAsync(
            new Uri("/internal/notifications", UriKind.Relative), new { user_id = subject, kind = kind });
    }

    private async Task<JsonElement> Inbox(Guid subject) =>
        await Json(await AlsPerson(subject).GetAsync(new Uri("/notifications/me", UriKind.Relative)));

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
