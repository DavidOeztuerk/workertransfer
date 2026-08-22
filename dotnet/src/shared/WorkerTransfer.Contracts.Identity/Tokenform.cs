namespace WorkerTransfer.Contracts.Identity;

/// <summary>
/// What a WorkerTransfer access token carries, and under which names.
/// </summary>
/// <remarks>
/// The only thing every service shares about identity. Written down here rather
/// than read off whatever identity-service happens to emit, because a claim
/// name that drifts turns every service anonymous at once — and it does it
/// quietly, since an unreadable claim looks exactly like an absent one.
/// <para>
/// Verified from the signature by each service on its own. There is no call
/// back to identity-service on a request path, and no revocation list: the
/// access token lives fifteen minutes, and taking one back before that is the
/// refresh token's job, whose row lives in the issuer's database.
/// </para>
/// </remarks>
public static class Tokenform
{
    /// <summary>Who is acting. A subject id.</summary>
    public const string Subjekt = "sub";

    /// <summary>
    /// Which company they act for, when they do.
    /// </summary>
    /// <remarks>
    /// Absent — or explicitly null — means acting as a person, which is the
    /// ordinary state (ADR-0017). Never a role: a token says which company
    /// somebody acts for, never with what rights. The role is read from the
    /// membership per operation, so withdrawing it takes effect on the next
    /// read and not on the next sign-in.
    /// </remarks>
    public const string Mandant = "tenant";

    /// <summary>The sign-in this token belongs to, stable across rotation.</summary>
    public const string Sitzung = "sid";

    /// <summary>
    /// Deliberately absent: no address, no name, no roles, no permissions.
    /// </summary>
    /// <remarks>
    /// A JWT travels with every single request, is readable by anything that
    /// sees it, and is not encrypted. An address in it is an address in every
    /// proxy log. Whoever needs one reads it from the service that owns it,
    /// for the person themselves.
    /// </remarks>
    public static IReadOnlySet<string> NiemalsImToken { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "email", "name", "display_name", "roles", "permissions", "phone"
        };
}
