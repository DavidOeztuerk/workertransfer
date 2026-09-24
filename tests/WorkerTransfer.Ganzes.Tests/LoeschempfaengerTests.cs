using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// Whoever holds rows about people MUST be a recipient of the erasure
/// (ADR-0027 §4).
/// </summary>
/// <remarks>
/// The defect this suite prevents does not happen through a wrong decision but
/// through <strong>looking away</strong>: at some point a service grows a new
/// table with a <c>subject_id</c>, nobody thinks of the cascade, and the erasure
/// is once again a promise that is only partly redeemed. It does not show,
/// because nothing goes red.
/// <para>
/// The check is made on the <em>model</em>, not on the source: a regular
/// expression over the contexts would miss a column that comes out of
/// <c>ConfigureOutbox()</c> — and the outbox is exactly such a case.
/// </para>
/// <para>
/// The predecessor was called <c>tests/test_erasure_recipients.py</c> and
/// checked the same thing against the SQLAlchemy metadata. It fell with the
/// Python tree; this suite is its successor, not its replacement.
/// </para>
/// </remarks>
public class LoeschempfaengerTests
{
    /// <summary>The column names that name a natural person.</summary>
    private static readonly string[] Personenspalten = ["subject_id", "user_id"];

    /// <summary>Every service with its context and its name in the cascade.</summary>
    /// <remarks>
    /// The name is the one <c>ErasureRecipient.Services</c> carries and the one
    /// identity-service looks up in <c>Erasure__Adressen__&lt;name&gt;</c>.
    /// </remarks>
    public static TheoryData<string, Type> Dienste => new()
    {
        { "identity", typeof(Identity.Infrastructure.Persistence.IdentityDbContext) },
        { "consent", typeof(Consent.Infrastructure.Persistence.ConsentDbContext) },
        { "profile", typeof(Profile.Infrastructure.Persistence.ProfileDbContext) },
        { "resume", typeof(Resume.Infrastructure.Persistence.ResumeDbContext) },
        { "portfolio", typeof(Portfolio.Infrastructure.Persistence.PortfolioDbContext) },
        { "jobs", typeof(Jobs.Infrastructure.Persistence.JobsDbContext) },
        { "applications", typeof(Applications.Infrastructure.Persistence.ApplicationsDbContext) },
        { "companies", typeof(Companies.Infrastructure.Persistence.CompaniesDbContext) },
        { "transfer", typeof(Transfer.Infrastructure.Persistence.TransferDbContext) },
        { "github", typeof(GitHub.Infrastructure.Persistence.GitHubDbContext) },
        { "notification", typeof(Notification.Infrastructure.Persistence.NotificationDbContext) },
        { "scout", typeof(Scout.Infrastructure.Persistence.ScoutDbContext) },
        { "advisor", typeof(Advisor.Infrastructure.Persistence.AdvisorDbContext) },
        { "assessment", typeof(Assessment.Infrastructure.Persistence.AssessmentDbContext) }
    };

    /// <summary>
    /// Whoever holds a row per person stands on the list — and whoever holds
    /// none does not.
    /// </summary>
    /// <remarks>
    /// Both directions, and the second is not tidiness: an erasure order to a
    /// service with nothing to delete would be an endpoint that says "done"
    /// without ever having done anything — and the cascade would wait for its
    /// acknowledgement.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Dienste))]
    public void A_recipient_is_exactly_whoever_holds_a_row_per_person(string name, Type context)
    {
        var (columns, personenzeilen) = Modell(context);

        // Two signals, and both are needed. Mostly the person stands in a
        // column; with some aggregates the person IS the key, and then the
        // column is simply called `id`. Whoever searches only by column names
        // misses profile, portfolio and github — measured, on the first run of
        // this suite.
        var haeltMenschen =
            columns.Intersect(Personenspalten, StringComparer.Ordinal).Any() || personenzeilen;

        // identity is the origin of the cascade, not its recipient: its own
        // tables fall last and over a note of their own.
        if (name == "identity")
        {
            haeltMenschen.Should().BeTrue("sonst prüft dieser Test nichts");
            ErasureRecipient.Services.Should().NotContain(name);
            return;
        }

        if (haeltMenschen)
        {
            ErasureRecipient.Services.Should().Contain(
                name, $"{name} hält eine Zeile je Mensch");
        }
        else
        {
            ErasureRecipient.Services.Should().NotContain(
                name, $"{name} hält nichts über einen natürlichen Menschen");
        }
    }

    /// <summary>No name on the list without a service behind it.</summary>
    [Fact]
    public void The_list_names_only_services_that_exist()
    {
        var bekannt = Dienste.Select(row => (string)row[0]!).ToHashSet(StringComparer.Ordinal);

        ErasureRecipient.Services.Should().BeSubsetOf(bekannt);
    }

    /// <summary>What a context really creates: column names and subject-keyed tables.</summary>
    private static (IReadOnlyCollection<string> Spalten, bool Personenzeilen) Modell(Type context)
    {
        var type = typeof(DbContextOptionsBuilder<>).MakeGenericType(context);
        var bauer = (DbContextOptionsBuilder)Activator.CreateInstance(type)!;

        // Npgsql, because the contexts map Postgres types (jsonb, citext,
        // enumerations). It never connects — only the model is built.
        bauer.UseNpgsql("Host=modell;Database=modell");

        using var instanz = (DbContext)Activator.CreateInstance(context, bauer.Options)!;

        var typen = instanz.Model.GetEntityTypes().ToList();

        return (
            [
                .. typen
                    .SelectMany(entitaet => entitaet.GetProperties())
                    .Select(eigenschaft => eigenschaft.GetColumnName())
                    .Distinct(StringComparer.Ordinal)
            ],
            typen.Any(entitaet =>
                entitaet.FindAnnotation(SubjectKeyedTable.Annotation)?.Value is true));
    }
}
