using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Companies;

/// <summary>Where an invitation stands.</summary>
public enum InvitationStatus
{
    /// <summary>Issued, not yet answered.</summary>
    Pending,

    /// <summary>Taken up.</summary>
    Accepted,

    /// <summary>Taken back before it was taken up.</summary>
    Withdrawn
}

/// <summary>Maps <see cref="InvitationStatus"/> to the stored value.</summary>
public static class InvitationStatusNames
{
    /// <summary>The stored value for a status.</summary>
    public static string ToDatabase(InvitationStatus status) => status switch
    {
        InvitationStatus.Pending => "pending",
        InvitationStatus.Accepted => "accepted",
        InvitationStatus.Withdrawn => "withdrawn",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown status.")
    };

    /// <summary>The status for a stored value.</summary>
    public static InvitationStatus FromDatabase(string stored) => stored switch
    {
        "pending" => InvitationStatus.Pending,
        "accepted" => InvitationStatus.Accepted,
        "withdrawn" => InvitationStatus.Withdrawn,
        _ => throw new ArgumentOutOfRangeException(nameof(stored), stored, "Unknown status.")
    };
}

/// <summary>Only an administrator may invite or take an invitation back.</summary>
public sealed class OnlyAdminsMayInviteException()
    : Exception("Only an administrator may invite");

/// <summary>Only an administrator may remove members.</summary>
public sealed class OnlyAdminsMayRemoveException()
    : Exception("Only an administrator may remove members");

/// <summary>The caller is not a member of this company.</summary>
/// <remarks>
/// Deliberately does not say whether the company exists: a caller must not be
/// able to enumerate companies by probing.
/// </remarks>
public sealed class NotAMemberException()
    : Exception("The user is not a member of this tenant");

/// <summary>A company needs at least one administrator.</summary>
/// <remarks>
/// A company without one is not deleted, it is orphaned: the domain stays
/// claimed (ADR-0019), the data stays, and nobody can invite or remove any
/// more. That dead end appears with a single click and can afterwards only be
/// undone by hand in the database — so it is prevented rather than repaired.
/// </remarks>
public sealed class LastAdminMayNotLeaveException()
    : Exception("A company needs at least one administrator; promote someone first");

/// <summary>This invitation means nothing.</summary>
/// <remarks>
/// Without detail: unknown, withdrawn and already accepted are indistinguishable
/// from outside, or the endpoint becomes an oracle about other people's
/// invitations.
/// </remarks>
public sealed class InvitationInvalidException() : Exception("This invitation is not valid");

/// <summary>The invitation is past its time.</summary>
/// <remarks>
/// Expiry may be said out loud: it is a statement about an invitation the
/// recipient is holding anyway, and it is fixable — ask to be invited again. A
/// blanket "invalid" would only leave them at a loss.
/// </remarks>
public sealed class InvitationExpiredException() : Exception("This invitation has expired");

/// <summary>The invitation was issued for a different address.</summary>
public sealed class NotYourInvitationException()
    : Exception("This invitation was issued for a different email address");

/// <summary>An invitation into a company.</summary>
/// <remarks>
/// A company comes into being with exactly one person — whoever created it
/// (ADR-0019). Without a way in it stays that way; this is the way in.
/// <para>
/// Bound to an <em>address</em>, not to an account. That is deliberate: the
/// invited person need not have one yet, and inviting must not reveal whether
/// they do. On accepting, the signed-in person has to hold exactly that address
/// — the server compares, the client asserts nothing.
/// </para>
/// <para>
/// The company's domain plays no part here. It proves who owns the domain and
/// is why a company may exist at all; whom that company then lets in is its own
/// decision — an external recruiter with a foreign address is an entirely
/// ordinary case.
/// </para>
/// </remarks>
public sealed class Invitation
{
    /// <summary>
    /// Seven days. Short enough that a forgotten invitation does not still open
    /// access to applicant data a year later; long enough for a holiday.
    /// </summary>
    public static readonly TimeSpan Lebensdauer = TimeSpan.FromDays(7);

    private Invitation(
        Guid id,
        TenantId tenant,
        string email,
        MembershipRole role,
        SubjectId? invitedBy,
        InvitationStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? acceptedAt)
    {
        Id = id;
        Tenant = tenant;
        Email = email;
        Role = role;
        InvitedBy = invitedBy;
        Status = status;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        AcceptedAt = acceptedAt;
    }

    /// <summary>Which invitation.</summary>
    public Guid Id { get; }

    /// <summary>Into which company.</summary>
    public TenantId Tenant { get; }

    /// <summary>Which address was invited.</summary>
    public string Email { get; }

    /// <summary>With what role.</summary>
    public MembershipRole Role { get; }

    /// <summary>
    /// Who invited, or <c>null</c> once that person deleted their account.
    /// </summary>
    /// <remarks>
    /// The invitation belongs to the company and stays valid; what falls away is
    /// the name on it. <c>null</c> means "the person who invited is gone", not
    /// "nobody invited" (ADR-0027 §2).
    /// </remarks>
    public SubjectId? InvitedBy { get; }

    /// <summary>Where it stands.</summary>
    public InvitationStatus Status { get; private set; }

    /// <summary>When it was issued.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When it stops working.</summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>When it was taken up.</summary>
    public DateTimeOffset? AcceptedAt { get; private set; }

    /// <exception cref="OnlyAdminsMayInviteException">The inviter is not an administrator.</exception>
    public static Invitation Issue(
        TenantId tenant,
        string email,
        MembershipRole role,
        MembershipRole inviterRole,
        SubjectId invitedBy,
        DateTimeOffset now)
    {
        if (inviterRole != MembershipRole.Admin)
        {
            throw new OnlyAdminsMayInviteException();
        }

        return new Invitation(
            Guid.CreateVersion7(), tenant, email, role, invitedBy,
            InvitationStatus.Pending, now, now + Lebensdauer, acceptedAt: null);
    }

    /// <summary>The invitation as a row holds it.</summary>
    public static Invitation Restore(
        Guid id,
        TenantId tenant,
        string email,
        MembershipRole role,
        SubjectId? invitedBy,
        InvitationStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? acceptedAt) =>
        new(id, tenant, email, role, invitedBy, status, createdAt, expiresAt, acceptedAt);

    /// <summary>Takes it up.</summary>
    /// <param name="byEmail">
    /// The address the signed-in person actually holds, read from their account
    /// and never from the request.
    /// </param>
    /// <param name="now">When.</param>
    /// <exception cref="InvitationInvalidException">It was withdrawn or already used.</exception>
    /// <exception cref="InvitationExpiredException">It is past its time.</exception>
    /// <exception cref="NotYourInvitationException">It names a different address.</exception>
    public void Accept(string byEmail, DateTimeOffset now)
    {
        if (Status != InvitationStatus.Pending)
        {
            throw new InvitationInvalidException();
        }

        if (now >= ExpiresAt)
        {
            throw new InvitationExpiredException();
        }

        // The server compares the signed-in person's address with the invited
        // one. A token alone would be enough to let just anybody in — and
        // tokens get forwarded.
        if (!string.Equals(byEmail, Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotYourInvitationException();
        }

        Status = InvitationStatus.Accepted;
        AcceptedAt = now;
    }

    /// <summary>Takes it back.</summary>
    /// <exception cref="OnlyAdminsMayInviteException">The caller is not an administrator.</exception>
    /// <exception cref="InvitationInvalidException">It is no longer pending.</exception>
    public void Withdraw(MembershipRole byRole)
    {
        if (byRole != MembershipRole.Admin)
        {
            throw new OnlyAdminsMayInviteException();
        }

        if (Status != InvitationStatus.Pending)
        {
            // Withdrawing an accepted invitation would not end the membership —
            // that is a different operation, and suggesting it here would be
            // dangerous.
            throw new InvitationInvalidException();
        }

        Status = InvitationStatus.Withdrawn;
    }
}
