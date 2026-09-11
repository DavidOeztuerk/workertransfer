using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// Wer personenbezogene Zeilen hält, MUSS Empfänger der Löschung sein
/// (ADR-0027 §4).
/// </summary>
/// <remarks>
/// Der Fehler, den diese Reihe verhindert, passiert nicht durch eine falsche
/// Entscheidung, sondern durch <strong>Wegsehen</strong>: irgendwann bekommt
/// ein Dienst eine neue Tabelle mit einer <c>subject_id</c>, niemand denkt an
/// die Kaskade, und die Löschung ist wieder eine Zusage, die nur zum Teil
/// eingelöst wird. Es fällt nicht auf, weil nichts rot wird.
/// <para>
/// Geprüft wird am <em>Modell</em>, nicht am Quelltext: ein regulärer Ausdruck
/// über die Kontexte übersähe eine Spalte, die aus <c>ConfigureOutbox()</c>
/// kommt — und die Outbox ist genau so ein Fall.
/// </para>
/// <para>
/// Der Vorgänger hieß <c>tests/test_erasure_recipients.py</c> und prüfte
/// dasselbe an der SQLAlchemy-Metadata. Er ist mit dem Python-Baum gefallen;
/// diese Reihe ist sein Nachfolger, nicht sein Ersatz.
/// </para>
/// </remarks>
public class LoeschempfaengerTests
{
    /// <summary>Die Spaltennamen, die einen natürlichen Menschen benennen.</summary>
    private static readonly string[] Personenspalten = ["subject_id", "user_id"];

    /// <summary>Jeder Dienst mit seinem Kontext und seinem Namen in der Kaskade.</summary>
    /// <remarks>
    /// Der Name ist der, den <c>Loeschempfaenger.Fremde</c> trägt und den
    /// identity-service in <c>Erasure__Adressen__&lt;name&gt;</c> nachschlägt.
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
        { "advisor", typeof(Advisor.Infrastructure.Persistence.AdvisorDbContext) }
    };

    /// <summary>
    /// Wer eine Zeile je Mensch hält, steht auf der Liste — und wer keine hält,
    /// steht nicht darauf.
    /// </summary>
    /// <remarks>
    /// Beide Richtungen, und die zweite ist nicht Ordnungsliebe: ein
    /// Löschbefehl an einen Dienst ohne etwas zu löschen wäre ein Endpunkt, der
    /// „erledigt" sagt, ohne je etwas getan zu haben — und die Kaskade wartete
    /// auf seine Bestätigung.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Dienste))]
    public void Empfaenger_ist_genau_wer_eine_Zeile_je_Mensch_haelt(string name, Type kontext)
    {
        var (spalten, personenzeilen) = Modell(kontext);

        // Zwei Signale, und beide sind noetig. Meistens steht die Person in
        // einer Spalte; bei manchen Aggregaten IST sie der Schluessel, und dann
        // heisst die Spalte schlicht `id`. Wer nur nach Spaltennamen sucht,
        // uebersieht profile, portfolio und github — gemessen, beim ersten Lauf
        // dieser Reihe.
        var haeltMenschen =
            spalten.Intersect(Personenspalten, StringComparer.Ordinal).Any() || personenzeilen;

        // identity ist der Ursprung der Kaskade, nicht ihr Empfänger: seine
        // eigenen Tabellen fallen zuletzt und über einen eigenen Vermerk.
        if (name == "identity")
        {
            haeltMenschen.Should().BeTrue("sonst prüft dieser Test nichts");
            Loeschempfaenger.Fremde.Should().NotContain(name);
            return;
        }

        if (haeltMenschen)
        {
            Loeschempfaenger.Fremde.Should().Contain(
                name, $"{name} hält eine Zeile je Mensch");
        }
        else
        {
            Loeschempfaenger.Fremde.Should().NotContain(
                name, $"{name} hält nichts über einen natürlichen Menschen");
        }
    }

    /// <summary>Kein Name auf der Liste ohne einen Dienst dahinter.</summary>
    [Fact]
    public void Die_Liste_nennt_nur_Dienste_die_es_gibt()
    {
        var bekannt = Dienste.Select(zeile => (string)zeile[0]!).ToHashSet(StringComparer.Ordinal);

        Loeschempfaenger.Fremde.Should().BeSubsetOf(bekannt);
    }

    /// <summary>Was ein Kontext wirklich anlegt: Spaltennamen und Personenzeilen.</summary>
    private static (IReadOnlyCollection<string> Spalten, bool Personenzeilen) Modell(Type kontext)
    {
        var typ = typeof(DbContextOptionsBuilder<>).MakeGenericType(kontext);
        var bauer = (DbContextOptionsBuilder)Activator.CreateInstance(typ)!;

        // Npgsql, weil die Kontexte Postgres-Typen abbilden (jsonb, citext,
        // Aufzählungen). Es wird nie verbunden — nur das Modell gebaut.
        bauer.UseNpgsql("Host=modell;Database=modell");

        using var instanz = (DbContext)Activator.CreateInstance(kontext, bauer.Options)!;

        var typen = instanz.Model.GetEntityTypes().ToList();

        return (
            [
                .. typen
                    .SelectMany(entitaet => entitaet.GetProperties())
                    .Select(eigenschaft => eigenschaft.GetColumnName())
                    .Distinct(StringComparer.Ordinal)
            ],
            typen.Any(entitaet =>
                entitaet.FindAnnotation(Personenzeile.Anmerkung)?.Value is true));
    }
}
