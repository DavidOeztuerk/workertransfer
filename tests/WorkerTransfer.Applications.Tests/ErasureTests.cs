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
using WorkerTransfer.Applications.Application.Erasure;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Infrastructure.Erasure;
using WorkerTransfer.Applications.Infrastructure.Persistence;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Applications.Tests;

/// <summary>The erasure — and the switch that stands at off.</summary>
[Collection(PostgresCollection.Name)]
public class ErasureTests(Postgres postgres) : IAsyncLifetime
{
    private const string ErasureSecret = "erasure-secret";

    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeLedger _ledger = new();
    private readonly ProbeJobs _jobs = new();
    private readonly ProbeMembers _members = new();

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
            host.UseSetting("Erasure:Geheimnis", ErasureSecret);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(
                    ServiceDescriptor.Scoped<IConsentWriter>(_ => _ledger));
                services.Replace(ServiceDescriptor.Scoped<IJobLookup>(_ => _jobs));
                services.Replace(ServiceDescriptor.Scoped<IDelivery, ProbeDelivery>());
                services.Replace(ServiceDescriptor.Scoped<ICompanyMemberLookup>(_ => _members));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// <strong>This service's load-bearing promise.</strong>
    /// </summary>
    /// <remarks>
    /// It is not the erasure that must justify itself but the keeping. If this
    /// test falls, the default has tipped over — and nobody is promised a
    /// complete erasure any more.
    /// </remarks>
    [Fact]
    public void The_switch_stands_at_off()
    {
        Retention.KeepHired.Should().BeFalse();
    }

    /// <summary>
    /// "Done" means done — including the application through which somebody was
    /// hired.
    /// </summary>
    /// <remarks>
    /// Said openly: it disappears from the company's list too. That is intended,
    /// and <c>/delete-account</c> says so before the button.
    /// </remarks>
    [Fact]
    public async Task The_default_also_erases_the_hired_application()
    {
        var subject = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        var hired = await Application(subject, company, "hired");
        var running = await Application(subject, company, "submitted");

        var response = await Erase(subject);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var receipt = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        receipt.GetProperty("retained").GetInt32().Should().Be(0);

        await using var context = postgres.Context();
        (await context.Applications.AnyAsync(row => row.Id == hired))
            .Should().BeFalse();
        (await context.Applications.AnyAsync(row => row.Id == running))
            .Should().BeFalse();
    }

    /// <summary>
    /// The flipped switch covers <strong>exactly one</strong> row class:
    /// <c>status = 'hired'</c>.
    /// </summary>
    /// <remarks>
    /// No extension to <c>rejected</c> — a rejected application justifies
    /// nothing — and no "running case" as a rubber word. The switch travels in as
    /// a parameter, so exactly this can be checked without touching the default.
    /// </remarks>
    [Fact]
    public async Task The_flipped_switch_covers_exactly_hired()
    {
        var subject = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        var hired = await Application(subject, company, "hired");
        var rejected = await Application(subject, company, "rejected");
        var withdrawn = await Application(subject, company, "withdrawn");
        var running = await Application(subject, company, "submitted");

        await using var context = postgres.Context();
        var ledger = new EfErasureLedger(context);

        var kept = await ledger.DeleteAsync(
            new SubjectId(subject), keepHired: true);

        kept.Should().Be(1);

        var remaining = await context.Applications
            .Where(row => row.SubjectId == subject)
            .Select(row => row.Id)
            .ToListAsync();

        remaining.Should().Equal(hired);
        remaining.Should().NotContain([rejected, withdrawn, running]);
    }

    /// <summary>A pending notification to an account that no longer exists.</summary>
    [Fact]
    public async Task The_erasure_clears_this_persons_outbox()
    {
        var subject = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        var job = _jobs.Publish(company);

        _members.For(new TenantId(company), new SubjectId(Guid.CreateVersion7()));

        var person = With(TokenShape.Person(subject));

        var created = JsonDocument
            .Parse(await (await person.PostAsJsonAsync("/applications", new
            {
                job_id = job, message = "Hello.", shares_resume = false,
                shares_portfolio = false
            })).Content.ReadAsStringAsync()).RootElement;

        await With(TokenShape.Company(Guid.CreateVersion7(), company)).PostAsJsonAsync(
            $"/applications/{created.GetProperty("id").GetGuid()}/status",
            new { status = "reviewing" });

        await using (var before = postgres.Context())
        {
            (await before.Set<OutboxRow>().AnyAsync(row => row.UserId == subject))
                .Should().BeTrue();
        }

        await Erase(subject);

        await using var after = postgres.Context();
        (await after.Set<OutboxRow>().AnyAsync(row => row.UserId == subject))
            .Should().BeFalse();
    }

    /// <summary>Erasing one person touches nobody else.</summary>
    [Fact]
    public async Task The_erasure_hits_only_this_person()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var somebody = Guid.CreateVersion7();
        await Application(subject, company, "submitted");
        var foreign = await Application(somebody, company, "submitted");

        await Erase(subject);

        await using var context = postgres.Context();
        (await context.Applications.AnyAsync(row => row.Id == foreign)).Should().BeTrue();
    }

    /// <summary>Without the secret nothing happens.</summary>
    /// <remarks>
    /// The same answer for a wrong and an unset secret: a caller must not be
    /// able to tell "you guessed wrong" from "nothing is wired up here yet".
    /// </remarks>
    [Fact]
    public async Task Without_the_right_secret_nothing_is_erased()
    {
        var subject = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        var mine = await Application(subject, company, "submitted");

        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", "guessed");

        var response = await browser.PostAsJsonAsync("/erasure", new { user_id = subject });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var context = postgres.Context();
        (await context.Applications.AnyAsync(row => row.Id == mine)).Should().BeTrue();
    }

    /// <summary>A second call is no failure.</summary>
    /// <remarks>
    /// A 404 for "already erased" would look like a failure to the dispatcher,
    /// and it would repeat for ever what was long done (ADR-0027 §4.2).
    /// </remarks>
    [Fact]
    public async Task Erasing_a_second_time_is_2xx_again()
    {
        var subject = Guid.CreateVersion7();

        (await Erase(subject)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Erase(subject)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient With(string token)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private Task<HttpResponseMessage> Erase(Guid subject)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", ErasureSecret);
        return browser.PostAsJsonAsync("/erasure", new { user_id = subject });
    }

    /// <summary>Creates a row in a given state.</summary>
    /// <remarks>
    /// Straight into the table: reaching <c>hired</c> through the routes costs
    /// three calls and checks nothing here that is not checked elsewhere
    /// already.
    /// </remarks>
    private async Task<Guid> Application(Guid subject, Guid company, string state)
    {
        await using var context = postgres.Context();
        var now = DateTime.UtcNow;
        var row = new ApplicationRow
        {
            Id = Guid.CreateVersion7(),
            JobId = Guid.CreateVersion7(),
            TenantId = company,
            SubjectId = subject,
            Message = "Hello.",
            Status = state,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.Applications.Add(row);
        await context.SaveChangesAsync();

        return row.Id;
    }
}
