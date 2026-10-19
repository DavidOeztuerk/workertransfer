namespace WorkerTransfer.ServiceDefaults.Roles;

/// <summary>What somebody is in a company.</summary>
/// <remarks>
/// Two roles and a "nothing" — the membership table knows no more. There is
/// deliberately no third role and no inheritance: every further one would be a
/// decision about who can lock out whom, and that does not belong in an
/// enumeration but in an ADR.
/// </remarks>
public enum CompanyRole
{
    /// <summary>Does not belong to it — or the company does not exist.</summary>
    /// <remarks>
    /// The two are deliberately the same value. The difference would be a
    /// disclosure about which companies exist.
    /// </remarks>
    None,

    /// <summary>May act for the company.</summary>
    Member,

    /// <summary>May also bind: invite, remove, publish, offer.</summary>
    Admin
}

/// <summary>The spelling in which the role travels over the wire.</summary>
/// <remarks>
/// The same words as in the membership table (<c>admin</c>, <c>member</c>). A
/// second spelling on the way would be the place where a typo silently becomes
/// "not an admin" — and a silent loss of permission looks like a correct
/// refusal.
/// </remarks>
public static class RoleNames
{
    /// <summary>Reads what identity-service wrote.</summary>
    /// <param name="raw">The word off the wire, or <c>null</c>.</param>
    public static CompanyRole Read(string? raw) => raw switch
    {
        "admin" => CompanyRole.Admin,
        "member" => CompanyRole.Member,
        _ => CompanyRole.None
    };
}
