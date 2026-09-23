using FluentAssertions;
using Noelia.Abstractions.Security.Checks;
using WorkerTransfer.ServiceDefaults.Checks;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// The build-time twin of <c>wt.ki.keine-zahl</c>, across all fourteen services.
/// </summary>
/// <remarks>
/// <para><strong>Why both.</strong> The check in the evidence report tells a
/// human what runs in <em>this instance</em>, and it says so dated and in a
/// document. This test falls <em>before</em> anything ships — and it falls in a
/// service whose report nobody is currently fetching. The check reports, the
/// test stops.</para>
///
/// <para><strong>And it covers the services whose report needs a database.</strong>
/// That is the real reason for this file: identity-service can answer its
/// provider finding only with a table; its vocabulary finding needs none, and
/// it does not get one here.</para>
///
/// <para><strong>An exemption exists exactly once.</strong>
/// <c>AdvisorInfrastructure.Wortausnahmen</c> is public and read by both. Two
/// lists about the same question drift apart, and the quieter of the two is the
/// one that grows.</para>
/// </remarks>
public class VocabularyTests
{
    /// <summary>The fourteen pairs of domain and contracts, as the services hold them.</summary>
    /// <remarks>
    /// <c>identity</c> and <c>consent</c> point at the <em>shared</em> contract
    /// packages: their own <c>.Contracts</c> projects are empty, the boundary
    /// DTOs live under <c>src/shared/</c>. Whoever enters the empty project here
    /// checks one half and reports green over the other.
    /// </remarks>
    public static IReadOnlyList<(string Service, Type Domain, Type Contracts)> Pairs { get; } =
    [
        ("identity", typeof(Identity.Domain.Audit.AuditAction),
         typeof(Contracts.Identity.KiZugangV1)),
        ("consent", typeof(Consent.Domain.Audit.AuditAction),
         typeof(Contracts.Consent.ConsentQuestionV1)),
        ("profile", typeof(Profile.Domain.Profile.PersonProfile),
         typeof(Profile.Contracts.ProfileHitV1)),
        ("resume", typeof(Resume.Domain.Anfragen.RequestState),
         typeof(Resume.Contracts.StationV1)),
        ("portfolio", typeof(Portfolio.Domain.Documents.StoredFile),
         typeof(Portfolio.Contracts.EntryV1)),
        ("jobs", typeof(Jobs.Domain.Jobs.SkillList),
         typeof(Jobs.Contracts.JobV1)),
        ("applications", typeof(Applications.Domain.Applications.ApplicationState),
         typeof(Applications.Contracts.ApplicationV1)),
        ("companies", typeof(Companies.Domain.EmployerProfiles.EmployerProfile),
         typeof(Companies.Contracts.EmployerProfileV1)),
        ("transfer", typeof(Transfer.Domain.Requests.RequestState),
         typeof(Transfer.Contracts.MarketStatusV1)),
        ("notification", typeof(Notification.Domain.Notifications.NotificationKind),
         typeof(Notification.Contracts.NotifyV1)),
        ("github", typeof(GitHub.Domain.Connections.LoginError),
         typeof(GitHub.Contracts.RepositoryV1)),
        ("scout", typeof(Scout.Domain.Searches.Search),
         typeof(Scout.Contracts.HakenV1)),
        ("advisor", typeof(Advisor.Domain.Gespraeche.UebergangNichtErlaubt),
         typeof(Advisor.Contracts.MandateV1)),
        ("assessment", typeof(Assessment.Domain.Cases.InputError),
         typeof(Assessment.Contracts.SetTaskV1))
    ];

    /// <summary>The same fourteen, as xUnit enumerates them.</summary>
    /// <remarks>
    /// One list in two shapes — not two lists. The second would be the one that
    /// stays behind when the fifteenth service arrives.
    /// </remarks>
    public static TheoryData<string, Type, Type> Services
    {
        get
        {
            var data = new TheoryData<string, Type, Type>();

            foreach (var (service, domain, contracts) in Pairs)
            {
                data.Add(service, domain, contracts);
            }

            return data;
        }
    }

    /// <summary>
    /// In no domain and in no contract does a name announce a number about a
    /// human being.
    /// </summary>
    [Theory]
    [MemberData(nameof(Services))]
    public async Task No_service_carries_a_word_that_rates_a_human(
        string service, Type domain, Type contracts)
    {
        var finding = await new NumberCheck(
            [domain.Assembly, contracts.Assembly], Exemptions(service)).RunAsync();

        finding.Status.Should().Be(
            SecurityCheckStatus.Pass,
            $"{service}: {finding.Summary}");
    }

    /// <summary>The detection itself is right — otherwise all of it is green because nothing matches.</summary>
    /// <remarks>
    /// The counter-probe to the fourteen above. Without it, whether the test
    /// measures or merely finds nothing would stay open — the same question
    /// <c>scripts/test-dotnet.sh</c> answers with its count on the screen.
    /// </remarks>
    [Theory]
    [InlineData("Haken", false)]
    [InlineData("Belegstand", false)]
    [InlineData("Seitenlaenge", false)]
    [InlineData("Capability", false)]
    [InlineData("Benefits", false)]
    [InlineData("Availability", false)]
    [InlineData("Passungsgrad", true)]
    [InlineData("PassungsGrad", true)]
    [InlineData("WorkloadPercent", true)]
    public void The_detection_matches_exactly_what_is_meant(string name, bool expected) =>
        // Through the SAME rule the check runs, and not through a rebuilt one:
        // a second rule beside the first is the one that goes wrong first.
        NumberCheck.Strikes(name).Should().Be(expected);

    /// <summary>
    /// And what it does NOT find is pinned too — so nobody believes more of it.
    /// </summary>
    /// <remarks>
    /// A German compound carrying the word at the <em>end</em> escapes the
    /// syllable start: <c>Trefferanzahl</c> is one syllable and does not begin
    /// with <c>anzahl</c>. Pinning that here is the more honest road than
    /// keeping quiet about it — <strong>the name is the signal, not the
    /// rule</strong>, and the closed field sets stand beside it.
    /// </remarks>
    [Theory]
    [InlineData("Trefferanzahl")]
    [InlineData("Gesamtpunkte")]
    public void A_compound_with_the_word_at_the_end_escapes_it(string name) =>
        NumberCheck.Strikes(name).Should().BeFalse(
            "the syllable start does not find it — whoever relies on that "
            + "relies on too little");

    /// <summary>
    /// Thirteen exemptions in the whole tree — three services, three homonyms, each with a reason.
    /// </summary>
    /// <remarks>
    /// <strong>The count stands here so a fourteenth stands out.</strong> An
    /// exemption list grows as long as nobody counts it; this one is counted,
    /// and whoever extends it changes this test in the same commit.
    /// </remarks>
    [Fact]
    public void There_are_thirteen_exemptions_and_none_without_a_reason()
    {
        var all = All();

        all.Should().HaveCount(13);
        all.Should().OnlyContain(exception => exception.Reason.Length > 20);
        all.Select(exception => exception.Name).Should().OnlyHaveUniqueItems();
    }

    /// <summary>The exemptions point at names that really exist.</summary>
    /// <remarks>
    /// An exemption for a name that no longer exists is a licence nobody needs
    /// any more and nobody withdraws. The check itself goes red on it — this
    /// test says so at build time, and that is the difference between "shows up
    /// at the next fetch" and "shows up now".
    /// </remarks>
    [Fact]
    public async Task No_exemption_points_into_the_void()
    {
        foreach (var (service, domain, contracts) in Pairs)
        {
            var finding = await new NumberCheck(
                [domain.Assembly, contracts.Assembly], Exemptions(service))
                .RunAsync();

            finding.Summary.Should().NotContain(
                "zeigen auf Namen", $"{service}: {finding.Summary}");
        }
    }

    /// <summary>Every exemption in the tree, from the three sources that hold them.</summary>
    private static IReadOnlyList<TermException> All() =>
    [
        .. Advisor.Infrastructure.AdvisorInfrastructure.Wortausnahmen,
        .. Applications.Infrastructure.ApplicationsInfrastructure.Wortausnahmen,
        .. Transfer.Infrastructure.TransferInfrastructure.Wortausnahmen
    ];

    /// <summary>One service's exemptions — from its composition root, not from here.</summary>
    private static IReadOnlyList<TermException> Exemptions(string service) => service switch
    {
        "advisor" => Advisor.Infrastructure.AdvisorInfrastructure.Wortausnahmen,
        "applications" => Applications.Infrastructure.ApplicationsInfrastructure.Wortausnahmen,
        "transfer" => Transfer.Infrastructure.TransferInfrastructure.Wortausnahmen,
        _ => []
    };
}
