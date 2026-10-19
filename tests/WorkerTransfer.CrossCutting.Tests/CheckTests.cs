using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Noelia.Abstractions.Security.Checks;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.Profile.Infrastructure.Consent;
using WorkerTransfer.ServiceDefaults.Checks;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>
/// What this field carries is the counter-probe — and it compiles.
/// </summary>
/// <remarks>
/// <para>It stood in PR #87 and survived the migration because the check
/// survived it: <c>wt.ki.keine-zahl</c> has no counterpart in Noelia. What
/// changed is the base class, not the content.</para>
///
/// <para><strong>A real type and not a name in a string.</strong> A
/// counter-probe that feeds the check with <c>"Fit"</c> checks the string
/// search; this one checks that the sweep over an assembly really arrives at
/// the fields.</para>
/// </remarks>
public sealed record CanaryV1(string Name, int Fit);

/// <summary>A ledger gate with a cache in front of it — the counter-probe.</summary>
/// <remarks>
/// It compiles, and that is half the battle: <c>else if (false)</c> would be
/// unreachable code and therefore a <em>build</em> error, and a build error
/// reads in the output like a passing test.
/// </remarks>
public sealed class CachingGate(IConsentGate inner) : IConsentGate
{
    private bool? _remembered;

    /// <inheritdoc />
    public async Task<bool> MaySeeAsync(
        Noelia.Core.Identity.SubjectId subject,
        Noelia.Core.Identity.TenantId company,
        CancellationToken cancellationToken = default) =>
        _remembered ??= await inner.MaySeeAsync(subject, company, cancellationToken);
}

/// <summary>The six checks Noelia does not have — and every one can go red.</summary>
/// <remarks>
/// <para><strong>What is NOT here any more is the runner.</strong> Until
/// ADR-0045 a <c>Nachweislauf</c> of our own drove the checks, caught every
/// exception and put a fixed sentence in its place. Noelia's
/// <c>SecurityCheckRunner</c> does the same, and that is <em>read</em> and not
/// hoped: it has a time limit per check, it turns an exception into a
/// <c>Fail</c>, and in its source stands the reason we had written ourselves —
/// <em>"Never copy an exception or its message: providers often put an
/// endpoint, connection string, key name or token in one."</em> Two runners for
/// one task would be two truths; ours is deleted.</para>
///
/// <para><strong>A counter-probe to every promise</strong>, and each one was
/// driven before it stands here.</para>
/// </remarks>
public class CheckTests
{
    // ------------------------------------------------------- wt.ki.keine-zahl

    /// <summary>Across domain and contracts of profile-service no name carries a number.</summary>
    [Fact]
    public async Task The_number_check_finds_nothing_in_profile_service()
    {
        var finding = await new NumberCheck(
            [typeof(Profile.Domain.Profile.PersonProfile).Assembly,
             typeof(Profile.Contracts.ProfileHitV1).Assembly]).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Pass);
        finding.Id.Should().Be("wt.ki.keine-zahl");
    }

    /// <summary>And it goes red as soon as a contract carries a field <c>Fit</c>.</summary>
    [Fact]
    public async Task The_number_check_goes_red_when_a_contract_carries_a_fit()
    {
        var finding = await new NumberCheck([typeof(CanaryV1).Assembly]).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Fail);
        finding.Summary.Should().Contain("CanaryV1.Fit");
    }

    /// <summary>An empty surface is not a green tick.</summary>
    [Fact]
    public async Task The_number_check_does_not_report_green_over_nothing()
    {
        var finding = await new NumberCheck([]).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Fail);
        finding.Summary.Should().Contain("nichts angesehen");
    }

    /// <summary>§ 87 (1) no. 6 BetrVG travels in the text because it cannot travel as a citation.</summary>
    /// <remarks>
    /// <strong>The gap stands visible instead of concealed.</strong>
    /// <c>RegulatoryRegime</c> knows no national labour law
    /// (<c>bugs/betrvg-hat-kein-regelwerk.md</c>). Putting it under <c>Gdpr</c>
    /// would be a wrong citation in a document a works council reads — worse
    /// than none.
    /// </remarks>
    [Fact]
    public async Task Codetermination_stands_in_the_text_and_not_as_a_wrong_citation()
    {
        var finding = await new NumberCheck([typeof(CanaryV1).Assembly]).RunAsync();

        finding.Remediation.Should().Contain("§ 87 Abs. 1 Nr. 6 BetrVG");

        finding.References.Should().NotContain(
            reference => reference.Article.Contains("87", StringComparison.Ordinal),
            "a labour-law article under a GDPR regime would be a wrong "
            + "citation");
    }

    // ------------------------------------------------------------- wt.ki.naht

    /// <summary>The agreed field set holds, and it stands in the finding.</summary>
    [Fact]
    public async Task The_seam_check_names_the_four_fields_and_the_prompt()
    {
        var finding = await new SeamCheck(
            typeof(DraftSituation),
            ["Headline", "Text", "Skills", "Wish", "Prompt"],
            "für einen Profiltext").RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Pass);
        finding.Summary.Should().Contain("Wish").And.Contain("Prompt");
    }

    /// <summary>One field more than agreed: red.</summary>
    [Fact]
    public async Task The_seam_check_goes_red_when_a_field_is_added()
    {
        var finding = await new SeamCheck(
            typeof(DraftSituation),
            ["Headline", "Text", "Skills", "Prompt"],
            "für einen Profiltext").RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Fail);
        finding.Summary.Should().Contain("zusätzlich Wish");
    }

    // --------------------------------------------------------- wt.ki.anbieter

    /// <summary>Without an entry the subject is absent — and that is not a tick.</summary>
    [Fact]
    public async Task The_provider_check_reports_not_applicable_without_an_entry()
    {
        var finding = await new ProviderCheck([]).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.NotApplicable);
        finding.Status.Should().NotBe(
            SecurityCheckStatus.Pass,
            "a green tick on something that does not apply at all is noise in "
            + "exactly the document meant to cut through noise");
    }

    /// <summary>It counts people and names none.</summary>
    /// <remarks>
    /// <strong>This is the check Noelia cannot have.</strong> Its
    /// <c>noelia.ai.inventory</c> reads the dependencies declared at STARTUP;
    /// here the access stands per person in a table.
    /// </remarks>
    [Fact]
    public async Task The_provider_check_counts_people_instead_of_naming_them()
    {
        var finding = await new ProviderCheck(
        [
            new SampleSource(
            [
                new ProviderRow("anthropic", "api.anthropic.com", ProviderOrigin.Person, 2),
                new ProviderRow("anthropic", "api.anthropic.com", ProviderOrigin.Person, 1),
                new ProviderRow("openai_compatible", "localhost", ProviderOrigin.Person, 1)
            ])
        ]).RunAsync();

        finding.Summary.Should().Contain("3 Mensch(en) auf api.anthropic.com");
        finding.Summary.Should().Contain("eigenes Netz");
        finding.Status.Should().Be(SecurityCheckStatus.Warning);
    }

    /// <summary>Not set up means: no row, not an empty one.</summary>
    [Fact]
    public async Task An_unconfigured_operator_provider_yields_no_row() =>
        (await new OperatorProviderSource(
            "anthropic", "https://api.anthropic.com/v1/messages", configured: false)
            .ReadAsync()).Should().BeEmpty();

    // ---------------------------------------------------- wt.einwilligung.wirkt

    /// <summary>Between the question and the ledger stands the adapter itself.</summary>
    [Fact]
    public async Task The_revocation_check_is_green_without_an_intermediate_type()
    {
        await using var provider = BuildProfileServices().BuildServiceProvider();

        var finding = await new RevocationCheck<IConsentGate>(
            provider, typeof(HttpConsentGate)).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Pass);
        finding.Summary.Should().Contain("kein weiterer Typ");
    }

    /// <summary>And red as soon as somebody puts a cache in front of it.</summary>
    [Fact]
    public async Task The_revocation_check_goes_red_with_a_cache()
    {
        var services = BuildProfileServices();

        services.AddScoped<IConsentGate>(provider =>
            new CachingGate(
                ActivatorUtilities.CreateInstance<HttpConsentGate>(provider)));

        await using var provider = services.BuildServiceProvider();

        var finding = await new RevocationCheck<IConsentGate>(
            provider, typeof(HttpConsentGate)).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Fail);
        finding.Summary.Should().Contain("CachingGate");
        finding.Remediation.Should().Contain("ADR-0013");
    }

    // --------------------------------------------------- wt.loeschung.nachweis

    /// <summary>A shut door is an erasure promise that is not redeemed.</summary>
    [Fact]
    public async Task A_closed_erasure_door_goes_red()
    {
        var finding = await ErasureCheck.AsRecipient(
            "profile", doorConfigured: false, auditTrailPresent: true).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Fail);
        finding.Summary.Should().Contain("Tür ist");
    }

    /// <summary>A service without subject rows is no recipient — and no tick.</summary>
    [Fact]
    public async Task A_service_without_subject_rows_reports_not_applicable() =>
        (await ErasureCheck.WithoutSubjectRows("jobs").RunAsync())
            .Status.Should().Be(SecurityCheckStatus.NotApplicable);

    /// <summary>Open door, but no audit trail: reachable, unevidenced.</summary>
    [Fact]
    public async Task Without_an_audit_trail_a_hint_remains()
    {
        var finding = await ErasureCheck.AsRecipient(
            "portfolio", doorConfigured: true, auditTrailPresent: false).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Warning);
    }

    /// <summary>The origin knows every address — or it says which one is missing.</summary>
    [Fact]
    public async Task An_origin_without_an_address_goes_red()
    {
        var finding = await ErasureCheck.AsOrigin(
            "identity", ["consent", "profile"], ["consent"]).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Fail);
        finding.Summary.Should().Contain("profile");
    }

    // ------------------------------------------------------------- Tooling

    private sealed class SampleSource(IReadOnlyList<ProviderRow> rows) : IProviderSource
    {
        public Task<IReadOnlyList<ProviderRow>> ReadAsync(CancellationToken ct = default) =>
            Task.FromResult(rows);
    }

    /// <summary>The composition root of profile-service, without a database on the wire.</summary>
    private static ServiceCollection BuildProfileServices()
    {
        var services = new ServiceCollection();

        services.AddHttpClient();
        // The gate asks ON BEHALF OF THE CALLER and needs that caller's
        // request for it — without this line it cannot be built at all, and
        // the check reported `Fail` over a fault of this fixture instead of
        // over the tree.
        services.AddHttpContextAccessor();
        services.AddOptions<ConsentSettings>();
        services.AddScoped<IConsentGate, HttpConsentGate>();

        return services;
    }
}
