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
    private const string Loeschgeheimnis = "loesch-geheimnis";

    private WebApplicationFactory<Program> _dienst = null!;

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:transfer", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Geheimnis", Loeschgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IConsentGate, ProbeLedger>());
                services.Replace(ServiceDescriptor.Scoped<IDelivery, Probezustellung>());
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    /// <summary><strong>The system's second load-bearing promise.</strong></summary>
    /// <remarks>
    /// An amount two companies agreed on does not turn the row into an agency's
    /// record: the platform holds no money. If this test falls, the default has
    /// been flipped.
    /// </remarks>
    [Fact]
    public void Der_Schalter_steht_auf_aus()
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
    public void Abgeschlossen_heisst_accepted_oder_completed()
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
        var bezahlt = await Case(anna, company, "completed", gebuehr: 500_000);
        var laufend = await Case(anna, Guid.CreateVersion7(), "talking");
        await Status(anna);

        var response = await Loesche(anna);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var quittung = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        quittung.GetProperty("retained").GetInt32().Should().Be(0);

        await using var context = postgres.Context();
        (await context.Cases.AnyAsync(row => row.Id == bezahlt)).Should().BeFalse();
        (await context.Cases.AnyAsync(row => row.Id == laufend)).Should().BeFalse();
        (await context.MarketStatus.AnyAsync(row => row.Id == anna)).Should().BeFalse();
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
        var abgeschlossenBezahlt = await Case(
            anna, Guid.CreateVersion7(), "completed", gebuehr: 500_000);
        var angenommenBezahlt = await Case(
            anna, Guid.CreateVersion7(), "accepted", gebuehr: 1);
        var abgeschlossenUnbezahlt = await Case(anna, Guid.CreateVersion7(), "completed");
        var angebotBezahlt = await Case(
            anna, Guid.CreateVersion7(), "offered", gebuehr: 500_000);
        var abgesagtBezahlt = await Case(
            anna, Guid.CreateVersion7(), "declined", gebuehr: 500_000);

        await using var context = postgres.Context();
        var bestand = new EfErasureLedger(context);

        var behalten = await bestand.DeleteAsync(
            new SubjectId(anna), keepPaid: true);

        behalten.Should().Be(2);

        var uebrig = await context.Cases
            .Where(row => row.SubjectId == anna)
            .Select(row => row.Id)
            .ToListAsync();

        uebrig.Should().BeEquivalentTo([abgeschlossenBezahlt, angenommenBezahlt]);
        uebrig.Should().NotContain(
            [abgeschlossenUnbezahlt, angebotBezahlt, abgesagtBezahlt]);
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
        var ueberIhn = await ResumeRequest(recruiter, Guid.CreateVersion7(), Guid.CreateVersion7());
        var vonIhm = await ResumeRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), recruiter);

        await Loesche(recruiter);

        await using var context = postgres.Context();

        (await context.Requests.AnyAsync(row => row.Id == ueberIhn)).Should().BeFalse();

        var geblieben = await context.Requests.SingleAsync(row => row.Id == vonIhm);
        geblieben.RequestedBy.Should().BeNull("der Vorgang gehört dem Unternehmen");
    }

    /// <summary>Ein ausstehender Vermerk an ein Konto, das es nicht mehr gibt.</summary>
    [Fact]
    public async Task The_erasure_clears_this_human_beings_outbox()
    {
        var anna = Guid.CreateVersion7();

        await using (var vorher = postgres.Context())
        {
            vorher.Set<OutboxRow>().Add(new OutboxRow
            {
                Id = Guid.CreateVersion7(),
                UserId = anna,
                Kind = "transfer_update",
                CreatedAt = DateTime.UtcNow
            });

            await vorher.SaveChangesAsync();
        }

        await Loesche(anna);

        await using var nachher = postgres.Context();
        (await nachher.Set<OutboxRow>().AnyAsync(row => row.UserId == anna))
            .Should().BeFalse();
    }

    /// <summary>Erasing one human being touches nobody else.</summary>
    [Fact]
    public async Task The_erasure_strikes_only_this_human_being()
    {
        var anna = Guid.CreateVersion7();
        var jemand = Guid.CreateVersion7();
        await Status(anna);
        await Status(jemand);
        var fremder = await Case(jemand, Guid.CreateVersion7(), "talking");

        await Loesche(anna);

        await using var context = postgres.Context();
        (await context.MarketStatus.AnyAsync(row => row.Id == jemand)).Should().BeTrue();
        (await context.Cases.AnyAsync(row => row.Id == fremder)).Should().BeTrue();
    }

    /// <summary>Ohne das Geheimnis passiert nichts.</summary>
    [Fact]
    public async Task Without_the_right_secret_nothing_is_erased()
    {
        var anna = Guid.CreateVersion7();
        await Status(anna);

        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", "geraten");

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

        (await Loesche(anna)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Loesche(anna)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> Loesche(Guid subject)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", Loeschgeheimnis);
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
            Note = "Nur Remote.",
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
    private async Task<Guid> Case(Guid subject, Guid company, string state, long? gebuehr = null)
    {
        await using var context = postgres.Context();
        var now = DateTime.UtcNow;
        var row = new CaseRow
        {
            Id = Guid.CreateVersion7(),
            SubjectId = subject,
            TenantId = company,
            Status = state,
            Message = "Wir hätten da etwas.",
            OfferNote = gebuehr is null ? string.Empty : "Angebot.",
            OfferFeeCents = gebuehr,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.Cases.Add(row);
        await context.SaveChangesAsync();

        return row.Id;
    }

    private async Task<Guid> ResumeRequest(Guid ueberWen, Guid company, Guid frager)
    {
        await using var context = postgres.Context();
        var row = new RequestRow
        {
            Id = Guid.CreateVersion7(),
            SubjectId = ueberWen,
            TenantId = company,
            RequestedBy = frager,
            Status = "PENDING",
            CreatedAt = DateTime.UtcNow
        };

        context.Requests.Add(row);
        await context.SaveChangesAsync();

        return row.Id;
    }
}
