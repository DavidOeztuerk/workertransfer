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
using Noelia.Core.Identity;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Conversations;
using WorkerTransfer.Advisor.Infrastructure.Persistence;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Advisor.Tests;

/// <summary>The mandate, three stages, an agreement — and being erased.</summary>
/// <remarks>
/// The suite goes the whole way over the wire, because between a handler and
/// what a browser gets there are still the settings, the wiring and the field
/// names. A field bound in camelCase never arrives — and every handler suite
/// would stay green.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class AdvisorJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private const string ErasureSecret = "erasure-secret";

    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Recruiter = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Anna = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeLedger _ledger = new();
    private readonly ProbeCompanies _companies = new();
    private readonly ProbePeople _people = new();
    private readonly ProbeHandover _handover = new();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        // The suites share ONE database, and xUnit admits no order within a
        // collection. Without this emptying, every promise about "exactly one
        // row" would depend on who ran before.
        await using (var setup = Context())
        {
            await setup.Database.ExecuteSqlRawAsync(
                "TRUNCATE TABLE mandates, conversations, outbox");
        }

        _ledger.Self = Anna;

        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:advisor", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Geheimnis", ErasureSecret);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _ledger));
                services.Replace(ServiceDescriptor.Scoped<ICompanyLookup>(_ => _companies));
                services.Replace(ServiceDescriptor.Scoped<IPersonLookup>(_ => _people));
                services.Replace(ServiceDescriptor.Scoped<ICaseHandover>(_ => _handover));
            });
        });
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------------
    // The mandate
    // ---------------------------------------------------------------------

    /// <summary>An empty mandate is complete — and answers 200, not 404.</summary>
    /// <remarks>
    /// "Nothing said" IS a state. A 404 would force the UI to invent a default,
    /// and the danger is that it invents the wrong one.
    /// </remarks>
    [Fact]
    public async Task Whoever_said_nothing_gets_an_empty_mandate()
    {
        var response = await AsPerson(Anna).GetAsync(Target("/advisor/me/mandate"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var mandate = await Json(response);

        mandate.GetProperty("entry_month").ValueKind.Should().Be(JsonValueKind.Null);
        mandate.GetProperty("excluded_domains").GetArrayLength().Should().Be(0);
    }

    /// <summary>The four values travel there and back — in snake_case.</summary>
    /// <remarks>
    /// The reason for this suite: a field the server binds as <c>salaryMin</c>
    /// while the UI sends <c>salary_min</c> never arrives — and every handler
    /// suite would stay green.
    /// </remarks>
    [Fact]
    public async Task The_four_values_travel_there_and_back()
    {
        var written = await AsPerson(Anna).PutAsJsonAsync(
            Target("/advisor/me/mandate"),
            new Dictionary<string, object?>
            {
                ["entry_month"] = "2026-11",
                ["salary_min"] = 4000,
                ["salary_max"] = 5200,
                ["workload_percent"] = 80,
                ["excluded_domains"] = new[] { "Employer.TEST" }
            });

        written.StatusCode.Should().Be(HttpStatusCode.OK);

        var read = await Json(await AsPerson(Anna).GetAsync(Target("/advisor/me/mandate")));

        read.GetProperty("entry_month").GetString().Should().Be("2026-11");
        read.GetProperty("salary_min").GetInt32().Should().Be(4000);
        read.GetProperty("salary_max").GetInt32().Should().Be(5200);
        read.GetProperty("workload_percent").GetInt32().Should().Be(80);

        // Stored in lower case: a domain is not a case-sensitive string, and a
        // comparison that did not know that would let an exclusion miss.
        read.GetProperty("excluded_domains")[0].GetString().Should().Be("employer.test");
    }

    /// <summary>An impossible value is named, not stored.</summary>
    [Theory]
    [InlineData("workload_percent", 5)]
    [InlineData("workload_percent", 120)]
    public async Task An_impossible_workload_is_refused(string field, int value)
    {
        var response = await AsPerson(Anna).PutAsJsonAsync(
            Target("/advisor/me/mandate"),
            new Dictionary<string, object?> { [field] = value });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // The RULE, never the value: a salary or a workload in a message would
        // end up in the log.
        (await Json(response)).GetProperty("detail").GetString()
            .Should().NotContain(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // ---------------------------------------------------------------------
    // The three stages
    // ---------------------------------------------------------------------

    /// <summary>Only somebody acting for a company may open — and only with a release.</summary>
    [Fact]
    public async Task Only_a_company_with_a_release_may_open()
    {
        var anonymous = await _service.CreateClient().PostAsJsonAsync(
            Target("/advisor/conversations"), Opening());
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var person = await AsPerson(Recruiter).PostAsJsonAsync(
            Target("/advisor/conversations"), Opening());
        person.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A company, but nothing released: the same 404 as "does not exist".
        var withoutRelease = await AsCompany().PostAsJsonAsync(
            Target("/advisor/conversations"), Opening());
        withoutRelease.StatusCode.Should().Be(HttpStatusCode.NotFound);

        _ledger.SetStage(Anna, Company, Stage.Profile);

        var withRelease = await AsCompany().PostAsJsonAsync(
            Target("/advisor/conversations"), Opening());
        withRelease.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>The person learns of it — content-free, through the outbox.</summary>
    /// <remarks>
    /// An id and a kind, no more fits in (ADR-0025). No company name: a mail
    /// lands in an inbox, and that inbox may be the current employer's.
    /// </remarks>
    [Fact]
    public async Task An_opened_conversation_leaves_an_outbox_entry()
    {
        _ledger.SetStage(Anna, Company, Stage.Profile);

        await AsCompany().PostAsJsonAsync(Target("/advisor/conversations"), Opening());

        await using var context = Context();

        var rows = await context.Set<OutboxRow>().ToListAsync();

        rows.Should().ContainSingle()
            .Which.Should().Match<OutboxRow>(
                row => row.UserId == Anna && row.Kind == "advisor_conversation");
    }

    /// <summary>Opening a second time creates no second conversation.</summary>
    [Fact]
    public async Task A_second_opening_returns_the_same_conversation()
    {
        _ledger.SetStage(Anna, Company, Stage.Profile);

        var first = await AsCompany().PostAsJsonAsync(
            Target("/advisor/conversations"), Opening());
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await AsCompany().PostAsJsonAsync(
            Target("/advisor/conversations"), Opening());
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        (await Json(second)).GetProperty("id").GetGuid()
            .Should().Be((await Json(first)).GetProperty("id").GetGuid());
    }

    /// <summary>
    /// The whole journey: stage 1, stage 2, stage 3 — and at every stage the
    /// company sees exactly what is released, and not one field more.
    /// </summary>
    /// <remarks>
    /// <strong>Measured on the JSON and not on the object.</strong> That is the
    /// promise: not <c>"salary_min": null</c>, but no key at all. A nulled field
    /// says "there is a salary here, you just do not see it" — and with that,
    /// that there would be something to see.
    /// </remarks>
    [Fact]
    public async Task Every_stage_shows_exactly_what_is_released()
    {
        await WriteMandate();
        _people.Known[Anna] = new PersonCard("Anna Example", "anna@example.test");
        _ledger.SetStage(Anna, Company, Stage.Profile);

        var conversation = await OpenConversation();

        var stage1 = await Raw(await AsCompany().GetAsync(Target($"/advisor/conversations/{conversation}")));
        stage1.Should().Contain("\"entry_month\"");
        stage1.Should().NotContain("salary_min");
        stage1.Should().NotContain("\"name\"");

        await AsPerson(Anna).PostAsJsonAsync(
            Target($"/advisor/me/conversations/{conversation}/advance"),
            new Dictionary<string, object?> { ["stage"] = 2 });

        var stage2 = await Raw(await AsCompany().GetAsync(Target($"/advisor/conversations/{conversation}")));
        stage2.Should().Contain("\"salary_min\"");
        stage2.Should().NotContain("\"name\"");

        await AsPerson(Anna).PostAsJsonAsync(
            Target($"/advisor/me/conversations/{conversation}/advance"),
            new Dictionary<string, object?> { ["stage"] = 3 });

        var stage3 = await Raw(await AsCompany().GetAsync(Target($"/advisor/conversations/{conversation}")));
        stage3.Should().Contain("\"name\"");
        stage3.Should().Contain("anna@example.test");
    }

    /// <summary>An advance without a stage releases stage 1 — not nothing.</summary>
    /// <remarks>
    /// The endpoint's default is the smallest stage, so that a release is never
    /// the widest by accident. It is also never the empty one: the default must
    /// be <c>Stage.Profile</c> and not the keyword <c>default</c>, which is
    /// <c>Stage.None</c> and would answer 200 with stage 0 while granting
    /// nothing. Measured: after the rename to English the endpoint did exactly
    /// that, and no test noticed.
    /// </remarks>
    [Fact]
    public async Task An_advance_without_a_stage_releases_stage_one()
    {
        // Publicly visible, so the company can open without a per-company release.
        _ledger.SetPublic(Anna, true);

        var conversation = await OpenConversation();

        var response = await AsPerson(Anna).PostAsync(
            Target($"/advisor/me/conversations/{conversation}/advance"), null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("stage").GetInt32().Should().Be(1);

        var tenant = new TenantId(Company);

        _ledger.Grants.Should().BeEquivalentTo(
            [StageCapabilities.Profile(tenant), StageCapabilities.Market(tenant)]);
    }

    /// <summary>A revocation takes effect on the next request — and completely.</summary>
    /// <remarks>
    /// At stage 0 the conversation drops out of the company's list and answers
    /// 404 when asked directly. An empty row in it would be the "locked" hint in
    /// list form: it would say that this human being exists and that they have
    /// withdrawn.
    /// </remarks>
    [Fact]
    public async Task A_revocation_takes_effect_on_the_next_request()
    {
        _ledger.SetStage(Anna, Company, Stage.Person);

        var conversation = await OpenConversation();

        (await AsCompany().GetAsync(Target($"/advisor/conversations/{conversation}")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var withdrawn = await AsPerson(Anna).PostAsJsonAsync(
            Target($"/advisor/me/conversations/{conversation}/withdraw"),
            new Dictionary<string, object?> { ["stage"] = 1 });

        withdrawn.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(withdrawn)).GetProperty("stage").GetInt32().Should().Be(0);

        (await AsCompany().GetAsync(Target($"/advisor/conversations/{conversation}")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await Json(await AsCompany().GetAsync(Target("/advisor/conversations")));
        list.GetProperty("items").GetArrayLength().Should().Be(0);

        // The person keeps it and can release again — a revocation that took
        // the row with it would be a one-way street.
        var mine = await Json(await AsPerson(Anna).GetAsync(Target("/advisor/me/conversations")));
        mine.GetProperty("items").GetArrayLength().Should().Be(1);
        mine.GetProperty("items")[0].GetProperty("stage").GetInt32().Should().Be(0);
    }

    /// <summary>A stage of another human being's conversation does not exist.</summary>
    [Fact]
    public async Task Other_peoples_conversations_do_not_exist()
    {
        _ledger.SetStage(Anna, Company, Stage.Profile);

        var conversation = await OpenConversation();

        var response = await AsPerson(Recruiter).PostAsJsonAsync(
            Target($"/advisor/me/conversations/{conversation}/advance"),
            new Dictionary<string, object?> { ["stage"] = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>A silent ledger is neither a yes nor a no: 503.</summary>
    [Fact]
    public async Task A_silent_ledger_gives_503()
    {
        _ledger.SetStage(Anna, Company, Stage.Profile);
        await OpenConversation();

        _ledger.Silent = true;

        var response = await AsCompany().GetAsync(Target("/advisor/conversations"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response)).GetProperty("correlationId").GetString()
            .Should().NotBeNullOrEmpty();
    }

    // ---------------------------------------------------------------------
    // The current employer
    // ---------------------------------------------------------------------

    /// <summary>
    /// An excluded company gets the same 404 as a stranger — and the person
    /// loses "for all companies" with it.
    /// </summary>
    /// <remarks>
    /// <para><strong>The acceptance from PBI-4, over the wire.</strong> The
    /// ledger knows no denial, so the mode "everyone" no longer exists as soon as
    /// somebody names a company. Afterwards the scout finds this person only for
    /// companies they released to one by one.</para>
    /// </remarks>
    [Fact]
    public async Task An_excluded_company_does_not_get_into_a_conversation()
    {
        _companies.Domains[Company] = "employer.test";
        _ledger.SetPublic(Anna, true);

        var written = await AsPerson(Anna).PutAsJsonAsync(
            Target("/advisor/me/mandate"),
            new Dictionary<string, object?>
            {
                ["excluded_domains"] = new[] { "employer.test" }
            });

        written.StatusCode.Should().Be(HttpStatusCode.OK);

        _ledger.Revocations.Should().ContainSingle()
            .Which.Should().Be(StageCapabilities.ProfilePublic);

        // Even with a release of its own this company does not get in: the
        // exclusion is the person's explicit veto.
        _ledger.SetStage(Anna, Company, Stage.Profile);

        var response = await AsCompany().PostAsJsonAsync(
            Target("/advisor/conversations"), Opening());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------------
    // The agreement
    // ---------------------------------------------------------------------

    /// <summary>Agree, hand over — and the case comes about elsewhere.</summary>
    [Fact]
    public async Task An_agreement_becomes_a_case()
    {
        _ledger.SetStage(Anna, Company, Stage.Person);

        var conversation = await OpenConversation();

        var tooEarly = await AsCompany().PostAsync(
            Target($"/advisor/conversations/{conversation}/hand-over"), null);
        tooEarly.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var agreement = await AsPerson(Anna).PostAsync(
            Target($"/advisor/me/conversations/{conversation}/agree"), null);
        agreement.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterAgreement = await Json(agreement);
        afterAgreement.GetProperty("state").GetString().Should().Be("agreed");

        // AND THE STAGE IN IT IS RIGHT. It once said 0 here — the endpoint
        // built the answer with `Stage.None`, because this step does not change
        // the stage. The answer thereby claimed "nothing released" immediately
        // after somebody agreed. It was not visible, because the page reloads
        // afterwards anyway — an answer that lies and that nobody reads is the
        // one somebody believes later.
        afterAgreement.GetProperty("stage").GetInt32().Should().Be(3);

        var handover = await AsCompany().PostAsync(
            Target($"/advisor/conversations/{conversation}/hand-over"), null);
        handover.StatusCode.Should().Be(HttpStatusCode.OK);

        (await Json(handover)).GetProperty("transfer_id").GetGuid()
            .Should().Be(_handover.Case);

        _handover.HandedOver.Should().Equal(Anna);
    }

    /// <summary>If transfer-service refuses, that is 409 — and not 500.</summary>
    /// <remarks>
    /// A refusal from there is a statement about the conditions of this case
    /// (market release, approachability) and not a fault. Silence, on the other
    /// hand, would be 503: "cannot" and "we do not know" are two different
    /// things.
    /// </remarks>
    [Fact]
    public async Task A_refused_handover_is_409()
    {
        _ledger.SetStage(Anna, Company, Stage.Person);
        _handover.Refuses = true;

        var conversation = await OpenConversation();

        await AsPerson(Anna).PostAsync(
            Target($"/advisor/me/conversations/{conversation}/agree"), null);

        var response = await AsCompany().PostAsync(
            Target($"/advisor/conversations/{conversation}/hand-over"), null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Both sides may end it.</summary>
    [Fact]
    public async Task Both_sides_may_end_it()
    {
        _ledger.SetStage(Anna, Company, Stage.Profile);

        var conversation = await OpenConversation();

        var ended = await AsPerson(Anna).PostAsync(
            Target($"/advisor/me/conversations/{conversation}/end"), null);

        ended.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterTheEnd = await Json(ended);
        afterTheEnd.GetProperty("state").GetString().Should().Be("ended");

        // An end is not a withdrawal: the stage stands where it stood. Whoever
        // also wants visibility back revokes it — and that is a second,
        // deliberate step.
        afterTheEnd.GetProperty("stage").GetInt32().Should().Be(1);

        var again = await AsCompany().PostAsync(
            Target($"/advisor/conversations/{conversation}/end"), null);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---------------------------------------------------------------------
    // The erasure
    // ---------------------------------------------------------------------

    /// <summary>Without the secret nothing is erased.</summary>
    [Fact]
    public async Task Without_the_secret_nobody_erases()
    {
        var response = await _service.CreateClient().PostAsJsonAsync(
            Target("/erasure"), new Dictionary<string, object?> { ["user_id"] = Anna });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Mandate, conversations and outbox entries fall together.</summary>
    /// <remarks>
    /// All three: whoever deletes only the conversations leaves the mandate with
    /// entry month and salary range behind — and the promise of ADR-0027 is then
    /// half kept without anybody noticing.
    /// </remarks>
    [Fact]
    public async Task The_erasure_takes_mandate_conversations_and_outbox_entries()
    {
        await WriteMandate();
        _ledger.SetStage(Anna, Company, Stage.Profile);
        await OpenConversation();

        var client = _service.CreateClient();
        client.DefaultRequestHeaders.Add("X-Erasure-Secret", ErasureSecret);

        var response = await client.PostAsJsonAsync(
            Target("/erasure"), new Dictionary<string, object?> { ["user_id"] = Anna });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // No retention case: nothing here belongs to somebody else.
        (await Json(response)).GetProperty("retained").GetInt32().Should().Be(0);

        await using var context = Context();

        (await context.Mandates.CountAsync()).Should().Be(0);
        (await context.Conversations.CountAsync()).Should().Be(0);
        (await context.Set<OutboxRow>().CountAsync()).Should().Be(0);
    }

    // ---------------------------------------------------------------------

    private static Uri Target(string path) => new(path, UriKind.Relative);

    private static Dictionary<string, object?> Opening() => new()
    {
        ["subject_id"] = Anna,
        ["note"] = "We are looking for someone for distributed systems."
    };

    private async Task<Guid> OpenConversation()
    {
        var response = await AsCompany().PostAsJsonAsync(
            Target("/advisor/conversations"), Opening());

        response.EnsureSuccessStatusCode();

        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private async Task WriteMandate() =>
        (await AsPerson(Anna).PutAsJsonAsync(
            Target("/advisor/me/mandate"),
            new Dictionary<string, object?>
            {
                ["entry_month"] = "2026-11",
                ["salary_min"] = 4000,
                ["salary_max"] = 5200,
                ["workload_percent"] = 80
            })).EnsureSuccessStatusCode();

    private HttpClient AsPerson(Guid subject) => With(TokenShape.Person(subject));

    private HttpClient AsCompany() => With(TokenShape.Company(Recruiter, Company));

    private HttpClient With(string token)
    {
        var client = _service.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static Task<string> Raw(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync();

    private AdvisorDbContext Context()
    {
        var builder = new DbContextOptionsBuilder<AdvisorDbContext>();
        AdvisorDbContextFactory.AtDesignTime(builder, postgres.ConnectionString);

        return new AdvisorDbContext(builder.Options);
    }
}
