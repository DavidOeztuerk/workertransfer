using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Applications.Tests;

/// <summary>Applying, withdrawing, moving — and what the ledger sees along the way.</summary>
[Collection(PostgresCollection.Name)]
public class ApplicationJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private const string ErasureSecret = "erasure-secret";

    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeLedger _ledger = new();
    private readonly ProbeJobs _jobs = new();
    private readonly ProbeMembers _members = new();
    private readonly ProbeAiAccess _ai = new();

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:applications", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Secret", ErasureSecret);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(
                    ServiceDescriptor.Scoped<IConsentWriter>(_ => _ledger));
                services.Replace(ServiceDescriptor.Scoped<IJobLookup>(_ => _jobs));
                services.Replace(ServiceDescriptor.Scoped<IDelivery, ProbeDelivery>());
                services.Replace(ServiceDescriptor.Scoped<ICompanyMemberLookup>(_ => _members));
                services.Replace(ServiceDescriptor.Scoped<IAiAccessLookup>(_ => _ai));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AsCompany(Guid subject, Guid company) =>
        With(TokenShape.Company(subject, company));

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

    private static Task<HttpResponseMessage> Apply(
        HttpClient browser, Guid job, bool resume = false, bool portfolio = false) =>
        browser.PostAsJsonAsync("/applications", new
        {
            job_id = job,
            message = "I like building distributed systems.",
            shares_resume = resume,
            shares_portfolio = portfolio
        });

    /// <summary>
    /// The release comes about in the ledger, not in this database — and it
    /// names the recipient.
    /// </summary>
    [Fact]
    public async Task Applying_grants_the_releases_for_exactly_this_company()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var job = _jobs.Publish(company);

        _members.For(new TenantId(company), new SubjectId(Guid.CreateVersion7()));

        var response = await Apply(AsPerson(subject), job, resume: true);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await Json(response);
        body.GetProperty("status").GetString().Should().Be("submitted");
        body.GetProperty("tenant_id").GetGuid().Should().Be(company);
        body.GetProperty("shares_resume").GetBoolean().Should().BeTrue();

        _ledger.Granted.Should().Equal(
            $"profile.visibility:tenant:{company}",
            $"resume.visibility:tenant:{company}");
    }

    /// <summary>
    /// An arriving application creates exactly one outbox row per member. The
    /// row carries no content — only id and kind (ADR-0025).
    /// </summary>
    [Fact]
    public async Task Applying_leaves_one_content_free_intent_per_member()
    {
        var company = Guid.CreateVersion7();
        var anna = new SubjectId(Guid.CreateVersion7());
        var bea = new SubjectId(Guid.CreateVersion7());
        var job = _jobs.Publish(company);
        _members.For(new TenantId(company), anna, bea);

        var response = await Apply(AsPerson(Guid.CreateVersion7()), job);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var context = postgres.Context();
        var notes = await context.Set<OutboxRow>()
            .Where(row => row.UserId == anna.Value || row.UserId == bea.Value)
            .ToListAsync();

        notes.Should().HaveCount(2);
        notes.Select(row => row.UserId).Should().BeEquivalentTo([anna.Value, bea.Value]);
        notes.Should().OnlyContain(row => row.Kind == "application_received");
        notes.Should().OnlyContain(row => row.LastError == string.Empty);
    }

    /// <summary>
    /// If identity-service is silent, no application may come about: it would
    /// otherwise go out without anybody in the company learning of it.
    /// </summary>
    [Fact]
    public async Task A_silent_identity_service_leaves_no_application_behind()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var job = _jobs.Publish(company);
        _members.Silent = true;

        var response = await Apply(AsPerson(subject), job);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        await using var context = postgres.Context();
        (await context.Applications.AnyAsync(row => row.SubjectId == subject))
            .Should().BeFalse();
        _ledger.Granted.Should().BeEmpty();
    }

    /// <summary>The folder hangs on the one id — foreign and missing are 404.</summary>
    [Fact]
    public async Task An_application_is_read_only_by_whoever_may_see_it()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var job = _jobs.Publish(company);
        var id = (await Json(await Apply(AsPerson(subject), job))).GetProperty("id").GetGuid();

        (await AsPerson(subject).GetAsync($"/applications/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await AsCompany(Guid.CreateVersion7(), company).GetAsync($"/applications/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await AsPerson(Guid.CreateVersion7()).GetAsync($"/applications/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await AsCompany(Guid.CreateVersion7(), Guid.CreateVersion7()).GetAsync($"/applications/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The profile always goes along — an application without any statement
    /// about the person is none. Portfolio and résumé only when they are sent
    /// along.
    /// </summary>
    [Fact]
    public async Task Without_attachments_only_the_profile_goes_into_the_ledger()
    {
        var company = Guid.CreateVersion7();
        var job = _jobs.Publish(company);

        await Apply(AsPerson(Guid.CreateVersion7()), job);

        _ledger.Granted.Should().Equal($"profile.visibility:tenant:{company}");
    }

    /// <summary>
    /// If the ledger is silent, no application may come about: the company would
    /// see a case whose data it may not read.
    /// </summary>
    [Fact]
    public async Task A_silent_ledger_leaves_no_application_behind()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var job = _jobs.Publish(company);
        _ledger.Silent = true;

        var response = await Apply(AsPerson(subject), job);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        await using var context = postgres.Context();
        (await context.Applications.AnyAsync(row => row.SubjectId == subject))
            .Should().BeFalse();
    }

    /// <summary>
    /// A silent jobs-service is no "does not exist": the person would otherwise
    /// get a rejection nobody gave.
    /// </summary>
    [Fact]
    public async Task A_silent_jobs_service_becomes_503_and_not_404()
    {
        _jobs.Silent = true;

        var response = await Apply(AsPerson(Guid.CreateVersion7()), Guid.CreateVersion7());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>Draft, closed and missing stay indistinguishable.</summary>
    [Fact]
    public async Task A_job_that_does_not_exist_publicly_is_404()
    {
        var response = await Apply(AsPerson(Guid.CreateVersion7()), Guid.CreateVersion7());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The withdrawal revokes all four — also what was never granted.
    /// </summary>
    /// <remarks>
    /// That closes the gap a successful ledger call with a failed commit would
    /// have left.
    /// </remarks>
    [Fact]
    public async Task Withdrawing_revokes_all_four_unconditionally()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var job = _jobs.Publish(company);
        var browser = AsPerson(subject);

        var id = (await Json(await Apply(browser, job))).GetProperty("id").GetGuid();

        var response = await browser.PostAsync($"/applications/{id}/withdraw", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("status").GetString().Should().Be("withdrawn");

        _ledger.Revoked.Should().Equal(
            $"profile.visibility:tenant:{company}",
            $"resume.visibility:tenant:{company}",
            $"portfolio.visibility:tenant:{company}",
            // The fourth came with the application folder (ADR-0035). It stands
            // on its own and is not carried along under `resume`: the career
            // history is self-written text, a reference a third party's document
            // with names and grades — whoever wants to show the one and not the
            // other must be able to.
            $"documents.visibility:tenant:{company}");
    }

    /// <summary>A foreign application looks like none from outside.</summary>
    [Fact]
    public async Task Withdrawing_a_foreign_application_is_404_and_not_403()
    {
        var company = Guid.CreateVersion7();
        var job = _jobs.Publish(company);
        var id = (await Json(await Apply(AsPerson(Guid.CreateVersion7()), job)))
            .GetProperty("id").GetGuid();

        var response = await AsPerson(Guid.CreateVersion7())
            .PostAsync($"/applications/{id}/withdraw", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// After a withdrawal a new decision is possible — after a rejection not.
    /// </summary>
    [Fact]
    public async Task After_a_withdrawal_it_works_again_after_a_rejection_not()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var job = _jobs.Publish(company);
        var person = AsPerson(subject);
        var employer = AsCompany(Guid.CreateVersion7(), company);

        var id = (await Json(await Apply(person, job))).GetProperty("id").GetGuid();
        await person.PostAsync($"/applications/{id}/withdraw", null);

        var again = await Apply(person, job);

        again.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(again)).GetProperty("id").GetGuid().Should().Be(id);

        (await employer.PostAsJsonAsync(
            $"/applications/{id}/status", new { status = "rejected" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var chasing = await Apply(person, job);

        chasing.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// The company's move is reported — and the note commits with it (ADR-0025).
    /// </summary>
    [Fact]
    public async Task Moving_leaves_the_intent_in_the_outbox()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var job = _jobs.Publish(company);

        var id = (await Json(await Apply(AsPerson(subject), job))).GetProperty("id").GetGuid();

        var response = await AsCompany(Guid.CreateVersion7(), company)
            .PostAsJsonAsync($"/applications/{id}/status", new { status = "reviewing" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("status").GetString().Should().Be("reviewing");

        await using var context = postgres.Context();
        var notes = await context.Set<OutboxRow>()
            .Where(row => row.UserId == subject)
            .ToListAsync();

        notes.Should().ContainSingle()
            .Which.Kind.Should().Be("application_update");
    }

    /// <summary>
    /// The person's withdrawal is NOT reported: she knows what she did.
    /// </summary>
    [Fact]
    public async Task The_persons_withdrawal_is_not_reported()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var job = _jobs.Publish(company);
        var browser = AsPerson(subject);

        var id = (await Json(await Apply(browser, job))).GetProperty("id").GetGuid();
        await browser.PostAsync($"/applications/{id}/withdraw", null);

        await using var context = postgres.Context();
        (await context.Set<OutboxRow>().AnyAsync(row => row.UserId == subject))
            .Should().BeFalse();
    }

    /// <summary>
    /// A company does not withdraw an application — that is the person's act,
    /// and allowing it here would mean doing it in her name.
    /// </summary>
    [Fact]
    public async Task A_company_cannot_withdraw_in_her_name()
    {
        var company = Guid.CreateVersion7();
        var job = _jobs.Publish(company);
        var id = (await Json(await Apply(AsPerson(Guid.CreateVersion7()), job)))
            .GetProperty("id").GetGuid();

        var response = await AsCompany(Guid.CreateVersion7(), company)
            .PostAsJsonAsync($"/applications/{id}/status", new { status = "withdrawn" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>A state word that does not exist is an input and no crash.</summary>
    [Fact]
    public async Task An_invented_state_is_422()
    {
        var company = Guid.CreateVersion7();
        var job = _jobs.Publish(company);
        var id = (await Json(await Apply(AsPerson(Guid.CreateVersion7()), job)))
            .GetProperty("id").GetGuid();

        var response = await AsCompany(Guid.CreateVersion7(), company)
            .PostAsJsonAsync($"/applications/{id}/status", new { status = "invited" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>A foreign company sees 404, not 403.</summary>
    [Fact]
    public async Task A_foreign_company_cannot_move_an_application()
    {
        var company = Guid.CreateVersion7();
        var job = _jobs.Publish(company);
        var id = (await Json(await Apply(AsPerson(Guid.CreateVersion7()), job)))
            .GetProperty("id").GetGuid();

        var response = await AsCompany(Guid.CreateVersion7(), Guid.CreateVersion7())
            .PostAsJsonAsync($"/applications/{id}/status", new { status = "reviewing" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>A foreign job delivers nothing instead of giving away that it exists.</summary>
    [Fact]
    public async Task The_list_for_a_job_shows_only_ones_own()
    {
        var company = Guid.CreateVersion7();
        var job = _jobs.Publish(company);
        await Apply(AsPerson(Guid.CreateVersion7()), job);

        var own = await AsCompany(Guid.CreateVersion7(), company)
            .GetAsync($"/jobs/{job}/applications");
        var foreign = await AsCompany(Guid.CreateVersion7(), Guid.CreateVersion7())
            .GetAsync($"/jobs/{job}/applications");

        (await Json(own)).GetArrayLength().Should().Be(1);
        (await Json(foreign)).GetArrayLength().Should().Be(0);
    }

    /// <summary>Applications are read by whoever acts for a company.</summary>
    [Fact]
    public async Task A_private_person_reads_no_list_of_applications()
    {
        var response = await AsPerson(Guid.CreateVersion7())
            .GetAsync($"/jobs/{Guid.CreateVersion7()}/applications");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>One's own list, and only one's own.</summary>
    [Fact]
    public async Task The_own_list_shows_ones_own_applications()
    {
        var subject = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        await Apply(AsPerson(subject), _jobs.Publish(company));
        await Apply(AsPerson(subject), _jobs.Publish(company));
        await Apply(AsPerson(Guid.CreateVersion7()), _jobs.Publish(company));

        var mine = await Json(await AsPerson(subject).GetAsync("/applications/me"));

        mine.GetArrayLength().Should().Be(2);
        mine.EnumerateArray()
            .Should().OnlyContain(entry => entry.GetProperty("subject_id").GetGuid() == subject);
    }

    /// <summary>
    /// Numbers about one's own cases: a convenience, no new information — and
    /// nothing that merges sources.
    /// </summary>
    [Fact]
    public async Task The_numbers_count_only_ones_own_cases()
    {
        var company = Guid.CreateVersion7();
        var job = _jobs.Publish(company);
        var employer = AsCompany(Guid.CreateVersion7(), company);

        var id = (await Json(await Apply(AsPerson(Guid.CreateVersion7()), job)))
            .GetProperty("id").GetGuid();
        await Apply(AsPerson(Guid.CreateVersion7()), job);
        await employer.PostAsJsonAsync(
            $"/applications/{id}/status", new { status = "hired" });

        // A foreign company with an application of its own must not move the numbers.
        await Apply(
            AsPerson(Guid.CreateVersion7()),
            _jobs.Publish(Guid.CreateVersion7()));

        var numbers = await Json(await employer.GetAsync("/companies/me/application-stats"));

        numbers.GetProperty("total").GetInt32().Should().Be(2);
        numbers.GetProperty("by_status").GetProperty("hired").GetInt32().Should().Be(1);
        numbers.GetProperty("by_status").GetProperty("submitted").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// The answer carries exactly two fields — what does not exist cannot go out.
    /// </summary>
    /// <remarks>
    /// ADR-0026 promises: "A test pins the answer's field set — what does not
    /// exist cannot go out (the same strictness as for <c>DraftContext</c>,
    /// ADR-0024)." <b>That test did not exist.</b> Three values were checked, and
    /// a fourth field would have gone out quietly.
    /// <para>
    /// The promise stands exactly against that: an analysis grows not through a
    /// decision but through a "we might as well include that" — and the next step
    /// would be a number per person, which ADR-0022 forbids. A closed set makes
    /// that step visible instead of preventing it: whoever widens it writes it
    /// down here.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_numbers_carry_exactly_two_fields()
    {
        var employer = AsCompany(Guid.CreateVersion7(), Guid.CreateVersion7());

        var numbers = await Json(
            await employer.GetAsync("/companies/me/application-stats"));

        numbers.EnumerateObject().Select(field => field.Name).Should().BeEquivalentTo(
            ["total", "by_status"],
            "ADR-0026 names this set and no larger one");
    }

    /// <summary>Without an active company there are no numbers.</summary>
    /// <remarks>
    /// ADR-0026 names the 403 expressly. It was checked only at
    /// <c>/jobs/{id}/applications</c> — a different endpoint whose green test
    /// says nothing about this one. The numbers are a company's analysis;
    /// whoever acts for none is asking about somebody else's.
    /// </remarks>
    [Fact]
    public async Task Without_a_company_there_are_no_numbers()
    {
        var response = await AsPerson(Guid.CreateVersion7())
            .GetAsync("/companies/me/application-stats");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Nothing works without signing in.</summary>
    [Fact]
    public async Task Without_a_token_it_is_401()
    {
        var response = await _service.CreateClient().GetAsync("/applications/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Without a provider the draft does not stay at "being written".
    /// </summary>
    /// <remarks>
    /// 503 tells the interface that nothing is set up. The state must still
    /// become <c>failed</c>, otherwise the person may not set the text herself —
    /// <c>EditByAuthor</c> is forbidden from <c>generating</c>. The reason is
    /// the sentence the interface recognises (<c>schreibfehler.ts</c>).
    /// </remarks>
    [Fact]
    public async Task Without_a_provider_the_draft_fails_and_is_not_generated()
    {
        var job = _jobs.Publish(Guid.CreateVersion7());
        var browser = AsPerson(Guid.CreateVersion7());

        var created = await browser.PostAsJsonAsync(
            "/applications/drafts", new { job_ids = new[] { job } });

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var draft = (await Json(created))[0];
        var id = draft.GetProperty("id").GetGuid();
        draft.GetProperty("status").GetString().Should().Be("generating");

        var write = await browser.PostAsync($"/applications/drafts/{id}/write", null);

        write.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var afterwards = await Json(await browser.GetAsync($"/applications/drafts/{id}"));

        afterwards.GetProperty("status").GetString().Should().Be("failed");
        afterwards.GetProperty("body").GetString().Should().BeEmpty();
        afterwards.GetProperty("error").GetString().Should().Contain("No writing-help provider");
    }
}
