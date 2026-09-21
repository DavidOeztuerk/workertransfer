namespace WorkerTransfer.Companies.Api.Authorization;

/// <summary>The permissions this service knows.</summary>
/// <remarks>
/// <para>The employer profile IS the company to the outside: name,
/// description, postal address, the slug under which the careers page stands
/// publicly. Whoever writes it speaks for everybody else — and at an address
/// reachable without signing in.</para>
///
/// <para><em>Reading</em> deliberately does not stand here: every member may
/// see their own shop window, it is on the net anyway.</para>
/// </remarks>
public static class ShopWindowPermissions
{
    /// <summary>Writing the company's own employer profile.</summary>
    public const string Write = "company.profile.write";
}
