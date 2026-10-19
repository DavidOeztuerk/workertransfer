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
using WorkerTransfer.Notification.Application.Ports;
using WorkerTransfer.Notification.Domain.Notifications;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Notification.Tests;

/// <summary>Notify, read, unsubscribe — and what never goes out.</summary>
[Collection(PostgresCollection.Name)]
public class NotificationJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private const string NotifySecret = "notify-secret";

    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeMailer _mailer = new();
    private readonly ProbeClock _clock = new(new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero));

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:notification", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Identity:BaseUrl", "http://identity.test");
            host.UseSetting("Identity:Secret", NotifySecret);
            host.UseSetting("Erasure:Secret", "erasure-secret");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IMailer>(_ => _mailer));
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AsPerson(Guid subject)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenShape.Person(subject));
        return browser;
    }

    private Task<HttpResponseMessage> Notify(
        Guid subject, string kind = "market_request", string secret = NotifySecret)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Notify-Secret", secret);
        return browser.PostAsJsonAsync("/internal/notifications", new { user_id = subject, kind = kind });
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>The ordinary path: notified, in the inbox, mail triggered.</summary>
    [Fact]
    public async Task A_notification_lands_in_the_inbox_and_triggers_mail()
    {
        var anna = Guid.CreateVersion7();

        var notified = await Notify(anna);

        notified.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _mailer.Requested.Should().Equal(anna);

        var inbox = await Json(await AsPerson(anna).GetAsync("/notifications/me"));

        inbox.GetArrayLength().Should().Be(1);

        var entry = inbox.EnumerateArray().Single();
        entry.GetProperty("kind").GetString().Should().Be("market_request");
        entry.GetProperty("read_at").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// The entry always comes into being, the mail is throttled.
    /// </summary>
    /// <remarks>
    /// The inbox lies behind the sign-in, where who reads is checked; throttling
    /// it would mean concealing from somebody that something happened. The mail
    /// lands in an inbox that need not belong to the person alone.
    /// </remarks>
    [Fact]
    public async Task The_throttle_holds_back_the_mail_not_the_inbox()
    {
        var anna = Guid.CreateVersion7();

        await Notify(anna, "market_request");
        await Notify(anna, "resume_request");
        await Notify(anna, "transfer_update");

        _mailer.Requested.Should().Equal(anna);

        var inbox = await Json(await AsPerson(anna).GetAsync("/notifications/me"));

        inbox.GetArrayLength().Should().Be(3);
    }

    /// <summary>After the throttle hour mail goes out again.</summary>
    [Fact]
    public async Task After_an_hour_mail_goes_out_again()
    {
        var anna = Guid.CreateVersion7();

        await Notify(anna);
        _clock.Advance(NotificationPreference.Throttle);
        await Notify(anna);

        _mailer.Requested.Should().Equal(anna, anna);
    }

    /// <summary>Shortly before, not yet.</summary>
    [Fact]
    public async Task Just_before_the_hour_not_yet()
    {
        var anna = Guid.CreateVersion7();

        await Notify(anna);
        _clock.Advance(NotificationPreference.Throttle - TimeSpan.FromMinutes(1));
        await Notify(anna);

        _mailer.Requested.Should().Equal(anna);
    }

    /// <summary>The throttle applies per person, not to everybody together.</summary>
    [Fact]
    public async Task The_throttle_applies_per_person()
    {
        var anna = Guid.CreateVersion7();
        var bea = Guid.CreateVersion7();

        await Notify(anna);
        await Notify(bea);

        _mailer.Requested.Should().Equal(anna, bea);
    }

    /// <summary>Whoever has set nothing has everything on.</summary>
    /// <remarks>
    /// A notification about one's <em>own</em> case is not advertising but the
    /// condition for "the person decides" to be able to happen at all.
    /// </remarks>
    [Fact]
    public async Task By_default_everything_is_on()
    {
        var preferences = await Json(
            await AsPerson(Guid.CreateVersion7()).GetAsync("/me/notification-preferences"));

        foreach (var field in new[]
                 {
                     "resume_request", "market_request", "application_update", "transfer_update",
                     "application_received"
                 })
        {
            preferences.GetProperty(field).GetBoolean().Should().BeTrue(field);
        }
    }

    /// <summary>Switched off means no mail — the entry stays all the same.</summary>
    [Fact]
    public async Task Switched_off_means_no_mail_but_the_entry_stays()
    {
        var anna = Guid.CreateVersion7();
        var hers = AsPerson(anna);

        await hers.PutAsJsonAsync("/me/notification-preferences", new
        {
            resume_request = true,
            market_request = false,
            application_update = true,
            transfer_update = true
        });

        await Notify(anna, "market_request");

        _mailer.Requested.Should().BeEmpty();

        var inbox = await Json(await hers.GetAsync("/notifications/me"));

        inbox.GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// A switched-off kind does not block the throttle: the next allowed one
    /// goes out.
    /// </summary>
    [Fact]
    public async Task A_switched_off_kind_does_not_spend_the_throttle()
    {
        var anna = Guid.CreateVersion7();

        await AsPerson(anna).PutAsJsonAsync("/me/notification-preferences", new
        {
            resume_request = true,
            market_request = false,
            application_update = true,
            transfer_update = true
        });

        await Notify(anna, "market_request");
        await Notify(anna, "resume_request");

        _mailer.Requested.Should().Equal(anna);
    }

    /// <summary>The settings come back as they were written.</summary>
    [Fact]
    public async Task The_settings_come_back_as_written()
    {
        var hers = AsPerson(Guid.CreateVersion7());

        var written = await hers.PutAsJsonAsync("/me/notification-preferences", new
        {
            resume_request = false,
            market_request = false,
            application_update = true,
            transfer_update = false
        });

        written.StatusCode.Should().Be(HttpStatusCode.OK);

        var read = await Json(await hers.GetAsync("/me/notification-preferences"));

        read.GetProperty("resume_request").GetBoolean().Should().BeFalse();
        read.GetProperty("application_update").GetBoolean().Should().BeTrue();
        read.GetProperty("application_received").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Whoever unsubscribed from the kind gets no mail — the entry in the
    /// inbox stays all the same.
    /// </summary>
    [Fact]
    public async Task ApplicationReceived_switched_off_means_no_mail()
    {
        var anna = Guid.CreateVersion7();
        var hers = AsPerson(anna);

        await hers.PutAsJsonAsync("/me/notification-preferences", new
        {
            resume_request = true,
            market_request = true,
            application_update = true,
            transfer_update = true,
            application_received = false
        });

        await Notify(anna, "application_received");

        _mailer.Requested.Should().BeEmpty();

        var inbox = await Json(await hers.GetAsync("/notifications/me"));
        inbox.GetArrayLength().Should().Be(1);
        inbox[0].GetProperty("kind").GetString().Should().Be("application_received");
    }

    /// <summary>Read is read — and a second time changes nothing.</summary>
    [Fact]
    public async Task Read_all_sets_the_moment_once()
    {
        var anna = Guid.CreateVersion7();
        var hers = AsPerson(anna);
        await Notify(anna, "market_request");
        await Notify(anna, "resume_request");

        var read = await Json(await hers.PostAsync("/notifications/me/read", null));

        read.GetProperty("read").GetInt32().Should().Be(2);

        var inbox = await Json(await hers.GetAsync("/notifications/me"));

        inbox.EnumerateArray().Should().OnlyContain(
            entry => entry.GetProperty("read_at").ValueKind != JsonValueKind.Null);

        var again = await Json(await hers.PostAsync("/notifications/me/read", null));

        again.GetProperty("read").GetInt32().Should().Be(0);
    }

    /// <summary>The inbox shows only one's own entries.</summary>
    [Fact]
    public async Task The_inbox_shows_only_ones_own_entries()
    {
        var anna = Guid.CreateVersion7();
        var bea = Guid.CreateVersion7();
        await Notify(anna);
        await Notify(bea);

        var ownInbox = await Json(await AsPerson(anna).GetAsync("/notifications/me"));

        ownInbox.GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// Without the secret nothing happens — and the answer is 404, not 401.
    /// </summary>
    /// <remarks>
    /// A 401 confirms that the endpoint exists.
    /// </remarks>
    [Fact]
    public async Task Without_the_right_secret_it_is_404()
    {
        var anna = Guid.CreateVersion7();

        var response = await Notify(anna, secret: "guessed");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _mailer.Requested.Should().BeEmpty();

        await using var context = postgres.Context();
        (await context.InboxEntries.AnyAsync(row => row.UserId == anna)).Should().BeFalse();
    }

    /// <summary>
    /// Always 202 — even for somebody who never existed here.
    /// </summary>
    /// <remarks>
    /// Otherwise the endpoint would be an oracle about whether this person
    /// exists. This service does not know anyway: it knows no accounts, only
    /// ids.
    /// </remarks>
    [Fact]
    public async Task Even_for_an_unknown_id_it_is_202()
    {
        var response = await Notify(Guid.CreateVersion7());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    /// <summary>
    /// Unsubscribed and throttled answer like delivered: 202 and an empty
    /// body.
    /// </summary>
    [Fact]
    public async Task Switched_off_and_delivered_answer_the_same()
    {
        var anna = Guid.CreateVersion7();
        var bea = Guid.CreateVersion7();

        await AsPerson(bea).PutAsJsonAsync("/me/notification-preferences", new
        {
            resume_request = false,
            market_request = false,
            application_update = false,
            transfer_update = false
        });

        var delivered = await Notify(anna);
        var unsubscribed = await Notify(bea);

        delivered.StatusCode.Should().Be(unsubscribed.StatusCode);
        (await delivered.Content.ReadAsStringAsync())
            .Should().Be(await unsubscribed.Content.ReadAsStringAsync());
    }

    /// <summary>A kind that does not exist is the caller's fault.</summary>
    [Fact]
    public async Task An_invented_kind_is_422()
    {
        var response = await Notify(Guid.CreateVersion7(), "anything");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Without a sign-in there is no inbox.</summary>
    [Fact]
    public async Task Without_a_token_it_is_401()
    {
        (await _service.CreateClient().GetAsync("/notifications/me"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _service.CreateClient().GetAsync("/me/notification-preferences"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
