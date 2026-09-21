using Noelia.Core.Identity;
using WorkerTransfer.Identity.Domain.Companies;

namespace WorkerTransfer.Identity.Application.Unternehmen;

/// <summary>What a caller may do inside one company.</summary>
/// <remarks>
/// One place, because the alternative is eight places that each read the role
/// and one of them eventually reads it from the token. A token says which
/// company somebody acts for, never with what rights — so the role is read from
/// the relation, per operation, and withdrawing it takes effect on the next
/// request rather than on the next sign-in.
/// <para>
/// Python enforces none of this: <c>admin</c> against <c>member</c> is checked
/// nowhere, and the interface merely hides entries. That is not rebuilt.
/// </para>
/// </remarks>
public sealed class CompanyAccess(IMembershipRepository mitgliedschaften)
{
    /// <summary>The caller's role, or a refusal that reveals nothing.</summary>
    /// <exception cref="NotAMemberException">
    /// The caller is not a member. Answered the same whether or not the company
    /// exists, so nobody can enumerate companies by probing.
    /// </exception>
    public async Task<MembershipRole> RoleAsync(
        SubjectId subject,
        TenantId company,
        CancellationToken cancellationToken = default) =>
        await mitgliedschaften.RoleOfAsync(subject, company, cancellationToken)
        ?? throw new NotAMemberException();

    /// <summary>
    /// Die Rolle, oder <c>null</c> — ohne zu werfen.
    /// </summary>
    /// <remarks>
    /// Für den Berechtigungshandler: eine Autorisierungsprüfung entscheidet
    /// „ja oder nein" und darf dafür keine Ausnahme brauchen. Wer eine
    /// Begründung will (einladen? entfernen?), nimmt weiterhin
    /// <see cref="AlsAdminAsync"/>.
    /// </remarks>
    /// <param name="wer">Wer fragt.</param>
    /// <param name="firma">Welche Firma.</param>
    /// <param name="cancellationToken">Bricht die Abfrage ab.</param>
    public Task<MembershipRole?> RoleOrNoneAsync(
        SubjectId subject,
        TenantId company,
        CancellationToken cancellationToken = default) =>
        mitgliedschaften.RoleOfAsync(subject, company, cancellationToken);

    /// <summary>The caller's role, refusing anybody who is not an administrator.</summary>
    /// <param name="wer">Who is asking.</param>
    /// <param name="firma">Which company.</param>
    /// <param name="fuers">What they are trying to do — decides which refusal.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    public async Task<MembershipRole> AsAdminAsync(
        SubjectId subject,
        TenantId company,
        Verwaltungsakt fuers,
        CancellationToken cancellationToken = default)
    {
        var role = await RoleAsync(subject, company, cancellationToken);

        if (role != MembershipRole.Admin)
        {
            // 403 rather than 404: this is a statement about the caller, and
            // they already know they are a member. Hiding it would tell them
            // less than they can see.
            throw fuers == Verwaltungsakt.Einladen
                ? new OnlyAdminsMayInviteException()
                : new OnlyAdminsMayRemoveException();
        }

        return role;
    }
}

/// <summary>Which administrative act is being attempted.</summary>
public enum Verwaltungsakt
{
    /// <summary>Inviting somebody, or taking an invitation back.</summary>
    Einladen,

    /// <summary>Removing a member.</summary>
    Entfernen
}
