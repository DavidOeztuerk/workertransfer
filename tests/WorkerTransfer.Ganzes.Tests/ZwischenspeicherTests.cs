using System.Reflection;
using FluentAssertions;
using Noelia.Application.Interfaces;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// No query may be cached (ADR-0013).
/// </summary>
/// <remarks>
/// <para>A consent is read <strong>synchronously and never cached</strong>: a
/// withdrawal has to take effect on the next read. A cache here is not a
/// performance detail but a broken rule — the person switched it off, and for
/// five minutes the service goes on answering "may see".</para>
///
/// <para><strong>Why the check is sweeping and does not only mean the
/// consent.</strong> It is not meant to answer the question "is THIS query
/// sensitive", but to prevent anybody from having to answer it. Almost every
/// read query in this system hangs indirectly on a consent check:
/// <c>KandidatenAbfrage</c> asks the ledger directly, <c>ForeignProfileQuery</c>
/// does too, and what looks harmless today gets a gate in front of it tomorrow.
/// Whoever really wants to cache a query has to change this test and, in doing
/// so, write down why the query neither contains a check nor depends on one —
/// not even indirectly. That sentence is exactly what should fall due.</para>
///
/// <para><strong>What this test replaces.</strong> Five services carry the
/// sentence in the source that nobody here implements <c>ICacheableQuery</c>,
/// which is why <c>AddCQRS</c> leaves both cache behaviours out of the chain —
/// and <c>IAbfrage</c> even claimed a test pinned it. <strong>That test did not
/// exist.</strong> Measured in H2, measurement 4: the promise was true, but it
/// hung solely on nobody ever writing that one line.</para>
///
/// <para>The check is made on the <em>type</em>, not on the source: a regular
/// expression would miss a base class that brings the interface with it.</para>
/// </remarks>
public class ZwischenspeicherTests
{
    /// <summary>One type from each service's application layer.</summary>
    /// <remarks>
    /// The assembly is found through it. A new service belongs here — if
    /// somebody forgets it, this test does not check it, and that is why the
    /// count stands below: eleven, not "at least one".
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
        { "companies", typeof(Companies.Application.Profiles.MyProfileQuery) },
        { "transfer", typeof(Transfer.Application.Market.MyMarketStatusQuery) },
        { "github", typeof(GitHub.Application.Connections.MyConnectionQuery) },
        { "notification", typeof(Notification.Application.Notifications.MyInboxQuery) },

        // scout-service holds the sharpest version of this rule: it searches
        // for people, and a cached result would survive a revocation. That is
        // why it stores the REQUEST and never the result (ADR-0036 decision 4)
        // — and the request is no cache either.
        { "scout", typeof(Scout.Application.Kandidaten.KandidatenAbfrage) },

        // advisor-service holds it just as sharply: the STAGE of a
        // conversation stands only in the ledger and is asked for there on every
        // read (ADR-0037 decision 2). A cached answer would be the same thing
        // here as a stage column, only transient.
        { "advisor", typeof(Advisor.Application.Gespraeche.CompanyConversationsQuery) }
    };

    /// <summary>No type of a service implements <c>ICacheableQuery</c>.</summary>
    [Theory]
    [MemberData(nameof(Dienste))]
    public void No_query_may_be_cached(string service, Type ausDerSchicht)
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
    /// All thirteen services are really there — and the assemblies are not
    /// empty.
    /// </summary>
    /// <remarks>
    /// Without that the suite would be green if somebody took a service out of
    /// the list or a type name no longer resolved. A check that checks nothing
    /// looks just as green as one that carries.
    /// </remarks>
    [Fact]
    public void The_list_covers_every_service()
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
