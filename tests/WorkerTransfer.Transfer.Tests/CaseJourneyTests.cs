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
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Transfer.Tests;

/// <summary>The case: three yeses, a no at any time.</summary>
[Collection(PostgresCollection.Name)]
public class CaseJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeLedger _ledger = new();

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:transfer", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Secret", "erasure-secret");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _ledger));
                services.Replace(ServiceDescriptor.Scoped<IDelivery, ProbeDelivery>());
                // Offer and completion demand an `admin` since PBI-2. These
                // suites measure the case and not the role — so the probe
                // answers like an owner. `CompanyRoleTests` turns it to
                // `member`.
                services.Replace(ServiceDescriptor.Scoped<ICompanyRoles>(_ => new RoleProbe()));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AsCompany(Guid company) => With(TokenShape.Company(Guid.CreateVersion7(), company));

    private HttpClient AsPerson(Guid subject) => With(TokenShape.Person(subject));

    private HttpClient With(string token)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>An approachable person whose status is released to this company.</summary>
    private async Task<Guid> Approachable(
        Guid company, bool isEmployed = false, string state = "listening")
    {
        var subject = Guid.CreateVersion7();

        await AsPerson(subject).PutAsJsonAsync("/market/me", new
        {
            availability = state, employed = isEmployed, note = string.Empty
        });

        _ledger.MarketReleased.Add((subject, company));

        return subject;
    }

    private Task<HttpResponseMessage> ShowInterest(Guid company, Guid subject) =>
        AsCompany(company).PostAsJsonAsync("/transfers", new
        {
            subject_id = subject, message = "We might have something for you."
        });

    /// <summary>
    /// An offer that starts before the person could leave is marked — for her,
    /// and only for her (VISION-FRAGEN H2).
    /// </summary>
    [Fact]
    public async Task An_offer_before_the_earliest_change_is_marked_for_the_person_only()
    {
        var company = Guid.CreateVersion7();
        var anna = await Approachable(company, isEmployed: true);
        var asAnna = AsPerson(anna);
        var asCompany = AsCompany(company);

        // Three months to the end of a quarter: from any day today the earliest
        // start lies at least three months out.
        (await asAnna.PutAsJsonAsync("/market/me/notice", new { length = 3, unit = "month", end = "quarter_end" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var id = (await Json(await ShowInterest(company, anna))).GetProperty("id").GetGuid();
        await asAnna.PostAsync($"/transfers/{id}/accept-talk", null);
        // Next month: earlier than three months to a quarter's end, from any day.
        var tooEarly = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1).ToString("yyyy-MM");
        var offered = await asCompany.PostAsJsonAsync($"/transfers/{id}/offer", new { note = "Soon.", start_on = tooEarly });
        offered.StatusCode.Should().Be(HttpStatusCode.OK, await offered.Content.ReadAsStringAsync());

        var hers = (await Json(await asAnna.GetAsync("/transfers/me"))).EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == id);
        hers.TryGetProperty("starts_before_earliest", out var mark).Should().BeTrue(hers.ToString());
        mark.GetBoolean().Should().BeTrue();

        var theirs = (await Json(await asCompany.GetAsync("/transfers"))).EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == id);
        theirs.TryGetProperty("starts_before_earliest", out _).Should().BeFalse("the company learns nothing of her notice");
    }

    /// <summary>
    /// A notice period binds somebody with a job. Stated before she left it, it
    /// must not mark offers afterwards — and the dates go, the statement stays.
    /// </summary>
    [Fact]
    public async Task Without_a_job_the_notice_period_marks_nothing()
    {
        var company = Guid.CreateVersion7();
        var anna = await Approachable(company, isEmployed: false);
        var asAnna = AsPerson(anna);
        await asAnna.PutAsJsonAsync("/market/me/notice", new { length = 3, unit = "month", end = "quarter_end" });

        var id = (await Json(await ShowInterest(company, anna))).GetProperty("id").GetGuid();
        await asAnna.PostAsync($"/transfers/{id}/accept-talk", null);
        var tooEarly = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1).ToString("yyyy-MM");
        await AsCompany(company).PostAsJsonAsync($"/transfers/{id}/offer", new { note = "Soon.", start_on = tooEarly });

        var hers = (await Json(await asAnna.GetAsync("/transfers/me"))).EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == id);
        hers.TryGetProperty("starts_before_earliest", out _).Should().BeFalse(hers.ToString());

        var notice = await Json(await asAnna.GetAsync("/market/me/notice"));
        notice.GetProperty("length").GetInt32().Should().Be(3);
        notice.GetProperty("earliest_start").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("Oktober 2026")]
    [InlineData("2026-10-15")]
    public async Task A_start_that_is_no_month_is_422_not_500(string start)
    {
        var company = Guid.CreateVersion7();
        var anna = await Approachable(company);
        var id = (await Json(await ShowInterest(company, anna))).GetProperty("id").GetGuid();
        await AsPerson(anna).PostAsync($"/transfers/{id}/accept-talk", null);

        (await AsCompany(company).PostAsJsonAsync($"/transfers/{id}/offer", new { note = "Offer.", start_on = start }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_notice_period_is_hers_to_state_read_and_withdraw()
    {
        var asAnna = AsPerson(Guid.CreateVersion7());
        await asAnna.PutAsJsonAsync("/market/me", new { availability = "listening", employed = true, note = string.Empty });

        var empty = await Json(await asAnna.GetAsync("/market/me/notice"));
        empty.GetProperty("length").ValueKind.Should().Be(JsonValueKind.Null);

        var stated = await Json(await asAnna.PutAsJsonAsync(
            "/market/me/notice", new { length = 4, unit = "week", end = "fifteenth_or_month_end" }));
        stated.GetProperty("earliest_start").GetString().Should().NotBeNullOrEmpty();

        (await asAnna.PutAsJsonAsync("/market/me/notice", new { length = 99, unit = "month", end = "month_end" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await asAnna.DeleteAsync("/market/me/notice")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Json(await asAnna.GetAsync("/market/me/notice"))).GetProperty("length").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    /// <summary>The whole path: interest, conversation, offer, acceptance, completion.</summary>
    [Fact]
    public async Task Interest_conversation_offer_acceptance_completion()
    {
        var company = Guid.CreateVersion7();
        var anna = await Approachable(company);
        var asAnna = AsPerson(anna);
        var asCompany = AsCompany(company);

        var created = await ShowInterest(company, anna);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(created)).GetProperty("status").GetString().Should().Be("interested");

        var id = (await Json(created)).GetProperty("id").GetGuid();

        (await asAnna.PostAsync($"/transfers/{id}/accept-talk", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var offered = await asCompany.PostAsJsonAsync($"/transfers/{id}/offer", new
        {
            note = "We are offering the job.", start_on = "2026-10"
        });

        offered.StatusCode.Should().Be(HttpStatusCode.OK);

        (await asAnna.PostAsync($"/transfers/{id}/accept-offer", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var done = await asCompany.PostAsync($"/transfers/{id}/complete", null);

        done.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(done)).GetProperty("status").GetString().Should().Be("completed");
    }

    /// <summary>
    /// Whoever is employed completes themselves — they are the only one who
    /// knows whether the release is there.
    /// </summary>
    [Fact]
    public async Task With_a_release_requirement_the_person_completes()
    {
        var company = Guid.CreateVersion7();
        var anna = await Approachable(company, isEmployed: true);
        var asAnna = AsPerson(anna);
        var asCompany = AsCompany(company);

        var id = (await Json(await ShowInterest(company, anna))).GetProperty("id").GetGuid();

        (await Json(await asAnna.GetAsync("/transfers/me")))
            .EnumerateArray().Single()
            .GetProperty("requires_release").GetBoolean().Should().BeTrue();

        await asAnna.PostAsync($"/transfers/{id}/accept-talk", null);
        await asCompany.PostAsJsonAsync($"/transfers/{id}/offer", new { note = "Offer." });
        await asAnna.PostAsync($"/transfers/{id}/accept-offer", null);

        // The company may NOT complete now.
        var tooEarly = await asCompany.PostAsync($"/transfers/{id}/complete", null);

        tooEarly.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var confirmed = await asAnna.PostAsync($"/transfers/{id}/confirm-release", null);

        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await Json(confirmed);
        body.GetProperty("status").GetString().Should().Be("completed");
        body.GetProperty("release_confirmed").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Without a release requirement there is nothing to confirm.
    /// </summary>
    [Fact]
    public async Task Without_a_release_requirement_there_is_nothing_to_confirm()
    {
        var company = Guid.CreateVersion7();
        var anna = await Approachable(company);
        var asAnna = AsPerson(anna);
        var asCompany = AsCompany(company);

        var id = (await Json(await ShowInterest(company, anna))).GetProperty("id").GetGuid();
        await asAnna.PostAsync($"/transfers/{id}/accept-talk", null);
        await asCompany.PostAsJsonAsync($"/transfers/{id}/offer", new { note = "Offer." });
        await asAnna.PostAsync($"/transfers/{id}/accept-offer", null);

        var needless = await asAnna.PostAsync($"/transfers/{id}/confirm-release", null);

        needless.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// A later change of the market status does not shift the terms of a
    /// running case retroactively.
    /// </summary>
    [Fact]
    public async Task The_release_requirement_is_copied_at_creation()
    {
        var company = Guid.CreateVersion7();
        var anna = await Approachable(company, isEmployed: true);
        var asAnna = AsPerson(anna);

        var id = (await Json(await ShowInterest(company, anna))).GetProperty("id").GetGuid();

        // They resign in the middle of the conversation.
        await asAnna.PutAsJsonAsync("/market/me", new
        {
            availability = "listening", employed = false, note = string.Empty
        });

        var now = (await Json(await asAnna.GetAsync("/transfers/me")))
            .EnumerateArray().Single(entry => entry.GetProperty("id").GetGuid() == id);

        now.GetProperty("requires_release").GetBoolean().Should().BeTrue();
    }

    /// <summary>The release requirement has no setter.</summary>
    /// <remarks>
    /// Not "is not changed" but <em>cannot</em> be: there is no assignment for
    /// it, and therefore no later move can shift the terms of a running case
    /// either. This check falls the moment somebody adds one — a counter-probe
    /// showed that this cannot be refuted over HTTP for as long as the setter is
    /// missing.
    /// </remarks>
    [Fact]
    public void The_release_requirement_has_no_setter()
    {
        typeof(Domain.Cases.Transfer)
            .GetProperty(nameof(Domain.Cases.Transfer.NeedsRelease))!
            .CanWrite.Should().BeFalse();
    }

    /// <summary>The release permits SEEING, not DISTURBING.</summary>
    [Fact]
    public async Task Unavailable_means_no_even_with_a_release()
    {
        var company = Guid.CreateVersion7();
        var anna = await Approachable(company, state: "unavailable");

        var attempt = await ShowInterest(company, anna);

        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// No status, no release, or "not right now" — all the same to the
    /// outside.
    /// </summary>
    /// <remarks>
    /// Otherwise the endpoint would be an oracle about who is on the platform
    /// and who is listening right now.
    /// </remarks>
    [Fact]
    public async Task The_three_nos_look_the_same()
    {
        var company = Guid.CreateVersion7();
        var withoutStatus = Guid.CreateVersion7();
        var withoutRelease = Guid.CreateVersion7();
        await AsPerson(withoutRelease).PutAsJsonAsync("/market/me", new
        {
            availability = "open", employed = false, note = string.Empty
        });
        var notApproachable = await Approachable(company, state: "unavailable");

        var responses = new List<string>();

        foreach (var subject in new[] { withoutStatus, withoutRelease, notApproachable })
        {
            var response = await ShowInterest(company, subject);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            responses.Add(
                (await Json(response)).GetProperty("detail").GetString() ?? string.Empty);
        }

        responses.Distinct().Should().ContainSingle();
    }

    /// <summary>
    /// A second running case would be following up past the refusal.
    /// </summary>
    [Fact]
    public async Task Only_one_case_runs_per_pair()
    {
        var company = Guid.CreateVersion7();
        var anna = await Approachable(company);

        await ShowInterest(company, anna);
        var second = await ShowInterest(company, anna);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>After a refusal a new one may begin.</summary>
    [Fact]
    public async Task After_the_refusal_a_new_one_may_begin()
    {
        var company = Guid.CreateVersion7();
        var anna = await Approachable(company);

        var id = (await Json(await ShowInterest(company, anna))).GetProperty("id").GetGuid();
        await AsPerson(anna).PostAsync($"/transfers/{id}/decline", null);

        var fresh = await ShowInterest(company, anna);

        fresh.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(fresh)).GetProperty("id").GetGuid().Should().NotBe(id);
    }

    /// <summary>
    /// Declining is possible from every running state — a process one cannot
    /// get out of is a trap.
    /// </summary>
    [Fact]
    public async Task Declining_works_from_every_running_state()
    {
        var company = Guid.CreateVersion7();
        var asCompany = AsCompany(company);

        foreach (var upTo in new[] { "interested", "talking", "offered", "accepted" })
        {
            var anna = await Approachable(company);
            var asAnna = AsPerson(anna);
            var id = (await Json(await ShowInterest(company, anna))).GetProperty("id").GetGuid();

            if (upTo != "interested")
            {
                await asAnna.PostAsync($"/transfers/{id}/accept-talk", null);
            }

            if (upTo is "offered" or "accepted")
            {
                await asCompany.PostAsJsonAsync($"/transfers/{id}/offer", new { note = "Offer." });
            }

            if (upTo == "accepted")
            {
                await asAnna.PostAsync($"/transfers/{id}/accept-offer", null);
            }

            var declined = await asAnna.PostAsync($"/transfers/{id}/decline", null);

            declined.StatusCode.Should().Be(HttpStatusCode.OK, $"out of {upTo}");
            (await Json(declined)).GetProperty("status").GetString().Should().Be("declined");
        }
    }

    /// <summary>Somebody else's case is like none from outside.</summary>
    [Fact]
    public async Task Somebody_elses_case_is_404_and_not_403()
    {
        var company = Guid.CreateVersion7();
        var anna = await Approachable(company);
        var id = (await Json(await ShowInterest(company, anna))).GetProperty("id").GetGuid();

        var otherPerson = await AsPerson(Guid.CreateVersion7())
            .PostAsync($"/transfers/{id}/accept-talk", null);
        var otherCompany = await AsCompany(Guid.CreateVersion7())
            .PostAsync($"/transfers/{id}/withdraw", null);

        otherPerson.StatusCode.Should().Be(HttpStatusCode.NotFound);
        otherCompany.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>The offer to the person carries no fee any more (C3, ADR-0050).</summary>
    /// <remarks>
    /// It had no recipient: the current employer was never part of the case.
    /// A <c>fee_cents</c> from an old client is ignored, not stored, and does
    /// not come back — a fee is agreed between two companies now.
    /// </remarks>
    [Fact]
    public async Task An_offer_carries_no_fee_any_more()
    {
        var company = Guid.CreateVersion7();
        var anna = await Approachable(company);
        var id = (await Json(await ShowInterest(company, anna))).GetProperty("id").GetGuid();
        await AsPerson(anna).PostAsync($"/transfers/{id}/accept-talk", null);

        var offer = await AsCompany(company).PostAsJsonAsync($"/transfers/{id}/offer", new
        {
            note = "Offer.", fee_cents = 250_000L
        });

        offer.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(offer)).TryGetProperty("offer_fee_cents", out _).Should().BeFalse();

        var hers = (await Json(await AsPerson(anna).GetAsync("/transfers/me"))).EnumerateArray().Single();
        hers.TryGetProperty("offer_fee_cents", out _).Should().BeFalse();
    }

    /// <summary>Only the company's moves are notified.</summary>
    /// <remarks>
    /// The person learns nothing from the company by mail: it is the side that
    /// wants something, and a mail to a company distribution list carrying a
    /// human being's name would be the same leak channel, only the other way
    /// round.
    /// </remarks>
    [Fact]
    public async Task Only_the_companys_moves_are_notified()
    {
        var company = Guid.CreateVersion7();
        var anna = await Approachable(company);
        var id = (await Json(await ShowInterest(company, anna))).GetProperty("id").GetGuid();

        // Interest: notified.
        await using (var afterInterest = postgres.Context())
        {
            (await afterInterest.Set<OutboxRow>().CountAsync(row => row.UserId == anna))
                .Should().Be(1);
        }

        // Her move: not notified.
        await AsPerson(anna).PostAsync($"/transfers/{id}/accept-talk", null);

        await using (var afterHerMove = postgres.Context())
        {
            (await afterHerMove.Set<OutboxRow>().CountAsync(row => row.UserId == anna))
                .Should().Be(1);
        }

        // Their offer: notified.
        await AsCompany(company).PostAsJsonAsync($"/transfers/{id}/offer", new { note = "Yes." });

        await using var afterOffer = postgres.Context();
        var notes = await afterOffer.Set<OutboxRow>()
            .Where(row => row.UserId == anna)
            .ToListAsync();

        notes.Should().HaveCount(2);
        notes.Should().OnlyContain(row => row.Kind == "transfer_update");
    }

    /// <summary>
    /// The revocation does NOT end the running case: it has a door and a
    /// refusal of its own.
    /// </summary>
    [Fact]
    public async Task A_revocation_does_not_end_the_case()
    {
        var company = Guid.CreateVersion7();
        var anna = Guid.CreateVersion7();
        _ledger.ProfileReleased.Add(anna);
        var asAnna = AsPerson(anna);

        await asAnna.PutAsJsonAsync("/market/me", new
        {
            availability = "listening", employed = false, note = string.Empty
        });

        var request = (await Json(
                await AsCompany(company).PostAsync($"/market/{anna}/requests", null)))
            .GetProperty("id").GetGuid();
        await asAnna.PostAsync($"/market/requests/{request}/grant", null);

        var id = (await Json(await ShowInterest(company, anna))).GetProperty("id").GetGuid();

        await asAnna.PostAsync($"/market/requests/{request}/revoke", null);

        var stillRunning = (await Json(await asAnna.GetAsync("/transfers/me")))
            .EnumerateArray().Single(entry => entry.GetProperty("id").GetGuid() == id);

        stillRunning.GetProperty("status").GetString().Should().Be("interested");
    }

    /// <summary>Only a company shows interest.</summary>
    [Fact]
    public async Task A_private_person_shows_no_interest()
    {
        var response = await AsPerson(Guid.CreateVersion7()).PostAsJsonAsync("/transfers", new
        {
            subject_id = Guid.CreateVersion7(), message = "Hello."
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
