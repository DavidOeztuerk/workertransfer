namespace WorkerTransfer.Identity.Api.Permissions;

/// <summary>
/// The permissions a company administration knows.
/// </summary>
/// <remarks>
/// Names, not roles. With them an endpoint says <em>what for</em> it wants to
/// be authorised, and not <em>who</em> that is — who it is decided by
/// <see cref="MembershipPermission"/> when it reads the membership.
/// <para>
/// The prefix <c>Permission:</c> is Noelia's: its
/// <c>PermissionPolicyProvider</c> builds a policy at run time from every
/// policy name spelled that way. That is why none of them has to be registered
/// beforehand — and why it matters that the provider is registered at all:
/// without it nobody answers these names, and the framework refuses
/// <em>every</em> request to the protected endpoint.
/// </para>
/// </remarks>
public static class CompanyPermissions
{
    /// <summary>Noelia's prefix for dynamically resolved policies.</summary>
    public const string Prefix = "Permission:";

    /// <summary>Invite somebody into one's own company.</summary>
    public const string Invite = "company.invite";

    /// <summary>Take an open invitation back.</summary>
    /// <remarks>
    /// The reverse side of <see cref="Invite"/>, and therefore worth the same
    /// permission: whoever may not invite may not take a colleague's invitation
    /// away either. That it may be <em>read</em> is another question —
    /// <c>GET /invitations</c> stays open to every member, because a company
    /// need not keep from its own workforce who it is currently looking for.
    /// </remarks>
    public const string WithdrawInvitation = "company.invitations.withdraw";

    /// <summary>Remove a member from one's own company.</summary>
    public const string Remove = "company.members.remove";

    /// <summary>Set the company's own AI key and provider.</summary>
    /// <remarks>
    /// <c>admin</c>: it decides where the company's drafts are sent and who
    /// pays for them — a decision that binds the company, not daily work.
    /// </remarks>
    public const string Ai = "company.ai";

    /// <summary>The policy name for a permission.</summary>
    public static string Policy(string permission) => Prefix + permission;
}
