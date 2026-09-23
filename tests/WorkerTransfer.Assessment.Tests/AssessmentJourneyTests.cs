using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Assessment.Application.Ports;
using WorkerTransfer.Assessment.Infrastructure.Persistence;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Assessment.Tests;

/// <summary>The task, the solution, the feedback — over the wire.</summary>
/// <remarks>
/// The suite goes the whole way, because between a handler and what a browser
/// gets there still lie the settings, the wiring and the field names. A field
/// bound in camelCase never arrives — and every handler suite would stay green.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class AssessmentJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private const string ErasureSecret = "erasure-secret";

    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Recruiter = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Anna = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeLedger _ledger = new();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        // The suites share ONE database, and xUnit promises no order within a
        // collection. Without this emptying every claim about "exactly one row"
        // would depend on who ran before.
        await using (var setup = Context())
        {
            await setup.Database.ExecuteSqlRawAsync("TRUNCATE TABLE assessments, outbox");
        }

        _ledger.Set(Anna, Company, true);

        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:assessment", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Geheimnis", ErasureSecret);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _ledger)));
        });
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------------
    // The set of routes
    // ---------------------------------------------------------------------

    /// <summary>This service has exactly these routes.</summary>
    /// <remarks>
    /// <para><strong>A closed set, and that is the sharpest form of the first
    /// condition.</strong> Whoever adds a seventh route changes this test — and
    /// answers, while doing so, whether it gives out a statement about a human
    /// being (ADR-0042 §1).</para>
    ///
    /// <para>And one that is missing here and is meant to be: a way to
    /// <em>decline</em> a task. Whoever does not want to does nothing
    /// (ADR-0042 §3).</para>
    /// </remarks>
    [Fact]
    public void The_service_has_exactly_these_routes() =>
        _service.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint =>
                $"{Verbs(endpoint)} /{endpoint.RoutePattern.RawText?.TrimStart('/').TrimEnd('/')}")
            .Where(row => row.Contains("assessments", StringComparison.Ordinal)
                            || row.Contains("erasure", StringComparison.Ordinal))
            .Should().BeEquivalentTo(
                "POST /assessments",
                "GET /assessments",
                "POST /assessments/{id:guid}/evaluation",
                "GET /assessments/me",
                "POST /assessments/{id:guid}/submission",
                "GET /assessments/{id:guid}",
                "POST /erasure");

    // ---------------------------------------------------------------------
    // The whole way
    // ---------------------------------------------------------------------

    /// <summary>Set, submit, evaluate — and both see the same.</summary>
    [Fact]
    public async Task The_whole_way_from_the_task_to_the_feedback()
    {
        var id = await SetTask();

        var submitted = await AsPerson(Anna).PostAsJsonAsync(
            Url($"/assessments/{id}/submission"),
            new Dictionary<string, object?>
            {
                ["text"] = "It is in the repository.",
                ["url"] = "https://example.test/solution"
            });

        submitted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(submitted)).GetProperty("state").GetString().Should().Be("submitted");

        var evaluated = await AsCompany().PostAsJsonAsync(
            Url($"/assessments/{id}/evaluation"),
            new Dictionary<string, object?>
            {
                ["outcome"] = "rejected",
                ["text"] = "Cleanly solved, only the error handling is missing."
            });

        evaluated.StatusCode.Should().Be(HttpStatusCode.OK);

        var view = await Json(evaluated);

        view.GetProperty("state").GetString().Should().Be("evaluated");
        view.GetProperty("evaluation").GetProperty("outcome").GetString().Should().Be("rejected");
        view.GetProperty("submission").GetProperty("url").GetString()
            .Should().Be("https://example.test/solution");

        // And the fields travel in snake_case: a field the server bound as
        // `dueAt` would never arrive, and every handler suite would stay green.
        view.GetProperty("due_at").ValueKind.Should().Be(JsonValueKind.String);
        view.GetProperty("hours").GetInt32().Should().Be(4);
    }

    /// <summary>Both sides get the same document — byte for byte.</summary>
    /// <remarks>
    /// <strong>The mechanism behind "the person sees the evaluation"</strong>
    /// (ADR-0042 §2). There is no company view beside a person view, because there
    /// is only one endpoint — two would be the place where the two drift apart,
    /// and only months later, at the next field.
    /// </remarks>
    [Fact]
    public async Task Both_sides_see_the_same_bytes()
    {
        var id = await SetTask();
        await Submit(id);
        await Evaluate(id, "accepted", "Fits. Let us talk further.");

        var companyView = await Raw(await AsCompany().GetAsync(Url($"/assessments/{id}")));
        var personView = await Raw(await AsPerson(Anna).GetAsync(Url($"/assessments/{id}")));

        personView.Should().Be(companyView);
        personView.Should().Contain("Fits. Let us talk further.");
    }

    /// <summary>A rejection without text is 422 — and the rejection does not happen.</summary>
    /// <remarks>
    /// Rejecting and giving a reason are one step. Without this test the promise
    /// would be a paragraph: the server would accept the rejection and leave the
    /// text out, and the person would read a rejection without a word to it.
    /// </remarks>
    [Fact]
    public async Task A_rejection_without_text_is_refused()
    {
        var id = await SetTask();
        await Submit(id);

        var response = await AsCompany().PostAsJsonAsync(
            Url($"/assessments/{id}/evaluation"),
            new Dictionary<string, object?> { ["outcome"] = "rejected" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // And the case stands unchanged: a half-written rejection would be worse
        // than none.
        (await Json(await AsPerson(Anna).GetAsync(Url($"/assessments/{id}"))))
            .GetProperty("state").GetString().Should().Be("submitted");
    }

    /// <summary>After a revocation the person reads on, the company does not.</summary>
    [Fact]
    public async Task After_a_revocation_only_the_person_reads_on()
    {
        var id = await SetTask();
        await Submit(id);
        await Evaluate(id, "rejected", "Just missed the task.");

        _ledger.Set(Anna, Company, false);

        var personResponse = await AsPerson(Anna).GetAsync(Url($"/assessments/{id}"));
        personResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(personResponse)).GetProperty("evaluation").GetProperty("text").GetString()
            .Should().Be("Just missed the task.");

        // The same 404 as "does not exist": four situations, one answer.
        (await AsCompany().GetAsync(Url($"/assessments/{id}"))).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        var list = await Json(await AsCompany().GetAsync(Url("/assessments")));
        list.GetProperty("items").GetArrayLength().Should().Be(0);

        // And no total beside it that would give away the difference (ADR-0026).
        list.TryGetProperty("total", out _).Should().BeFalse();
    }

    /// <summary>Without a release there is nothing — the same 404 as for a stranger.</summary>
    [Fact]
    public async Task Without_a_release_no_task_comes_into_being()
    {
        _ledger.Set(Anna, Company, false);

        var response = await AsCompany().PostAsJsonAsync(Url("/assessments"), Assignment(Anna));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // And the same for an id that does not exist at all.
        (await AsCompany().PostAsJsonAsync(Url("/assessments"), Assignment(Guid.NewGuid())))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>The ledger is asked BEFORE the body.</summary>
    /// <remarks>
    /// The other way round a stranger would learn from the difference between 422
    /// and 404 that their values were fine — and with it that this human being
    /// exists.
    /// </remarks>
    [Fact]
    public async Task The_ledger_stands_before_the_body_check()
    {
        _ledger.Set(Anna, Company, false);

        var broken = Assignment(Anna);
        broken["hours"] = 99;
        broken["title"] = "";

        (await AsCompany().PostAsJsonAsync(Url("/assessments"), broken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>A size outside the limits does not get through.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(40)]
    public async Task An_impossible_size_is_refused(int hours)
    {
        var body = Assignment(Anna);
        body["hours"] = hours;

        var response = await AsCompany().PostAsJsonAsync(Url("/assessments"), body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>And with no size at all there is no task.</summary>
    /// <remarks>
    /// The server makes up no number: the person would take an invented one for
    /// the company's statement (ADR-0042 §3).
    /// </remarks>
    [Fact]
    public async Task Without_a_size_there_is_no_task()
    {
        var body = Assignment(Anna);
        body.Remove("hours");

        var response = await AsCompany().PostAsJsonAsync(Url("/assessments"), body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Json(response)).GetProperty("detail").GetString().Should().Contain("hours");
    }

    /// <summary>Whoever acts for themselves sets no tasks.</summary>
    [Fact]
    public async Task Only_a_company_sets_a_task()
    {
        (await _service.CreateClient().PostAsJsonAsync(Url("/assessments"), Assignment(Anna)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 403 and not 404: a statement about the CALLER gives away nothing about
        // the human being they ask about.
        (await AsPerson(Recruiter).PostAsJsonAsync(Url("/assessments"), Assignment(Anna)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Somebody else's case answers like one that does not exist.</summary>
    [Fact]
    public async Task Somebody_elses_case_does_not_exist()
    {
        var id = await SetTask();

        (await AsPerson(Recruiter).GetAsync(Url($"/assessments/{id}")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await AsPerson(Recruiter).PostAsJsonAsync(
                Url($"/assessments/{id}/submission"),
                new Dictionary<string, object?> { ["text"] = "Somebody else's work." }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>If the ledger is silent, it is 503 — and never an empty list.</summary>
    /// <remarks>
    /// Neither 404 nor an empty result: both would be a statement about a human
    /// being that stems from our outage (ADR-0020 §3).
    /// </remarks>
    [Fact]
    public async Task A_silent_ledger_answers_503()
    {
        await SetTask();
        _ledger.Silent = true;

        (await AsCompany().GetAsync(Url("/assessments"))).StatusCode
            .Should().Be(HttpStatusCode.ServiceUnavailable);

        // The person side stays reachable: it does not ask the ledger.
        (await AsPerson(Anna).GetAsync(Url("/assessments/me"))).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Submitting twice does not work, and neither does evaluating twice.</summary>
    [Fact]
    public async Task Submitted_and_evaluated_once_each()
    {
        var id = await SetTask();
        await Submit(id);

        (await AsPerson(Anna).PostAsJsonAsync(
                Url($"/assessments/{id}/submission"),
                new Dictionary<string, object?> { ["text"] = "And once more." }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        await Evaluate(id, "accepted", "Good.");

        (await AsCompany().PostAsJsonAsync(
                Url($"/assessments/{id}/evaluation"),
                new Dictionary<string, object?>
                {
                    ["outcome"] = "rejected",
                    ["text"] = "On second thoughts, no."
                }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>The task creates a note, and so does the evaluation.</summary>
    /// <remarks>
    /// Two notes, both to the person, both content-free: an id and a kind
    /// (ADR-0025). No company name, no task text, no evaluation — a mail may land
    /// in an inbox at the current employer.
    /// </remarks>
    [Fact]
    public async Task The_person_is_told_twice()
    {
        var id = await SetTask();
        await Submit(id);
        await Evaluate(id, "accepted", "Fits.");

        await using var context = Context();

        var notes = await context.Set<OutboxRow>().ToListAsync();

        notes.Should().HaveCount(2);
        notes.Should().OnlyContain(
            row => row.UserId == Anna && row.Kind == "assessment_update");
    }

    /// <summary>The erasure takes cases and notes — and keeps nothing.</summary>
    /// <remarks>
    /// No retention case, not for the evaluation either: it is no record belonging
    /// to somebody else. One that survived the erasure would be the reference
    /// letter ADR-0042 rules out.
    /// </remarks>
    [Fact]
    public async Task The_erasure_takes_cases_and_notes()
    {
        var id = await SetTask();
        await Submit(id);
        await Evaluate(id, "rejected", "Unfortunately not.");

        var client = _service.CreateClient();
        client.DefaultRequestHeaders.Add("X-Erasure-Secret", ErasureSecret);

        var response = await client.PostAsJsonAsync(
            Url("/erasure"), new Dictionary<string, object?> { ["user_id"] = Anna });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("retained").GetInt32().Should().Be(0);

        await using var context = Context();

        (await context.Cases.CountAsync()).Should().Be(0);
        (await context.Set<OutboxRow>().CountAsync()).Should().Be(0);
    }

    /// <summary>Without the secret nobody erases.</summary>
    [Fact]
    public async Task Without_the_secret_nobody_erases()
    {
        await SetTask();

        var response = await _service.CreateClient().PostAsJsonAsync(
            Url("/erasure"), new Dictionary<string, object?> { ["user_id"] = Anna });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var context = Context();

        (await context.Cases.CountAsync()).Should().Be(1);
    }

    // ---------------------------------------------------------------------

    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static string Verbs(RouteEndpoint endpoint) =>
        string.Join(
            ",",
            endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()
                ?.HttpMethods ?? ["?"]);

    private static Dictionary<string, object?> Assignment(Guid subject) => new()
    {
        ["subject_id"] = subject,
        ["title"] = "Small service",
        ["task"] = "Build a small service that serves a list.",
        ["hours"] = 4,
        ["due_at"] = DateTimeOffset.UtcNow.AddDays(7)
    };

    private async Task<Guid> SetTask()
    {
        var response = await AsCompany().PostAsJsonAsync(Url("/assessments"), Assignment(Anna));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private async Task Submit(Guid id) =>
        (await AsPerson(Anna).PostAsJsonAsync(
            Url($"/assessments/{id}/submission"),
            new Dictionary<string, object?> { ["text"] = "It is in the repository." }))
        .EnsureSuccessStatusCode();

    private async Task Evaluate(Guid id, string outcome, string text) =>
        (await AsCompany().PostAsJsonAsync(
            Url($"/assessments/{id}/evaluation"),
            new Dictionary<string, object?> { ["outcome"] = outcome, ["text"] = text }))
        .EnsureSuccessStatusCode();

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

    private AssessmentDbContext Context()
    {
        var builder = new DbContextOptionsBuilder<AssessmentDbContext>();
        AssessmentDbContextFactory.AtDesignTime(builder, postgres.ConnectionString);

        return new AssessmentDbContext(builder.Options);
    }
}
