namespace WorkerTransfer.Identity.Domain.Audit;

/// <summary>What happened, as the trail records it.</summary>
/// <remarks>
/// A closed set, and deliberately not a free-text column: a trail whose
/// vocabulary can grow at the call site cannot be read years later without
/// reading the code that wrote it.
/// <para>
/// Every member maps to one label of the <c>audit_action</c> enum in the
/// database, snake_case of the member name. <c>PruefspurTests</c> pins all
/// thirteen against the real column, because the rule that produces them is
/// Npgsql's and not ours.
/// </para>
/// </remarks>
public enum AuditAction
{
    /// <summary>An account was created.</summary>
    Register,

    /// <summary>Someone signed in.</summary>
    LoginSuccess,

    /// <summary>A sign-in was refused. Why is in the metadata, never in the answer.</summary>
    LoginFailure,

    /// <summary>A refresh token was exchanged for its successor.</summary>
    TokenRefresh,

    /// <summary>A sign-in was ended.</summary>
    TokenRevoke,

    /// <summary>Someone began acting for a company.</summary>
    TenantSwitch,

    /// <summary>
    /// Someone was refused a company they asked to act for.
    /// </summary>
    /// <remarks>
    /// The refusals are the interesting half of the pair, so they are their own
    /// action rather than a flag on the other one.
    /// </remarks>
    TenantSwitchDenied,

    /// <summary>An address was confirmed.</summary>
    EmailVerified,

    /// <summary>A company was created.</summary>
    CompanyCreated,

    /// <summary>Someone was invited into a company.</summary>
    MemberInvited,

    /// <summary>An invitation was accepted.</summary>
    MemberJoined,

    /// <summary>An open invitation was taken back.</summary>
    InvitationWithdrawn,

    /// <summary>Someone was removed from a company.</summary>
    MemberRemoved
}
