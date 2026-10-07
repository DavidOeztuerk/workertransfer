using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Erasure;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Cases;
using WorkerTransfer.Transfer.Infrastructure.Erasure;
using WorkerTransfer.Transfer.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Transfer.Tests;

/// <summary>The erasure — and the second switch that stands at off.</summary>
[Collection(PostgresCollection.Name)]
public class ErasureTests(Postgres postgres) : IAsyncLifetime
{
    private const string ErasureSecret = "erasure-secret";

    private WebApplicationFactory<Program> _service = null!;

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
            host.UseSetting("Erasure:Secret", ErasureSecret);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IConsentGate, ProbeLedger>());
                services.Replace(ServiceDescriptor.Scoped<IDelivery, ProbeDelivery>());
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    /// <summary><strong>The system's second load-bearing promise.</strong></summary>
    /// <remarks>
    /// An amount two companies agreed on does not turn the row into an agency's
    /// record: the platform holds no money. If this test falls, the default has
    /// been flipped.
    /// </remarks>
    [Fact]
    public void The_switch_stands_at_off()
    {
        Retention.KeepPaid.Should().BeFalse();
    }

    /// <summary>
    /// The delimitation is written out: <c>declined</c> and <c>withdrawn</c> do
    /// <em>not</em> belong to it.
    /// </summary>
    /// <remarks>
    /// A declined case grounds nothing. So this list is deliberately not the
    /// aggregate's set of final states.
    /// </remarks>
    [Fact]
    public void Completed_means_accepted_or_completed()
    {
        Retention.Completed.Should().Equal(
            TransferState.Accepted, TransferState.Completed);
    }

    /// <summary>"Done" means done — the paid transfer too.</summary>
    [Fact]
    public async Task The_default_erases_the_paid_transfer_too()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        var paid = await Case(anna, company, "completed", fee: 500_000);
        var running = await Case(anna, Guid.CreateVersion7(), "talking");
        await Status(anna);
        await using (var arrange = postgres.Context())
        {
            arrange.NoticePeriods.Add(new NoticeRow
            {
                Id = anna, Length = 3, Unit = "month", End = "month_end", UpdatedAt = DateTime.UtcNow
            });
            await arrange.SaveChangesAsync();
        }

        var response = await Erase(anna);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var receipt = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        receipt.GetProperty("retained").GetInt32().Should().Be(0);

        await using var context = postgres.Context();
        (await context.Cases.AnyAsync(row => row.Id == paid)).Should().BeFalse();
        (await context.Cases.AnyAsync(row => row.Id == running)).Should().BeFalse();
        (await context.MarketStatus.AnyAsync(row => row.Id == anna)).Should().BeFalse();
        (await context.NoticePeriods.AnyAsync(row => row.Id == anna)).Should().BeFalse("when she could be gone falls too");
    }

    /// <summary>
    /// The flipped switch covers <strong>exactly one</strong> row class: a
    /// completed deal <em>with</em> a fee.
    /// </summary>
    /// <remarks>
    /// No extension to <c>interested</c>/<c>talking</c>/<c>offered</c> — a
    /// conversation is no contract — and without a fee no trade has come into
    /// being that anything could hang on.
    /// </remarks>
    [Fact]
    public async Task The_flipped_switch_covers_exactly_the_paid_completion()
    {
        var anna = Guid.CreateVersion7();
        var completedPaid = await Case(
            anna, Guid.CreateVersion7(), "completed", fee: 500_000);
        var acceptedPaid = await Case(
            anna, Guid.CreateVersion7(), "accepted", fee: 1);
        var completedUnpaid = await Case(anna, Guid.CreateVersion7(), "completed");
        var offeredPaid = await Case(
            anna, Guid.CreateVersion7(), "offered", fee: 500_000);
        var declinedPaid = await Case(
            anna, Guid.CreateVersion7(), "declined", fee: 500_000);

        await using var context = postgres.Context();
        var ledger = new EfErasureLedger(context);

        var kept = await ledger.DeleteAsync(
            new SubjectId(anna), keepPaid: true);

        kept.Should().Be(2);

        var left = await context.Cases
            .Where(row => row.SubjectId == anna)
            .Select(row => row.Id)
            .ToListAsync();

        left.Should().BeEquivalentTo([completedPaid, acceptedPaid]);
        left.Should().NotContain(
            [completedUnpaid, offeredPaid, declinedPaid]);
    }

    /// <summary>
    /// What was said ABOUT the person falls. What they did FOR their company
    /// stays — without their name.
    /// </summary>
    /// <remarks>
    /// Without that distinction a recruiter giving up their private account
    /// would erase cases belonging to their employer that are about somebody
    /// else entirely.
    /// </remarks>
    [Fact]
    public async Task Having_been_asked_about_falls_having_asked_stays_without_the_name()
    {
        var recruiter = Guid.CreateVersion7();
        var aboutThem = await MarketRequest(recruiter, Guid.CreateVersion7(), Guid.CreateVersion7());
        var byThem = await MarketRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), recruiter);

        await Erase(recruiter);

        await using var context = postgres.Context();

        (await context.Requests.AnyAsync(row => row.Id == aboutThem)).Should().BeFalse();

        var remaining = await context.Requests.SingleAsync(row => row.Id == byThem);
        remaining.RequestedBy.Should().BeNull("the case belongs to the company");
    }

    /// <summary>A pending note to an account that no longer exists.</summary>
    [Fact]
    public async Task The_erasure_clears_this_human_beings_outbox()
    {
        var anna = Guid.CreateVersion7();

        await using (var before = postgres.Context())
        {
            before.Set<OutboxRow>().Add(new OutboxRow
            {
                Id = Guid.CreateVersion7(),
                UserId = anna,
                Kind = "transfer_update",
                CreatedAt = DateTime.UtcNow
            });

            await before.SaveChangesAsync();
        }

        await Erase(anna);

        await using var after = postgres.Context();
        (await after.Set<OutboxRow>().AnyAsync(row => row.UserId == anna))
            .Should().BeFalse();
    }

    /// <summary>Erasing one human being touches nobody else.</summary>
    [Fact]
    public async Task The_erasure_strikes_only_this_human_being()
    {
        var anna = Guid.CreateVersion7();
        var someone = Guid.CreateVersion7();
        await Status(anna);
        await Status(someone);
        var other = await Case(someone, Guid.CreateVersion7(), "talking");

        await Erase(anna);

        await using var context = postgres.Context();
        (await context.MarketStatus.AnyAsync(row => row.Id == someone)).Should().BeTrue();
        (await context.Cases.AnyAsync(row => row.Id == other)).Should().BeTrue();
    }

    /// <summary>Without the secret nothing happens.</summary>
    [Fact]
    public async Task Without_the_right_secret_nothing_is_erased()
    {
        var anna = Guid.CreateVersion7();
        await Status(anna);

        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", "guessed");

        var response = await browser.PostAsJsonAsync("/erasure", new { user_id = anna });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var context = postgres.Context();
        (await context.MarketStatus.AnyAsync(row => row.Id == anna)).Should().BeTrue();
    }

    /// <summary>A second call is not a failure.</summary>
    /// <remarks>
    /// A 404 for "already erased" would look like a failure to the dispatcher,
    /// and it would retry for ever what is long since done (ADR-0027 §4.2).
    /// </remarks>
    [Fact]
    public async Task Erasing_a_second_time_is_2xx_again()
    {
        var anna = Guid.CreateVersion7();

        (await Erase(anna)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Erase(anna)).StatusCode.Should().Be(HttpStatusCode.OK);
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
        var anna = Guid.CreateVersion7();
        var running = await Case(anna, Guid.CreateVersion7(), "talking");
        await Status(anna);

        (await Erase(anna)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Erase(anna)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var context = postgres.Context();
        (await context.Cases.AnyAsync(row => row.Id == running)).Should().BeFalse();
        (await context.MarketStatus.AnyAsync(row => row.Id == anna)).Should().BeFalse();
        (await context.AuditEntries.CountAsync(
                row => row.TargetId == anna && row.Action == "erased"))
            .Should().Be(1, "the trail says once that the erasure ran, and nothing about what it deleted");
    }

    private Task<HttpResponseMessage> Erase(Guid subject)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", ErasureSecret);
        return browser.PostAsJsonAsync("/erasure", new { user_id = subject });
    }

    private async Task Status(Guid subject)
    {
        await using var context = postgres.Context();
        var now = DateTime.UtcNow;

        context.MarketStatus.Add(new MarketRow
        {
            Id = subject,
            Availability = "listening",
            Employed = false,
            Note = "Remote only.",
            CreatedAt = now,
            UpdatedAt = now
        });

        await context.SaveChangesAsync();
    }

    /// <summary>Creates a case in a particular state.</summary>
    /// <remarks>
    /// Straight into the table: reaching a paid completion over the routes costs
    /// five calls and checks nothing here that is not checked elsewhere already.
    /// </remarks>
    private async Task<Guid> Case(Guid subject, Guid company, string state, long? fee = null)
    {
        await using var context = postgres.Context();
        var now = DateTime.UtcNow;
        var row = new CaseRow
        {
            Id = Guid.CreateVersion7(),
            SubjectId = subject,
            TenantId = company,
            Status = state,
            Message = "We might have something for you.",
            OfferNote = fee is null ? string.Empty : "Offer.",
            OfferFeeCents = fee,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.Cases.Add(row);
        await context.SaveChangesAsync();

        return row.Id;
    }

    private async Task<Guid> MarketRequest(Guid about, Guid company, Guid requestedBy)
    {
        await using var context = postgres.Context();
        var row = new RequestRow
        {
            Id = Guid.CreateVersion7(),
            SubjectId = about,
            TenantId = company,
            RequestedBy = requestedBy,
            Status = "PENDING",
            CreatedAt = DateTime.UtcNow
        };

        context.Requests.Add(row);
        await context.SaveChangesAsync();

        return row.Id;
    }
}
