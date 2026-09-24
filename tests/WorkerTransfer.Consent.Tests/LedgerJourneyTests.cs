using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Consent.Tests;

/// <summary>The ledger over HTTP, against the real schema.</summary>
[Collection(PostgresCollection.Name)]
public class LedgerJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private const string Secret = "erasure-secret-for-the-test";

    private WebApplicationFactory<Program> _service = null!;

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:consent", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Geheimnis", Secret);
            host.UseSetting("environment", "Development");
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient As(Guid subject)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenShape.For(subject));
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Grant(
        HttpClient browser, Guid subject, string skill) =>
        browser.PostAsJsonAsync(
            "/consent/grant", new { subject_id = subject, capability = skill });

    private static Task<HttpResponseMessage> Revoke(
        HttpClient browser, Guid subject, string skill, string reason) =>
        browser.PostAsJsonAsync(
            "/consent/revoke", new { subject_id = subject, capability = skill, reason = reason });

    private static Task<HttpResponseMessage> Check(
        HttpClient browser, Guid subject, string skill) =>
        browser.PostAsJsonAsync(
            "/consent/check", new { subject_id = subject, capability = skill });

    [Fact]
    public async Task Granting_revoking_and_granting_again()
    {
        var anna = Guid.CreateVersion7();
        var browser = As(anna);

        (await Grant(browser, anna, "profile.visibility:public")).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await Json(await Check(browser, anna, "profile.visibility:public")))
            .GetProperty("granted").GetBoolean().Should().BeTrue();

        (await Revoke(browser, anna, "profile.visibility:public", "not for now"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(await Check(browser, anna, "profile.visibility:public")))
            .GetProperty("granted").GetBoolean().Should().BeFalse();

        // Withdrawing must never be a one-way door for the person it belongs to.
        (await Grant(browser, anna, "profile.visibility:public")).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await Json(await Check(browser, anna, "profile.visibility:public")))
            .GetProperty("granted").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Absence is a state, not an error: a capability nobody granted is simply
    /// not granted. Consuming services must be able to ask about anything.
    /// </summary>
    [Fact]
    public async Task A_capability_never_touched_is_200_and_not_404()
    {
        var subject = Guid.CreateVersion7();

        var response = await Check(As(Guid.CreateVersion7()), subject, "profile.visibility:public");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("granted").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// Strict self-management: there is no delegation model, and admin or
    /// guardian consent is a later, deliberate decision rather than something
    /// allowed by omission.
    /// </summary>
    [Fact]
    public async Task Nobody_grants_for_somebody_else()
    {
        var anna = Guid.CreateVersion7();
        var stranger = Guid.CreateVersion7();

        var attempt = await Grant(As(stranger), anna, "profile.visibility:public");

        attempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Open to any authenticated caller about any person — that is what makes
    /// the ledger usable as an enabler by consuming services.
    /// </summary>
    [Fact]
    public async Task Any_authenticated_caller_may_ask_about_anyone()
    {
        var anna = Guid.CreateVersion7();
        await Grant(As(anna), anna, "profile.visibility:public");

        var response = await Check(As(Guid.CreateVersion7()), anna, "profile.visibility:public");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("granted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Without_authentication_the_ledger_answers_nothing()
    {
        var response = await _service.CreateClient().PostAsJsonAsync(
            "/consent/check",
            new { subject_id = Guid.CreateVersion7(), capability = "profile.visibility:public" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The reason is free text a person wrote about themselves. It must not
    /// ride along on a query anyone may issue.
    /// </summary>
    [Fact]
    public async Task The_check_does_not_give_away_the_withdrawal_reason()
    {
        var anna = Guid.CreateVersion7();
        var browser = As(anna);
        const string reason = "my employer must not see this";

        await Grant(browser, anna, "profile.visibility:public");
        await Revoke(browser, anna, "profile.visibility:public", reason);

        var foreign = await Check(As(Guid.CreateVersion7()), anna, "profile.visibility:public");
        var body = await foreign.Content.ReadAsStringAsync();

        body.Should().NotContain("employer");
        body.Should().NotContain("reason");
    }

    /// <summary>
    /// The opposite case: towards the person themselves there is no ground to
    /// withhold what they wrote. That is the difference between "belongs to
    /// them" and "is anybody else's business".
    /// </summary>
    [Fact]
    public async Task Ones_own_history_does_show_the_reason()
    {
        var anna = Guid.CreateVersion7();
        var browser = As(anna);
        const string reason = "my employer must not see this";

        await Grant(browser, anna, "profile.visibility:public");
        await Revoke(browser, anna, "profile.visibility:public", reason);

        var own = await browser.GetAsync("/consent/me/history");

        (await own.Content.ReadAsStringAsync()).Should().Contain(reason);
    }

    /// <summary>
    /// A foreign list would say which OTHER companies hold access to somebody.
    /// The endpoint takes no subject at all — not in the path, not as a query.
    /// </summary>
    [Fact]
    public async Task Ones_own_list_takes_no_foreign_subject()
    {
        var anna = Guid.CreateVersion7();
        var stranger = Guid.CreateVersion7();
        await Grant(As(anna), anna, "profile.visibility:public");

        var mine = await Json(await As(stranger)
            .GetAsync($"/consent/me?subject_id={anna}"));

        mine.GetArrayLength().Should().Be(
            0, "the parameter is not read, it is the caller's own list");
    }

    [Fact]
    public async Task Ones_own_list_shows_only_what_holds_right_now()
    {
        var anna = Guid.CreateVersion7();
        var browser = As(anna);

        await Grant(browser, anna, "profile.visibility:public");
        await Grant(browser, anna, "portfolio.visibility:public");
        await Revoke(browser, anna, "portfolio.visibility:public", "changed my mind");

        var mine = await Json(await browser.GetAsync("/consent/me"));

        mine.GetArrayLength().Should().Be(1);
        mine[0].GetProperty("capability").GetString().Should().Be("profile.visibility:public");
    }

    /// <summary>
    /// The order is part of the contract: the caller maps answers onto its rows,
    /// and a pair asked twice would otherwise be ambiguous.
    /// </summary>
    [Fact]
    public async Task The_batch_check_answers_in_the_order_of_the_questions()
    {
        var anna = Guid.CreateVersion7();
        var berta = Guid.CreateVersion7();
        await Grant(As(anna), anna, "profile.visibility:public");

        var response = await As(Guid.CreateVersion7()).PostAsJsonAsync("/consent/check-batch", new
        {
            pairs = new[]
            {
                new { subject_id = berta, capability = "profile.visibility:public" },
                new { subject_id = anna, capability = "profile.visibility:public" },
                new { subject_id = berta, capability = "portfolio.visibility:public" }
            }
        });

        var results = (await Json(response)).GetProperty("results");

        results.GetArrayLength().Should().Be(3, "one answer per question");
        results[0].GetProperty("granted").GetBoolean().Should().BeFalse();
        results[1].GetProperty("granted").GetBoolean().Should().BeTrue();
        results[2].GetProperty("granted").GetBoolean().Should().BeFalse();
        results[1].GetProperty("subject_id").GetGuid().Should().Be(anna);
    }

    /// <summary>
    /// A batch must not give away what the single question withholds.
    /// </summary>
    [Fact]
    public async Task The_batch_check_gives_away_just_as_little()
    {
        var anna = Guid.CreateVersion7();
        var browser = As(anna);
        const string reason = "my employer must not see this";

        await Grant(browser, anna, "profile.visibility:public");
        await Revoke(browser, anna, "profile.visibility:public", reason);

        var response = await As(Guid.CreateVersion7()).PostAsJsonAsync("/consent/check-batch", new
        {
            pairs = new[] { new { subject_id = anna, capability = "profile.visibility:public" } }
        });

        (await response.Content.ReadAsStringAsync()).Should().NotContain("employer");
    }

    /// <summary>
    /// Derived, not chosen: one page is fifty people, two capabilities each. A
    /// batch makes asking cheaper, and a ceiling keeps that difference small.
    /// </summary>
    [Fact]
    public async Task More_than_a_hundred_pairs_are_refused()
    {
        var pairs = Enumerable.Range(0, ConsentLimits.MaxBatchSize + 1)
            .Select(_ => new
            {
                subject_id = Guid.CreateVersion7(),
                capability = "profile.visibility:public"
            })
            .ToArray();

        var response = await As(Guid.CreateVersion7())
            .PostAsJsonAsync("/consent/check-batch", new { pairs = pairs });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Exactly_a_hundred_pairs_go_through()
    {
        var pairs = Enumerable.Range(0, ConsentLimits.MaxBatchSize)
            .Select(_ => new
            {
                subject_id = Guid.CreateVersion7(),
                capability = "profile.visibility:public"
            })
            .ToArray();

        var response = await As(Guid.CreateVersion7())
            .PostAsJsonAsync("/consent/check-batch", new { pairs = pairs });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("results").GetArrayLength()
            .Should().Be(ConsentLimits.MaxBatchSize);
    }

    /// <summary>
    /// The canonical rule lives in memory, the read path reduces in SQL. Two
    /// ways to the same answer that can differ are worse than no second way, so
    /// this pins that they cannot.
    /// </summary>
    [Fact]
    public async Task The_list_and_the_check_cannot_contradict_each_other()
    {
        var anna = Guid.CreateVersion7();
        var browser = As(anna);

        string[] all =
        [
            "profile.visibility:public",
            "portfolio.visibility:public",
            "resume.visibility:tenant:9f8e7d6c-5b4a-4938-8271-605f4e3d2c1b"
        ];

        foreach (var skill in all)
        {
            await Grant(browser, anna, skill);
        }

        await Revoke(browser, anna, all[1], "changed my mind");

        var list = (await Json(await browser.GetAsync("/consent/me")))
            .EnumerateArray()
            .Select(entry => entry.GetProperty("capability").GetString())
            .ToHashSet();

        foreach (var skill in all)
        {
            var checkResult = (await Json(await Check(browser, anna, skill)))
                .GetProperty("granted").GetBoolean();

            checkResult.Should().Be(
                list.Contains(skill),
                $"list and check must agree about {skill}");
        }
    }

    /// <summary>
    /// It was capability-scoped, every capability here is a visibility, and
    /// equating the two would have erased a CV on a visibility withdrawal
    /// nobody asked for.
    /// </summary>
    [Fact]
    public async Task There_is_no_consent_delete()
    {
        var anna = Guid.CreateVersion7();

        var attempt = await As(anna).PostAsJsonAsync(
            "/consent/delete",
            new { subject_id = anna, capability = "profile.visibility:public" });

        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The shape is checked, the vocabulary is not.
    /// </summary>
    /// <remarks>
    /// A list of permitted capabilities would be a claim about which
    /// permissions can exist, and the ledger has to be able to answer about
    /// anything a consumer asks. So <c>profile..visibility</c> is odd but
    /// structurally fine and goes through — only what cannot be a token at all
    /// is refused.
    /// </remarks>
    [Theory]
    [InlineData("NOT A TOKEN")]
    [InlineData("Profile.Visibility")]
    [InlineData("1profile.visibility")]
    [InlineData("")]
    public async Task What_cannot_be_a_capability_is_refused(string skill)
    {
        var anna = Guid.CreateVersion7();

        var attempt = await Grant(As(anna), anna, skill);

        attempt.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
