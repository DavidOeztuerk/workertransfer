using FluentAssertions;
using Noelia.Core.Identity;
using WorkerTransfer.Identity.Domain.Companies;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// The rules the invitation itself keeps, independent of any route.
/// </summary>
/// <remarks>
/// Their own tests because the application layer also refuses an ordinary
/// member before it gets this far — and a rule guarded twice is a rule whose
/// counter-probe lies unless both guards are pinned. Breaking the outer one
/// alone leaves every journey green, which is exactly how a second guard gets
/// deleted years later as "unreachable".
/// </remarks>
public class EinladungsregelnTests
{
    private static readonly TenantId Firma = TenantId.New();
    private static readonly SubjectId Chefin = SubjectId.New();
    private static readonly DateTimeOffset Jetzt = DateTimeOffset.UtcNow;

    private static Invitation Ausgestellt(
        MembershipRole role = MembershipRole.Member,
        string email = "kollege@andere.example") =>
        Invitation.Issue(Firma, email, role, MembershipRole.Admin, Chefin, Jetzt);

    [Fact]
    public void Ein_gewoehnliches_Mitglied_kann_nicht_einladen()
    {
        var einladen = () => Invitation.Issue(
            Firma, "wer@anders.example", MembershipRole.Member,
            MembershipRole.Member, Chefin, Jetzt);

        einladen.Should().Throw<OnlyAdminsMayInviteException>();
    }

    [Fact]
    public void Ein_gewoehnliches_Mitglied_kann_keine_Einladung_zuruecknehmen()
    {
        var invitation = Ausgestellt();

        var zuruecknehmen = () => invitation.Withdraw(MembershipRole.Member);

        zuruecknehmen.Should().Throw<OnlyAdminsMayInviteException>();
        invitation.Status.Should().Be(InvitationStatus.Pending);
    }

    /// <summary>
    /// Tokens get forwarded, and whoever holds the link is not necessarily who
    /// was invited.
    /// </summary>
    [Fact]
    public void Eine_fremde_Adresse_nimmt_die_Einladung_nicht_an()
    {
        var invitation = Ausgestellt();

        var annehmen = () => invitation.Accept("jemand@anders.example", Jetzt);

        annehmen.Should().Throw<NotYourInvitationException>();
        invitation.Status.Should().Be(InvitationStatus.Pending);
    }

    /// <summary>
    /// The address is compared case-insensitively, because that is how the
    /// column compares it. Anything stricter would lock somebody out over the
    /// capital letter their mail client added.
    /// </summary>
    [Fact]
    public void Die_Schreibweise_der_Adresse_entscheidet_nicht()
    {
        var invitation = Ausgestellt(email: "kollege@andere.example");

        invitation.Accept("Kollege@Andere.Example", Jetzt);

        invitation.Status.Should().Be(InvitationStatus.Accepted);
    }

    /// <summary>
    /// Seven days. Short enough that a forgotten invitation does not still open
    /// access to applicant data a year later; long enough for a holiday.
    /// </summary>
    [Fact]
    public void Nach_sieben_Tagen_traegt_eine_Einladung_nicht_mehr()
    {
        var invitation = Ausgestellt();

        var annehmen = () => invitation.Accept(
            "kollege@andere.example", Jetzt + Invitation.Lebensdauer);

        annehmen.Should().Throw<InvitationExpiredException>();
    }

    [Fact]
    public void Eine_zurueckgenommene_Einladung_laesst_sich_nicht_annehmen()
    {
        var invitation = Ausgestellt();
        invitation.Withdraw(MembershipRole.Admin);

        var annehmen = () => invitation.Accept("kollege@andere.example", Jetzt);

        annehmen.Should().Throw<InvitationInvalidException>();
    }

    /// <summary>
    /// Withdrawing an accepted invitation would not end the membership — that
    /// is a different operation, and suggesting it here would be dangerous.
    /// </summary>
    [Fact]
    public void Eine_angenommene_Einladung_laesst_sich_nicht_zuruecknehmen()
    {
        var invitation = Ausgestellt();
        invitation.Accept("kollege@andere.example", Jetzt);

        var zuruecknehmen = () => invitation.Withdraw(MembershipRole.Admin);

        zuruecknehmen.Should().Throw<InvitationInvalidException>();
        invitation.Status.Should().Be(InvitationStatus.Accepted);
    }

    [Fact]
    public void Zweimal_annehmen_geht_nicht()
    {
        var invitation = Ausgestellt();
        invitation.Accept("kollege@andere.example", Jetzt);

        var nochmal = () => invitation.Accept("kollege@andere.example", Jetzt);

        nochmal.Should().Throw<InvitationInvalidException>();
    }
}
