using System.Reflection;
using FluentAssertions;
using Noelia.Application.Interfaces;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// None Abfrage darf zwischengespeichert werden (ADR-0013).
/// </summary>
/// <remarks>
/// <para>Eine Einwilligung wird <strong>synchron und nie zwischengespeichert</strong>
/// gelesen: eine Rücknahme muss beim nächsten Lesen wirken. Ein Cache ist hier
/// kein Leistungsdetail, sondern ein Regelbruch — die Person hat abgeschaltet,
/// und fünf Minuten lang antwortet der Dienst weiter „darf sehen".</para>
///
/// <para><strong>Warum die Prüfung pauschal ist und nicht nur die Einwilligung
/// meint.</strong> Sie soll nicht die Frage beantworten „ist DIESE Abfrage
/// heikel", sondern verhindern, dass jemand sie beantworten muss. Fast jede
/// Leseabfrage in diesem System hängt mittelbar an einer Einwilligungsprüfung:
/// <c>KandidatenAbfrage</c> fragt den Ledger direkt, <c>ForeignProfileQuery</c>
/// auch, und was heute harmlos aussieht, bekommt morgen ein Tor davor. Wer eine
/// Abfrage wirklich speichern will, muss diesen Test ändern und dabei
/// hinschreiben, warum die Abfrage weder eine Prüfung enthält noch von einer
/// abhängt — auch nicht mittelbar. Genau dieser Satz soll fällig werden.</para>
///
/// <para><strong>Was dieser Test ersetzt.</strong> Fünf Dienste tragen im
/// Quelltext den Satz, hier implementiere niemand <c>ICacheableQuery</c>,
/// weshalb <c>AddCQRS</c> beide Cache-Behaviors aus der Kette lasse — und
/// <c>IAbfrage</c> behauptete sogar, ein Test nagle das fest. <strong>Den Test
/// gab es nicht.</strong> Gemessen in H2, Messung 4: die Zusage stimmte, aber
/// sie hing allein daran, dass niemand je die eine Zeile schreibt.</para>
///
/// <para>Geprüft wird am <em>Typ</em>, nicht am Quelltext: ein regulärer
/// Ausdruck übersähe eine Basisklasse, die die Schnittstelle mitbringt.</para>
/// </remarks>
public class ZwischenspeicherTests
{
    /// <summary>Je Dienst ein Typ aus seiner Application-Schicht.</summary>
    /// <remarks>
    /// Über ihn wird die Assembly gefunden. Ein neuer Dienst gehört hierher —
    /// vergisst ihn jemand, prüft dieser Test ihn nicht, und deshalb steht
    /// unten die Zählung: elf, nicht „mindestens eine".
    /// </remarks>
    public static TheoryData<string, Type> Dienste => new()
    {
        { "identity", typeof(Identity.Application.Anmelden.SessionStateQuery) },
        { "consent", typeof(Consent.Application.Einwilligung.CheckConsentQuery) },
        { "profile", typeof(Profile.Application.Profile.InternalProfileSearchQuery) },
        { "resume", typeof(Resume.Application.Anfragen.MyRequestsQuery) },
        { "portfolio", typeof(Portfolio.Application.Portfolios.MyPortfolioQuery) },
        { "jobs", typeof(Jobs.Application.Jobs.MyJobsQuery) },
        { "applications", typeof(Applications.Application.Applications.MyApplicationsQuery) },
        { "companies", typeof(Companies.Application.Profile.MyProfileQuery) },
        { "transfer", typeof(Transfer.Application.Markt.MyMarketStatusQuery) },
        { "github", typeof(GitHub.Application.Connections.MyConnectionQuery) },
        { "notification", typeof(Notification.Application.Benachrichtigungen.MyInboxQuery) },

        // scout-service haelt die schaerfste Fassung dieser Regel: er sucht
        // Menschen, und ein zwischengespeichertes Ergebnis ueberlebte einen
        // Revocation. Deshalb speichert er die ANFRAGE und nie das Ergebnis
        // (ADR-0036 Entscheidung 4) — und auch die ResumeRequest ist kein Cache.
        { "scout", typeof(Scout.Application.Kandidaten.KandidatenAbfrage) },

        // advisor-service haelt sie in derselben Schaerfe: die STUFE eines
        // Gespraechs steht nur im Ledger und wird bei jedem Lesen dort erfragt
        // (ADR-0037 Entscheidung 2). Eine zwischengespeicherte Antwort waere
        // hier dasselbe wie eine Stufenspalte, nur fluechtig.
        { "advisor", typeof(Advisor.Application.Gespraeche.CompanyConversationsQuery) }
    };

    /// <summary>Kein Typ eines Dienstes implementiert <c>ICacheableQuery</c>.</summary>
    [Theory]
    [MemberData(nameof(Dienste))]
    public void Keine_Abfrage_darf_zwischengespeichert_werden(string service, Type ausDerSchicht)
    {
        var speicherbare = ausDerSchicht.Assembly
            .GetTypes()
            .Where(type => typeof(ICacheableQuery).IsAssignableFrom(type))
            .Where(type => type is { IsInterface: false, IsAbstract: false })
            .Select(type => type.FullName)
            .ToArray();

        speicherbare.Should().BeEmpty(
            "{0} darf nichts zwischenspeichern: eine Ruecknahme muss beim "
            + "naechsten Lesen wirken (ADR-0013)", service);
    }

    /// <summary>
    /// Alle dreizehn Dienste sind wirklich dabei — und die Assemblies sind nicht leer.
    /// </summary>
    /// <remarks>
    /// Ohne das wäre die Reihe grün, wenn jemand einen Dienst aus der Liste
    /// nimmt oder ein Typname nicht mehr auflöst. Eine Prüfung, die nichts
    /// prüft, sieht genauso grün aus wie eine, die trägt.
    /// </remarks>
    [Fact]
    public void Die_Liste_deckt_alle_Dienste_ab()
    {
        var services = Dienste.Select(row => (string)row[0]!).ToArray();

        services.Should().HaveCount(13);
        services.Should().OnlyHaveUniqueItems();

        foreach (var row in Dienste)
        {
            var type = (Type)row[1]!;
            type.Assembly.GetTypes().Should().NotBeEmpty(
                "die Assembly von {0} muss wirklich geladen sein", type.FullName);
        }
    }
}
