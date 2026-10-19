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
using Noelia.Abstractions.Security.Checks;
using WorkerTransfer.Outbox;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Domain.Hits;
using WorkerTransfer.Scout.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Scout.Tests;

/// <summary>Search, save, approach — and be erased.</summary>
/// <remarks>
/// The suite goes the whole way over the wire, because between a handler and
/// what a browser gets there still lie the settings, the wiring and the field
/// names. A field bound in camelCase never arrives — and every handler suite
/// would stay green.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class ScoutJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private const string ErasureSecret = "erasure-secret";

    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Recruiter = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeSearch _search = new();
    private readonly ProbeLedger _ledger = new();
    private readonly ProbeEvidence _evidence = new();
    private readonly ProbeDrafter _drafter = new();
    private readonly ProbeJobs _jobs = new();

    public async Task InitializeAsync()
    {
        // The suites share ONE database, and xUnit admits no order within a
        // collection. Without this truncation every promise about "exactly one
        // row" would depend on who ran before — and that shows only when the
        // order changes some day.
        await using (var setup = Context())
        {
            await setup.Database.ExecuteSqlRawAsync(
                "TRUNCATE TABLE searches, outbox, audit_events");
        }

        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:scout", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Secret", ErasureSecret);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IProfileSearch>(_ => _search));
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _ledger));
                services.Replace(ServiceDescriptor.Scoped<IEvidenceSource>(_ => _evidence));
                services.Replace(ServiceDescriptor.Scoped<IDrafter>(_ => _drafter));
                services.Replace(ServiceDescriptor.Scoped<IJobLookup>(_ => _jobs));
            });
        });
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------------
    // A company is required
    // ---------------------------------------------------------------------

    /// <summary>Without a token: 401. As a person without a company: 403.</summary>
    /// <remarks>
    /// The second half is the one people forget. On a transfer market a human
    /// being without a company is the normal case — and 403 is a statement about
    /// the <em>caller</em> that reveals nothing about those searched for.
    /// </remarks>
    [Fact]
    public async Task Only_whoever_acts_for_a_company_may_search()
    {
        var without = await _service.CreateClient().GetAsync(new Uri("/scout/candidates", UriKind.Relative));
        without.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var person = await AsPerson().GetAsync(new Uri("/scout/candidates", UriKind.Relative));
        person.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var company = await AsCompany().GetAsync(new Uri("/scout/candidates", UriKind.Relative));
        company.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------------
    // The scout per advert (VISION-FRAGEN B1)
    // ---------------------------------------------------------------------

    private static readonly Guid Advert = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");

    private void Stock(Guid company, bool published, params string[] skills) =>
        _jobs.Stock[Advert] = new JobStatement(
            "Kiel", "24103", Attendance.OnSite, company, skills, published);

    [Fact]
    public async Task The_advert_hands_its_skills_to_the_search()
    {
        Stock(Company, published: true, "WIG", "MIG", "Postgres");
        var subject = Guid.NewGuid();
        _search.Stock.Add(Profile(subject, ["WIG"]));
        _ledger.Released.Add((subject, Company));

        var response = await AsCompany().GetAsync(new Uri($"/scout/jobs/{Advert}/candidates", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _search.LastFilter!.NamedWords.Should().BeEquivalentTo(["WIG", "MIG", "PostgreSQL"]);
        _search.LastFilter.Location.Should().BeEmpty("the advert's place is a tick, never a filter");
        (await Json(response)).GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task All_twenty_skills_of_an_advert_reach_the_search()
    {
        var twenty = Enumerable.Range(1, 20).Select(number => $"skill{number}").ToArray();
        Stock(Company, published: true, twenty);

        (await AsCompany().GetAsync(new Uri($"/scout/jobs/{Advert}/candidates", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        _search.LastFilter!.NamedWords.Should().HaveCount(20, "cutting would search half an advert");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task A_draft_or_a_foreign_advert_is_404(bool foreign, bool published)
    {
        Stock(foreign ? Guid.NewGuid() : Company, published, "WIG");

        (await AsCompany().GetAsync(new Uri($"/scout/jobs/{Advert}/candidates", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_missing_advert_is_the_same_404()
    {
        (await AsCompany().GetAsync(new Uri($"/scout/jobs/{Guid.NewGuid()}/candidates", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_advert_without_skills_is_422_and_searches_nobody()
    {
        Stock(Company, published: true);

        var response = await AsCompany().GetAsync(new Uri($"/scout/jobs/{Advert}/candidates", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        _search.LastFilter.Should().BeNull("an empty filter would list everybody who released");
    }

    [Fact]
    public async Task A_silent_job_service_is_503_here()
    {
        _jobs.Silent = true;

        (await AsCompany().GetAsync(new Uri($"/scout/jobs/{Advert}/candidates", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task A_person_gets_403_and_a_stranger_401()
    {
        (await AsPerson().GetAsync(new Uri($"/scout/jobs/{Advert}/candidates", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _service.CreateClient().GetAsync(new Uri($"/scout/jobs/{Advert}/candidates", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------------
    // The ledger
    // ---------------------------------------------------------------------

    /// <summary>The ledger is asked at EVERY call.</summary>
    /// <remarks>
    /// ADR-0013: a revocation has to take effect at the next read. A cache would
    /// be no performance detail here but a breach of the rule — and it would show
    /// only when somebody revoked and is still found.
    /// </remarks>
    [Fact]
    public async Task Every_call_asks_the_ledger_again()
    {
        var subject = Guid.NewGuid();
        _search.Stock.Add(Profile(subject));
        _ledger.Released.Add((subject, Company));

        var before = _ledger.Questions;

        await AsCompany().GetAsync(new Uri("/scout/candidates", UriKind.Relative));
        await AsCompany().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        (_ledger.Questions - before).Should().Be(2);
    }

    /// <summary>A silent ledger is neither a yes nor a no: 503.</summary>
    [Fact]
    public async Task A_silent_ledger_gives_503()
    {
        _search.Stock.Add(Profile(Guid.NewGuid()));
        _ledger.Silent = true;

        var response = await AsCompany().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response)).GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// An answer that does not fit the questions is not guessed.
    /// </summary>
    /// <remarks>
    /// Assigning wrongly would mean showing the profile of the wrong person. 503
    /// and not "as well as it goes".
    /// </remarks>
    [Fact]
    public async Task An_unfitting_ledger_answer_is_not_guessed()
    {
        _search.Stock.Add(Profile(Guid.NewGuid()));
        _search.Stock.Add(Profile(Guid.NewGuid()));
        _ledger.AnswersWithLength = 1;

        var response = await AsCompany().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// A shut search door gives 503 — and expressly no empty list.
    /// </summary>
    /// <remarks>
    /// <para><strong>That is the test for a measured defect.</strong>
    /// profile-service's internal search door answers <c>404</c> instead of
    /// <c>401</c> without the shared secret, so as not to give itself away. The
    /// adapter read that as "not found" and returned an empty page; the endpoint
    /// answered <c>200</c> with <c>items: []</c>, and the UI claimed there was
    /// nobody.</para>
    ///
    /// <para>On the running stack twelve E2E journeys fell as a result — while
    /// the route map stayed green, because it checks status codes and not bodies.
    /// "Not set up" must not look like "there is nobody".</para>
    /// </remarks>
    [Fact]
    public async Task A_shut_search_door_gives_503_and_no_empty_list()
    {
        _search.Shut = true;

        var response = await AsCompany().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        response.StatusCode.Should().Be(
            HttpStatusCode.ServiceUnavailable,
            "an empty hit list would be a statement about people that stems from "
            + "a missing environment variable");
    }

    /// <summary>A silent profile search gives 503 — no empty list.</summary>
    /// <remarks>
    /// An empty hit list would look like "there is nobody", and that would be a
    /// statement about people that stems from our outage.
    /// </remarks>
    [Fact]
    public async Task A_silent_profile_search_gives_503()
    {
        _search.Silent = true;

        var response = await AsCompany().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>Whoever has not released does not show up — and the page stays short.</summary>
    /// <remarks>
    /// Not reloaded (ADR-0020 §4), no total (ADR-0026): both would reveal how many
    /// profiles there are that nobody released.
    /// </remarks>
    [Fact]
    public async Task The_page_shows_only_the_released_and_names_no_total()
    {
        var released = Guid.NewGuid();
        var hidden = Guid.NewGuid();

        _search.Stock.Add(Profile(released));
        _search.Stock.Add(Profile(hidden));
        _ledger.Released.Add((released, Company));

        var response = await AsCompany().GetAsync(new Uri("/scout/candidates", UriKind.Relative));
        var body = await Json(response);

        body.GetProperty("items").EnumerateArray()
            .Select(entry => entry.GetProperty("subject_id").GetGuid())
            .Should().Equal(released);

        body.TryGetProperty("total", out _).Should().BeFalse();
        body.TryGetProperty("count", out _).Should().BeFalse();
        body.EnumerateObject().Select(field => field.Name)
            .Should().BeEquivalentTo("items", "next");
    }

    // ---------------------------------------------------------------------
    // "Your profile was discovered"
    // ---------------------------------------------------------------------

    /// <summary>
    /// Whoever turns up in a search gets a note in the outbox — and it names no
    /// company.
    /// </summary>
    /// <remarks>
    /// <para>ADR-0033: a message instead of a log "who looked at whom". The
    /// outbox stays free of content (ADR-0025) — identifier and kind, nothing
    /// else. That the tenant does <em>not</em> travel along is measured here and
    /// not claimed: the row has no column for it at all.</para>
    ///
    /// <para>And whoever has not released gets no note: they were not discovered
    /// either.</para>
    /// </remarks>
    [Fact]
    public async Task Whoever_is_found_learns_of_it_without_learning_by_whom()
    {
        var released = Guid.NewGuid();
        var hidden = Guid.NewGuid();

        _search.Stock.Add(Profile(released));
        _search.Stock.Add(Profile(hidden));
        _ledger.Released.Add((released, Company));

        await AsCompany().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        var notes = await Outbox();

        notes.Where(row => row.UserId == released)
            .Should().ContainSingle()
            .Which.Kind.Should().Be("profile_discovered");

        notes.Should().NotContain(row => row.UserId == hidden);

        // The row carries identifier and kind. The tenant stands nowhere in it —
        // "a company" is enough, and who is searching is a statement about the
        // company.
        var rowFields = typeof(OutboxRow).GetProperties().Select(property => property.Name);
        rowFields.Should().NotContain("TenantId");
    }

    /// <summary>The same human being gets at most one note per page.</summary>
    [Fact]
    public async Task The_same_human_being_gets_one_note_per_page()
    {
        var subject = Guid.NewGuid();

        // The same human being twice in one page — profile-service cannot deliver
        // that, but the command is to withstand it all the same.
        _search.Stock.Add(Profile(subject));
        _search.Stock.Add(Profile(subject));
        _ledger.Released.Add((subject, Company));

        await AsCompany().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        (await Outbox()).Count(row => row.UserId == subject).Should().Be(1);
    }

    // ---------------------------------------------------------------------
    // Availability (ADR-0041)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Whoever lives further away STAYS in the list — and carries a cross.
    /// </summary>
    /// <remarks>
    /// <para>The core condition of ADR-0041, and it is a statement about the
    /// <em>length</em> of the list: the job does not filter. Both human beings
    /// come back, one with a tick, one with a cross. The company decides, not the
    /// search — and it sees why.</para>
    ///
    /// <para>Along the way the second promise: beside the tick stands no
    /// kilometre figure but the two <em>statements</em>.</para>
    /// </remarks>
    [Fact]
    public async Task Whoever_lives_further_away_stays_in_the_list()
    {
        var near = Guid.NewGuid();
        var far = Guid.NewGuid();
        var job = Guid.NewGuid();

        _search.Stock.Add(Profile(near) with { Location = "Potsdam", Commute = CommuteReadiness.UpTo50 });
        _search.Stock.Add(Profile(far) with { Location = "München", Commute = CommuteReadiness.UpTo10 });
        _ledger.Released.Add((near, Company));
        _ledger.Released.Add((far, Company));
        _jobs.Stock[job] = new JobStatement("Berlin", "", Attendance.OnSite);

        // `stelle` is the query key web/ sends — a wire contract, German on purpose.
        var body = await Json(await AsCompany().GetAsync(
            new Uri($"/scout/candidates?stelle={job}", UriKind.Relative)));

        var entries = body.GetProperty("items").EnumerateArray()
            .ToDictionary(entry => entry.GetProperty("subject_id").GetGuid());

        entries.Should().HaveCount(2, "the job does not filter, it explains");
        entries[near].GetProperty("reach").GetString().Should().Be("reachable");
        entries[far].GetProperty("reach").GetString().Should().Be("further");

        // The statements stand beside it, the distance does not.
        entries[far].GetProperty("reach_commute").GetString().Should().Be("bis_10");
        entries[far].GetProperty("reach_attendance").GetString().Should().Be("vor_ort");

        var text = body.ToString();
        foreach (var forbidden in new[] { "km", "distance", "entfernung", "505" })
        {
            text.Should().NotContain(forbidden, "no kilometre figure on the wire");
        }
    }

    /// <summary>Without a job every hit carries a dash — and no error.</summary>
    [Fact]
    public async Task Without_a_job_every_hit_carries_a_dash()
    {
        var subject = Guid.NewGuid();
        _search.Stock.Add(Profile(subject) with { Commute = CommuteReadiness.UpTo10 });
        _ledger.Released.Add((subject, Company));

        var body = await Json(await AsCompany().GetAsync(
            new Uri("/scout/candidates", UriKind.Relative)));

        body.GetProperty("items").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("reach").GetString().Should().Be("unsaid");

        _jobs.Questions.Should().Be(0, "without a job jobs-service is not asked at all");
    }

    /// <summary>
    /// A silent job costs the tick, not the page of hits.
    /// </summary>
    /// <remarks>
    /// The difference to the ledger and to the profile search, and it is
    /// deliberate: there silence means 503, because an empty list would be a
    /// statement about people. Here only one piece of information ABOUT the list
    /// is missing — the hits themselves are complete, and a 503 would take them
    /// all away (ADR-0041).
    /// </remarks>
    [Fact]
    public async Task A_silent_job_costs_only_the_tick()
    {
        var subject = Guid.NewGuid();
        _search.Stock.Add(Profile(subject) with { Commute = CommuteReadiness.UpTo50 });
        _ledger.Released.Add((subject, Company));
        _jobs.Silent = true;

        var response = await AsCompany().GetAsync(
            new Uri($"/scout/candidates?stelle={Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("items").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("reach").GetString().Should().Be("unsaid");
    }

    /// <summary>The job is asked ONCE per page, not per row.</summary>
    /// <remarks>
    /// It is the same for all hits. Asking once per row would be the same mistake
    /// the batch question to the ledger fixed — only towards another service.
    /// </remarks>
    [Fact]
    public async Task The_job_is_asked_once_per_page()
    {
        var job = Guid.NewGuid();
        _jobs.Stock[job] = new JobStatement("Berlin", "", Attendance.OnSite);

        foreach (var _ in Enumerable.Range(0, 5))
        {
            var subject = Guid.NewGuid();
            _search.Stock.Add(Profile(subject));
            _ledger.Released.Add((subject, Company));
        }

        await AsCompany().GetAsync(
            new Uri($"/scout/candidates?stelle={job}", UriKind.Relative));

        _jobs.Questions.Should().Be(1);
    }

    // ---------------------------------------------------------------------
    // The saved question
    // ---------------------------------------------------------------------

    /// <summary>What is saved is the question — and demonstrably no result.</summary>
    /// <remarks>
    /// The counter-probe lies in the second half: there is a search, there is a
    /// hit, the search is saved — and the hit does not stand in the saved search.
    /// A saved result about people would go stale against a revocation (ADR-0036
    /// decision 4).
    /// </remarks>
    [Fact]
    public async Task The_question_is_saved_never_the_result()
    {
        var subject = Guid.NewGuid();
        _search.Stock.Add(Profile(subject, ["Go"]));
        _ledger.Released.Add((subject, Company));

        var found = await AsCompany().GetAsync(
            new Uri("/scout/candidates?skill=Go", UriKind.Relative));
        (await Json(found)).GetProperty("items").GetArrayLength().Should().Be(1);

        var created = await AsCompany().PostAsJsonAsync(
            new Uri("/scout/searches", UriKind.Relative),
            new { name = "Go in Berlin", skills = new[] { "Go" }, location = "Berlin", remote = true });

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var text = await created.Content.ReadAsStringAsync();

        text.Should().NotContain(subject.ToString(), "a saved search holds no hit");
        text.Should().Contain("Go");

        var read = await Json(
            await AsCompany().GetAsync(new Uri("/scout/searches", UriKind.Relative)));

        read.GetProperty("items").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("name").GetString().Should().Be("Go in Berlin");

        // And nothing else stands in the table either.
        await using var context = Context();
        var row = await context.Searches.SingleAsync();
        row.Skills.Should().Equal("Go");
        row.SubjectId.Should().Be(Recruiter);
    }

    /// <summary>Somebody else's search does not exist for me.</summary>
    [Fact]
    public async Task Somebody_elses_search_does_not_exist_for_me()
    {
        var created = await AsCompany().PostAsJsonAsync(
            new Uri("/scout/searches", UriKind.Relative),
            new { name = "Mine", skills = new[] { "Rust" } });

        var id = (await Json(created)).GetProperty("id").GetGuid();

        var stranger = With(TokenShape.Company(Guid.NewGuid(), Company));

        var removed = await stranger.DeleteAsync(new Uri($"/scout/searches/{id}", UriKind.Relative));
        removed.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var own = await AsCompany().DeleteAsync(new Uri($"/scout/searches/{id}", UriKind.Relative));
        own.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>A name is required, and too many words are too many.</summary>
    [Fact]
    public async Task An_unusable_request_gives_422()
    {
        var withoutName = await AsCompany().PostAsJsonAsync(
            new Uri("/scout/searches", UriKind.Relative), new { name = "", skills = new[] { "Go" } });

        withoutName.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var tooMany = await AsCompany().GetAsync(new Uri(
            "/scout/candidates?" + string.Join(
                '&', Enumerable.Range(0, 11).Select(number => $"skill=word{number}")),
            UriKind.Relative));

        tooMany.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ---------------------------------------------------------------------
    // The approach
    // ---------------------------------------------------------------------

    /// <summary>The draft comes back — and nothing of it stays here.</summary>
    /// <remarks>
    /// The promise is an absence, and it is measured as one: after the draft
    /// there is not one row more in the database than before — no note that
    /// somebody was to be approached, and certainly no message to the approached
    /// person (ADR-0036 condition 4, ADR-0024).
    /// </remarks>
    [Fact]
    public async Task An_approach_is_drafted_and_nothing_of_it_stays()
    {
        var subject = Guid.NewGuid();
        _search.Stock.Add(Profile(subject, ["Go"]));
        _ledger.Released.Add((subject, Company));

        var before = (await Outbox()).Count;

        var response = await AsCompany().PostAsJsonAsync(
            new Uri($"/scout/candidates/{subject}/draft", UriKind.Relative),
            new { skills = new[] { "Go" }, wish = "short" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await Json(response);
        body.GetProperty("draft").GetString().Should().NotBeNullOrEmpty();
        body.EnumerateObject().Select(field => field.Name).Should().Equal("draft");

        (await Outbox()).Count.Should().Be(
            before, "a draft sends nobody anything — not even a notification");

        await using var context = Context();
        (await context.Searches.CountAsync()).Should().Be(0, "a draft is not saved");
    }

    /// <summary>Whoever has released nothing is no contact person: 404.</summary>
    /// <remarks>
    /// Hidden and not present answer alike — byte for byte apart from the
    /// correlation id. A difference would say whether this human being is here.
    /// </remarks>
    [Fact]
    public async Task Without_a_release_there_is_nobody_to_approach()
    {
        var hidden = Guid.NewGuid();
        _search.Stock.Add(Profile(hidden, ["Go"]));

        var response = await AsCompany().PostAsJsonAsync(
            new Uri($"/scout/candidates/{hidden}/draft", UriKind.Relative), new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var doesNotExist = await AsCompany().PostAsJsonAsync(
            new Uri($"/scout/candidates/{Guid.NewGuid()}/draft", UriKind.Relative), new { });

        doesNotExist.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _drafter.Last.Should().BeNull("without a release the model sees nothing at all");

        (await Json(response)).GetProperty("detail").GetString().Should().Be(
            (await Json(doesNotExist)).GetProperty("detail").GetString(),
            "hidden and not present must not be told apart");
    }

    /// <summary>Without a provider the service says so: 503, no template.</summary>
    [Fact]
    public async Task Without_a_provider_there_is_503_and_no_template()
    {
        var subject = Guid.NewGuid();
        _search.Stock.Add(Profile(subject, ["Go"]));
        _ledger.Released.Add((subject, Company));
        _drafter.Missing = true;

        var response = await AsCompany().PostAsJsonAsync(
            new Uri($"/scout/candidates/{subject}/draft", UriKind.Relative), new { });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("\"draft\"");
    }

    // ---------------------------------------------------------------------
    // Erasure
    // ---------------------------------------------------------------------

    /// <summary>The cascade takes searches AND notes with it.</summary>
    /// <remarks>
    /// Both, and both are personal: the saved search belongs to the human being
    /// who saved it; the outbox row is about a human being. Whoever erases only
    /// the one has kept the promise of ADR-0027 by half — and nobody sees it.
    /// </remarks>
    [Fact]
    public async Task Erasure_takes_searches_and_notes_with_it()
    {
        var found = Guid.NewGuid();
        _search.Stock.Add(Profile(found));
        _ledger.Released.Add((found, Company));

        await AsCompany().GetAsync(new Uri("/scout/candidates", UriKind.Relative));
        await AsCompany().PostAsJsonAsync(
            new Uri("/scout/searches", UriKind.Relative),
            new { name = "does not stay", skills = new[] { "Go" } });

        (await Outbox()).Should().Contain(row => row.UserId == found);

        // The human being who was found.
        (await Erase(found)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Outbox()).Should().NotContain(row => row.UserId == found);

        // And the human being who searched.
        (await Erase(Recruiter)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var context = Context();
        (await context.Searches.CountAsync(row => row.SubjectId == Recruiter)).Should().Be(0);
    }

    /// <summary>Without the secret nobody erases.</summary>
    [Fact]
    public async Task Without_the_secret_nobody_erases()
    {
        var response = await _service.CreateClient().PostAsJsonAsync(
            new Uri("/erasure", UriKind.Relative), new { user_id = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------------
    // The evidence report (ADR-0044)
    // ---------------------------------------------------------------------

    /// <summary>The registered seam and vocabulary checks pass on this service.</summary>
    /// <remarks>
    /// <para>The seam check names the approach context's fields as STRINGS in
    /// the composition root, and the vocabulary check reads this service's names
    /// by syllable. A rename the build accepts can turn either red — and before
    /// this test only <c>make evidence-check</c> against the running stack
    /// would have said so. Here the checks run as the service registered them.</para>
    /// </remarks>
    [Theory]
    [InlineData("wt.ki.naht")]
    [InlineData("wt.ki.keine-zahl")]
    public async Task The_registered_evidence_check_passes(string id)
    {
        var check = _service.Services.GetServices<ISecurityCheck>()
            .Should().ContainSingle(candidate => candidate.Id == id).Subject;

        var finding = await check.RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Pass, finding.Summary);
    }

    // ---------------------------------------------------------------------

    private HttpClient AsPerson() => With(TokenShape.Person(Recruiter));

    private HttpClient AsCompany() => With(TokenShape.Company(Recruiter, Company));

    private HttpClient With(string token)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    /// <summary>The erasure leaves exactly one row behind: THAT it ran.</summary>
    /// <remarks>
    /// Measured in the database, not in the answer: a receipt can say "done"
    /// over an empty trail. Erased twice, because the cascade delivers at least
    /// once — a second delivery must not record a second erasure (ADR-0012,
    /// ADR-0027).
    /// </remarks>
    [Fact]
    public async Task The_erasure_leaves_exactly_one_audit_row()
    {
        await AsCompany().PostAsJsonAsync(
            new Uri("/scout/searches", UriKind.Relative),
            new { name = "does not stay", skills = new[] { "Go" } });

        (await Erase(Recruiter)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Erase(Recruiter)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var context = Context();
        (await context.Searches.CountAsync(row => row.SubjectId == Recruiter)).Should().Be(0);
        (await context.AuditEntries.CountAsync(
                row => row.TargetId == Recruiter && row.Action == "erased"))
            .Should().Be(1, "the trail says once that the erasure ran, and nothing about what it deleted");
    }

    private Task<HttpResponseMessage> Erase(Guid subject)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", ErasureSecret);

        return browser.PostAsJsonAsync(new Uri("/erasure", UriKind.Relative), new { user_id = subject });
    }

    private ScoutDbContext Context() =>
        new((DbContextOptions<ScoutDbContext>)ScoutDbContextFactory.Configure(
            new DbContextOptionsBuilder<ScoutDbContext>(),
            ScoutDbContextFactory.DataSource(postgres.ConnectionString)).Options);

    private async Task<List<OutboxRow>> Outbox()
    {
        await using var context = Context();

        return await context.Set<OutboxRow>().ToListAsync();
    }

    private static ProfileHit Profile(Guid subject, string[]? named = null) =>
        new(new SubjectId(subject), "Developer", "I weld.", "Berlin", true, named ?? ["Go"]);

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
